using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SteamMonitorTray {
    // One snapshot belongs to one popup. Background updates never mutate it.
    internal sealed class MenuEntry {
        internal readonly string Key, Text;
        internal readonly bool Enabled, Checked;
        internal readonly ReadOnlyCollection<MenuEntry> Children;
        private readonly Action action;
        internal MenuEntry(string key, string text, Action action = null, bool enabled = true, bool isChecked = false, params MenuEntry[] children) {
            Key = key; Text = text; this.action = action; Enabled = enabled; Checked = isChecked;
            Children = Array.AsReadOnly((MenuEntry[])children.Clone());
        }
        internal bool IsSeparator { get { return Text == null; } }
        internal void Invoke() { if (Enabled && Children.Count == 0 && action != null) action(); }
        internal static MenuEntry Separator() { return new MenuEntry(null, null); }
    }
    internal sealed class MenuSnapshot {
        internal readonly ReadOnlyCollection<MenuEntry> Items;
        internal readonly bool RightToLeft;
        internal MenuSnapshot(IEnumerable<MenuEntry> items, bool rightToLeft) { Items = new List<MenuEntry>(items).AsReadOnly(); RightToLeft = rightToLeft; }
    }
    internal static class MenuNative {
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr item, string text);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll", SetLastError = true)] internal static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
        [DllImport("kernel32.dll")] internal static extern void SetLastError(uint error);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EndMenu();
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AreDpiAwarenessContextsEqual(IntPtr one, IntPtr two);
    }
    internal sealed class NativeMenuTree : IDisposable {
        internal IntPtr Handle { get; private set; }
        private readonly Dictionary<uint, MenuEntry> commands = new Dictionary<uint, MenuEntry>();
        internal NativeMenuTree(MenuSnapshot snapshot) { Handle = Build(snapshot.Items); }
        internal static string DisplayText(string text) { return text.Replace("&", "&&").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
        private IntPtr Build(IEnumerable<MenuEntry> items) {
            IntPtr menu = MenuNative.CreatePopupMenu();
            if (menu == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                foreach (MenuEntry entry in items) {
                    uint flags = entry.IsSeparator ? 0x800U : ((entry.Enabled ? 0U : 3U) | (entry.Checked ? 8U : 0U));
                    IntPtr child = IntPtr.Zero;
                    UIntPtr value = UIntPtr.Zero;
                    if (entry.Children.Count > 0) { child = Build(entry.Children); value = UIntPtr.Size == 8 ? new UIntPtr(unchecked((ulong)child.ToInt64())) : new UIntPtr(unchecked((uint)child.ToInt32())); flags |= 0x10U; }
                    else if (!entry.IsSeparator) { uint id = (uint)commands.Count + 1; commands.Add(id, entry); value = new UIntPtr(id); }
                    if (!MenuNative.AppendMenuW(menu, flags, value, entry.IsSeparator ? null : DisplayText(entry.Text))) {
                        int error = Marshal.GetLastWin32Error();
                        // An unattached child is not owned by the root yet.
                        if (child != IntPtr.Zero) MenuNative.DestroyMenu(child);
                        throw new Win32Exception(error);
                    }
                }
                return menu;
            } catch { MenuNative.DestroyMenu(menu); throw; }
        }
        internal MenuEntry Selected(uint id) { MenuEntry entry; return commands.TryGetValue(id, out entry) && entry.Enabled && entry.Children.Count == 0 ? entry : null; }
        internal uint CommandFor(string key) { foreach (KeyValuePair<uint, MenuEntry> pair in commands) if (pair.Value.Key == key) return pair.Key; return 0; }
        public void Dispose() { if (Handle != IntPtr.Zero) { MenuNative.DestroyMenu(Handle); Handle = IntPtr.Zero; } }
    }
    internal sealed class NativeMenu : IDisposable {
        private sealed class MenuHost : NativeWindow {
            internal MenuHost() { CreateHandle(new CreateParams { Caption = "SteamMonitorTray.MenuHost", Style = unchecked((int)0x80000000), ExStyle = 0x80 }); }
        }
        private readonly MenuHost host = new MenuHost();
        private bool disposed;
        internal bool IsOpen { get; private set; }
        internal MenuSnapshot ActiveSnapshot { get; private set; }
        internal IntPtr ActiveHandle { get; private set; }
        internal IntPtr Owner { get { return host.Handle; } }
        internal MenuEntry Show(MenuSnapshot snapshot, Point position) {
            if (disposed || IsOpen) return null;
            IsOpen = true; ActiveSnapshot = snapshot;
            try {
                using (NativeMenuTree tree = new NativeMenuTree(snapshot)) {
                    ActiveHandle = tree.Handle;
                    // Keep the owner hidden, but associate it with the target monitor.
                    MenuNative.SetWindowPos(host.Handle, IntPtr.Zero, position.X, position.Y, 0, 0, 0x15);
                    MenuNative.SetForegroundWindow(host.Handle);
                    uint flags = 0x182U; // RETURNCMD | NONOTIFY | RIGHTBUTTON
                    if (snapshot.RightToLeft) flags |= 0x8000U | 8U;
                    else if (MenuNative.GetSystemMetrics(40) != 0) flags |= 8U;
                    MenuNative.SetLastError(0);
                    uint selected = MenuNative.TrackPopupMenuEx(tree.Handle, flags, position.X, position.Y, host.Handle, IntPtr.Zero);
                    int error = Marshal.GetLastWin32Error();
                    if (selected == 0 && error != 0) throw new Win32Exception(error);
                    return tree.Selected(selected);
                }
            } finally {
                MenuNative.PostMessageW(host.Handle, 0, IntPtr.Zero, IntPtr.Zero);
                ActiveHandle = IntPtr.Zero; ActiveSnapshot = null; IsOpen = false;
                if (disposed) host.DestroyHandle();
            }
        }
        public void Dispose() {
            if (disposed) return;
            disposed = true;
            if (IsOpen) MenuNative.EndMenu(); else host.DestroyHandle();
        }
    }
}
