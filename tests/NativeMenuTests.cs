using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using SteamMonitorTray;

internal static class NativeMenuTests {
    [DllImport("user32.dll")] internal static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll")] internal static extern IntPtr GetSubMenu(IntPtr menu, int index);
    [DllImport("user32.dll")] internal static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuStringW(IntPtr menu, uint item, StringBuilder text, int count, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr window, StringBuilder name, int count);
    private static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
    internal static IntPtr PopupWindow() {
        IntPtr result = IntPtr.Zero;
        EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr parameter) {
            StringBuilder name = new StringBuilder(128); GetClassNameW(window, name, name.Capacity);
            if (name.ToString() == "#32768") result = window;
            return true;
        }, IntPtr.Zero);
        return result;
    }
    internal static int PopupCount() {
        int count = 0;
        EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr parameter) {
            StringBuilder name = new StringBuilder(128); GetClassNameW(window, name, name.Capacity);
            if (name.ToString() == "#32768" && Native.IsWindowVisible(window)) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }
    public static void Run() {
        int calls = 0;
        MenuEntry selectedEntry = new MenuEntry("Select", "Select & 中文", delegate { calls++; });
        MenuSnapshot snapshot = new MenuSnapshot(new MenuEntry[] {
            selectedEntry, MenuEntry.Separator(),
            new MenuEntry("Nested", "Nested", children: new MenuEntry[] { new MenuEntry("Checked", "Checked", isChecked: true), new MenuEntry("Disabled", "Disabled", enabled: false) })
        }, false);
        using (NativeMenuTree tree = new NativeMenuTree(snapshot)) {
            Assert(GetMenuItemCount(tree.Handle) == 3, "native root menu and separator created");
            IntPtr child = GetSubMenu(tree.Handle, 2);
            Assert(child != IntPtr.Zero && (GetMenuState(child, 0, 0x400) & 8) != 0 && (GetMenuState(child, 1, 0x400) & 3) != 0, "native submenu check and disabled flags");
            StringBuilder text = new StringBuilder(128); GetMenuStringW(tree.Handle, 0, text, text.Capacity, 0x400);
            Assert(text.ToString() == "Select && 中文", "native Unicode and literal ampersands preserved");
            Assert(tree.Selected(0) == null && tree.Selected(tree.CommandFor("Disabled")) == null && tree.Selected(tree.CommandFor("Select")) == selectedEntry, "cancel and disabled IDs cannot dispatch commands");
        }
        uint before = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
        for (int i = 0; i < 200; i++) using (NativeMenuTree tree = new NativeMenuTree(snapshot)) { }
        uint after = GetGuiResources(Process.GetCurrentProcess().Handle, 1);
        Assert(after <= before + 2, "native root and attached child menu handles are released");
        using (NativeMenu menu = new NativeMenu()) {
            Assert(MenuNative.AreDpiAwarenessContextsEqual(MenuNative.GetWindowDpiAwarenessContext(menu.Owner), new IntPtr(-4)), "UI window actually uses Per-Monitor V2");
            Assert(!Native.IsWindowVisible(menu.Owner), "native popup owner stays hidden");
            Console.WriteLine("INFO UI DPI " + MenuNative.GetDpiForWindow(menu.Owner));
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 150 }) {
                bool observed = false;
                timer.Tick += delegate { observed = menu.IsOpen && IsMenu(menu.ActiveHandle) && PopupWindow() != IntPtr.Zero; timer.Stop(); MenuNative.EndMenu(); };
                timer.Start(); MenuEntry canceled = menu.Show(snapshot, new Point(40, 40));
                Assert(observed && canceled == null && calls == 0 && !menu.IsOpen && menu.ActiveHandle == IntPtr.Zero, "real native popup cancels with no action and releases handles");
            }
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 200 }) {
                int tick = 0;
                Exception workerError = null;
                Thread worker = null;
                timer.Tick += delegate {
                    if (++tick == 1) {
                        IntPtr popup = PopupWindow();
                        worker = new Thread(delegate() {
                            try {
                                AutomationElement root = AutomationElement.FromHandle(popup);
                                AutomationElement first = root.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));
                                ((InvokePattern)first.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                            } catch (Exception ex) { workerError = ex; MenuNative.PostMessageW(menu.Owner, 0x1F, IntPtr.Zero, IntPtr.Zero); }
                        });
                        worker.IsBackground = true; worker.Start();
                    }
                    if (tick > 25 || workerError != null) { timer.Stop(); MenuNative.EndMenu(); }
                };
                timer.Start(); MenuEntry command = menu.Show(snapshot, new Point(40, 40)); timer.Stop();
                if (worker != null) worker.Join(1000);
                if (workerError != null) throw new Exception("Native UI Automation: " + workerError.Message);
                Assert(command == selectedEntry && calls == 0 && menu.ActiveHandle == IntPtr.Zero, "real native item selection returns command only after handles close");
                command.Invoke(); Assert(calls == 1, "selected native command dispatches exactly once");
            }
        }
    }
}
