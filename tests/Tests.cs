using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Windows.Forms;
using System.Windows.Automation;
using SteamMonitorTray;

internal static class Tests {
    private static readonly List<SessionClient> clients = new List<SessionClient>();
    private static readonly Dictionary<SessionClient, List<Wire>> messages = new Dictionary<SessionClient, List<Wire>>();
    private static readonly HashSet<long> shown = new HashSet<long>();
    private static Native.WinEventDelegate hookCallback;
    private static string root;
    private static object manager;
    private static Type managerType;
    private static object ManagedRuntime(Profile p) { return ((IDictionary)managerType.GetField("runtimes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager))[p.Id]; }
    private static string ManagedStatus(Profile p) { object r = ManagedRuntime(p); return (string)r.GetType().GetField("Status").GetValue(r); }
    private static void ManagerAction(string method, object argument) {
        managerType.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, argument == null ? null : new object[] { argument });
    }
    private static bool ManagerAlive(Profile p) { object r = ManagedRuntime(p); return (bool)r.GetType().GetProperty("Alive").GetValue(r, null); }
    private static bool AnyManagerAlive() {
        if (manager == null) return false;
        IDictionary all = (IDictionary)managerType.GetField("runtimes", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager);
        foreach (object runtime in all.Values) if ((bool)runtime.GetType().GetProperty("Alive").GetValue(runtime, null)) return true;
        return false;
    }
    private static void CheckMinimizedTrayMenu(Profile p) {
        object runtime = ManagedRuntime(p);
        TrayContext context = (TrayContext)manager;
        SessionClient client = (SessionClient)runtime.GetType().GetField("Client").GetValue(runtime);
        int hiddenReports = 0;
        Action<Wire> count = delegate(Wire message) { if (message.Kind == "Hidden") Interlocked.Increment(ref hiddenReports); };
        client.Message += count;
        try {
            client.Send("Show", "NoActivate"); Wait(delegate { return Native.IsWindowVisible(client.Window); }, "show fixture before minimize", 5);
            Native.ShowWindow(client.Window, 7);
            Wait(delegate { return !Native.IsWindowVisible(client.Window); }, "minimize fixture into tray", 5);
            MenuSnapshot snapshot = null;
            IntPtr original = IntPtr.Zero;
            bool stable = false, fresh = false;
            Exception workerError = null;
            Thread worker = null;
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 200 }) {
                int ticks = 0;
                timer.Tick += delegate {
                    if (++ticks == 1) {
                        snapshot = context.MenuPresenter.ActiveSnapshot; original = context.MenuPresenter.ActiveHandle;
                        runtime.GetType().GetField("Status").SetValue(runtime, "测试状态更新"); ManagerAction("RefreshMenu", null);
                        fresh = context.CreateMenuSnapshot().Items.First(item => item.Key == "Profile." + p.Id).Text.Contains("测试状态更新");
                        IntPtr popup = NativeMenuTests.PopupWindow();
                        worker = new Thread(delegate() {
                            try {
                                AutomationElement rootMenu = AutomationElement.FromHandle(popup);
                                AutomationElement profile = rootMenu.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, snapshot.Items.First(item => item.Key == "Profile." + p.Id).Text));
                                object pattern;
                                if (profile.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern)) ((ExpandCollapsePattern)pattern).Expand();
                                else ((InvokePattern)profile.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                                Thread.Sleep(1200);
                                AutomationElement stop = rootMenu.FindFirst(TreeScope.Subtree, new PropertyCondition(AutomationElement.NameProperty, "停止监测"));
                                if (stop == null) throw new Exception("Native Stop submenu item not found.");
                                ((InvokePattern)stop.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                            } catch (Exception ex) { workerError = ex; MenuNative.PostMessageW(context.MenuPresenter.Owner, 0x1F, IntPtr.Zero, IntPtr.Zero); }
                        });
                        worker.IsBackground = true; worker.Start();
                    }
                    if (ticks >= 5 && context.MenuPresenter.IsOpen) {
                        stable = original != IntPtr.Zero && original == context.MenuPresenter.ActiveHandle && NativeMenuTests.IsMenu(original) && Object.ReferenceEquals(snapshot, context.MenuPresenter.ActiveSnapshot) && NativeMenuTests.PopupCount() >= 2;
                    }
                    if (ticks > 35 || workerError != null) { timer.Stop(); MenuNative.EndMenu(); }
                };
                timer.Start(); context.ShowMenu(new System.Drawing.Point(40, 40)); timer.Stop();
            }
            if (worker != null) worker.Join(1000);
            if (workerError != null) throw new Exception("Native submenu UI Automation: " + workerError.Message);
            Assert(stable, "minimized monitor and background refresh preserve native root and submenu");
            Assert(Interlocked.CompareExchange(ref hiddenReports, 0, 0) == 1, "minimize emits exactly one hidden notification");
            Assert(fresh, "next native snapshot sees updated status while active snapshot stays frozen");
            Wait(delegate { return ManagedStatus(p) == "已停止" && File.Exists(Path.Combine(p.WorkingDirectory, "fixture.cleanup")); }, "tray stop remains usable after minimizing", 10);
            Assert(File.ReadAllText(Path.Combine(p.WorkingDirectory, "fixture.cleanup")).Contains("CTRL_C_EVENT"), "native menu command performs real Ctrl+C after minimize");
        } finally { client.Message -= count; }
    }
    private static void OpenManager(Settings settings, string suffix) {
        settings.Language = "zh-Hans";
        Assembly app = Assembly.LoadFrom(Path.Combine(Files.Bin, "SteamMonitorTray.exe")); managerType = app.GetType("SteamMonitorTray.TrayContext");
        manager = Activator.CreateInstance(managerType, new object[] { settings, true, Path.Combine(root, suffix) });
    }
    private static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private static bool Has(SessionClient c, string kind, int after) { lock (messages) return messages[c].Skip(after).Any(m => m.Kind == kind); }
    private static int Count(SessionClient c) { lock (messages) return messages[c].Count; }
    private static void Wait(Func<bool> condition, string label, int seconds) {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds) { Application.DoEvents(); if (condition()) return; Thread.Sleep(20); }
        lock (messages) foreach (SessionClient c in clients) Console.WriteLine("EVENTS " + String.Join(",", messages[c].Select(m => m.Kind + ":" + m.Value + ":" + m.Code)));
        throw new Exception("Timeout: " + label);
    }
    private static Profile Fixture(string name, bool fail) {
        string directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        string conf = Path.Combine(directory, "steam_monitor.conf");
        File.WriteAllText(conf, "TARGET_STEAM_ID = 'fixture'\nFILE_SUFFIX = 'fixture'\nSTEAM_STATUS_FILE = 'state.json'\n" + (fail ? "# TEST_FAIL\n" : ""));
        return new Profile { Name = name, WorkingDirectory = directory, ConfigFile = conf, EnvFile = "", LogDirectory = directory, Executable = Path.Combine(Files.Bin, "FakeMonitor.exe") };
    }
    private static SessionClient Open(Profile p, Settings s) {
        SessionClient c = new SessionClient(); clients.Add(c); messages.Add(c, new List<Wire>());
        c.Message += delegate(Wire m) { lock (messages) messages[c].Add(m); };
        c.Start(p, s, Path.Combine(root, "sessions"));
        Wait(delegate { return Has(c, "Prompt", 0); }, "interactive PowerShell prompt", 30);
        return c;
    }
    private static void Input(SessionClient c, string text, bool ctrlc) {
        ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Files.Bin, "InputDriver.exe"), c.Process.Id + " " + (ctrlc ? "ctrlc" : "text") + " " + Native.Quote(Convert.ToBase64String(Encoding.UTF8.GetBytes(text))));
        info.UseShellExecute = false; info.CreateNoWindow = true;
        using (Process process = Process.Start(info)) { process.WaitForExit(); if (process.ExitCode != 0) throw new Exception("Fixture input failed: " + process.ExitCode); }
    }
    [STAThread] public static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 1 && args[0] == "--preview") return UiPreview.Run();
        IntPtr hook = IntPtr.Zero;
        try {
            root = Path.Combine(Path.GetDirectoryName(Files.Bin.TrimEnd(Path.DirectorySeparatorChar)), "artifacts", "tests", Guid.NewGuid().ToString("N"));
            Files.EnsurePrivateDirectory(root);
            Settings s = Settings.Defaults();
            Assert(File.Exists(s.ShellPath) && File.Exists(s.PythonPath), "dependencies discovered");
            NativeMenuTests.Run();
            LocalizationTests.Run(root);
            StartupTests.Run(root);
            Assert(s.ShowOnLaunch(false) && !s.ShowOnLaunch(true), "default display policy");
            s.LaunchMode = "AlwaysTray"; Assert(!s.ShowOnLaunch(false) && !s.ShowOnLaunch(true), "always tray policy");
            s.LaunchMode = "AlwaysWindow"; Assert(s.ShowOnLaunch(false) && s.ShowOnLaunch(true), "always window policy");
            RetryPolicy retry = new RetryPolicy();
            Assert(retry.Next(s, TimeSpan.Zero) == 5 && retry.Next(s, TimeSpan.Zero) == 15 && retry.Next(s, TimeSpan.Zero) == 30 && retry.Next(s, TimeSpan.Zero) == -1, "finite exponential retry budget");
            Assert(retry.Next(s, TimeSpan.FromSeconds(600)) == 5, "stable lifetime resets retry budget");
            Profile one = Fixture("one", false), two = Fixture("two", false);
            s.Profiles.Add(one); s.Profiles.Add(two); Validation.All(s); Assert(true, "static config validation and independent output paths");
            Profile collision = one.Copy(); collision.Id = Guid.NewGuid().ToString("N"); s.Profiles.Add(collision);
            bool rejected = false; try { Validation.All(s); } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, "duplicate outputs blocked"); s.Profiles.Remove(collision);
            hookCallback = delegate(IntPtr h, uint evt, IntPtr window, int obj, int child, uint thread, uint time) { if (evt == 0x8002 && obj == 0) shown.Add(window.ToInt64()); };
            hook = Native.SetWinEventHook(0x8002, 0x8002, IntPtr.Zero, hookCallback, 0, 0, 0);
            Assert(hook != IntPtr.Zero, "window visibility event observer installed");
            SessionClient a = Open(one, s), b = Open(two, s);
            Application.DoEvents();
            Assert(!Native.IsWindowVisible(a.Window) && !Native.IsWindowVisible(b.Window), "both consoles initially hidden");
            Assert(!shown.Contains(a.Window.ToInt64()) && !shown.Contains(b.Window.ToInt64()), "no initial console show events (no flash)");
            a.Send("Start"); b.Send("Start");
            Wait(delegate { return File.Exists(Path.Combine(one.WorkingDirectory, "fixture.starts")) && File.Exists(Path.Combine(two.WorkingDirectory, "fixture.starts")); }, "both monitors started", 15);
            Assert(Has(a, "Started", 0) && Has(b, "Started", 0), "independent monitoring sessions");
            a.Send("Show", "NoActivate"); Wait(delegate { return Native.IsWindowVisible(a.Window); }, "show original console", 5);
            a.Send("Hide"); Wait(delegate { return !Native.IsWindowVisible(a.Window); }, "hide original console", 5);
            Assert(a.Alive && b.Alive, "visibility changes preserve sessions");
            int before = Count(a); a.Send("Stop");
            Wait(delegate { return File.Exists(Path.Combine(one.WorkingDirectory, "fixture.cleanup")) && Has(a, "Prompt", before); }, "Ctrl+C cleanup and prompt return", 15);
            Assert(File.ReadAllText(Path.Combine(one.WorkingDirectory, "fixture.cleanup")).Contains("CTRL_C_EVENT"), "real Ctrl+C observed and cleanup complete");
            Assert(!File.Exists(Path.Combine(two.WorkingDirectory, "fixture.cleanup")) && b.Alive, "stopping one console leaves the other running");
            string preserved = Path.Combine(one.WorkingDirectory, "preserved.txt");
            Input(a, "Set-Content -LiteralPath '" + preserved.Replace("'", "''") + "' -Value preserved", false);
            Thread.Sleep(200); before = Count(a); a.Send("Start");
            Wait(delegate { return Has(a, "Busy", before); }, "reject start over unsubmitted input", 5);
            Assert(File.ReadAllLines(Path.Combine(one.WorkingDirectory, "fixture.starts")).Length == 1, "unsubmitted command is never overwritten");
            before = Count(a); Input(a, "\n", false);
            Wait(delegate { return File.Exists(preserved) && Has(a, "Prompt", before); }, "preserved arbitrary PowerShell command executes", 5);
            Assert(File.ReadAllText(preserved).Trim() == "preserved", "genuine prompt supports arbitrary commands");
            before = Count(a); Input(a, "steam_monitor\n", false);
            Wait(delegate { return Has(a, "Started", before) && File.ReadAllLines(Path.Combine(one.WorkingDirectory, "fixture.starts")).Length == 2; }, "restart from retained prompt", 15);
            Assert(true, "manual steam_monitor command synchronizes state");
            before = Count(a); Input(a, "", true); b.Send("Stop");
            Wait(delegate { return Has(a, "Prompt", before) && File.Exists(Path.Combine(two.WorkingDirectory, "fixture.cleanup")); }, "stop all gracefully", 15);
            Assert(Has(a, "Interrupted", before) && Has(a, "Stopped", before), "console Ctrl+C synchronizes stopped state");
            Wait(delegate { return Has(b, "Prompt", 4); }, "second prompt", 10);
            a.Send("Exit"); b.Send("Exit");
            Wait(delegate { return !a.Alive && !b.Alive; }, "normal PowerShell and host exit", 15);
            Assert(true, "retained PowerShell sessions exit without forced termination");
            Profile failed = Fixture("failed", true); SessionClient failure = Open(failed, s); failure.Send("Start");
            Wait(delegate { return Has(failure, "Stopped", 0) && Has(failure, "Prompt", 2); }, "failed monitor returns prompt", 10);
            lock (messages) Assert(messages[failure].Any(m => m.Kind == "Stopped" && m.Code == 42), "native failure exit code preserved");
            Input(failure, "exit\n", false);
            Wait(delegate { return !failure.Alive; }, "manual PowerShell exit", 10);
            Assert(Has(failure, "SessionEnded", 0), "manual shell exit synchronizes session state");
            Settings managed = Settings.Defaults(); managed.LaunchMode = "AlwaysTray"; managed.RetrySeconds = new int[] { 1, 1, 1 };
            Profile retryFixture = Fixture("retry-manager", true); managed.Profiles.Add(retryFixture); OpenManager(managed, "manager-data");
            Wait(delegate { return ManagedStatus(retryFixture) == "异常" && File.ReadAllLines(Path.Combine(retryFixture.WorkingDirectory, "fixture.starts")).Length == 4; }, "tray manager performs exactly three retries", 20);
            Assert(true, "actual tray manager retry budget and final error state");
            ManagerAction("Quit", null); Wait(delegate { return !ManagerAlive(retryFixture); }, "tray manager normal exit", 10); Application.DoEvents(); manager = null;
            managed = Settings.Defaults(); managed.LaunchMode = "AlwaysTray"; managed.RetrySeconds = new int[] { 1, 1, 1 };
            Profile stopFixture = Fixture("stop-manager", false); managed.Profiles.Add(stopFixture);
            Profile broken = Fixture("broken-manager", false); broken.ConfigFile += ".missing"; managed.Profiles.Add(broken); OpenManager(managed, "stop-manager-data");
            Wait(delegate { return ManagedStatus(stopFixture) == "监测进程运行" && File.Exists(Path.Combine(stopFixture.WorkingDirectory, "fixture.starts")); }, "tray manager starts monitor", 10);
            Assert(ManagedStatus(broken) == "异常", "invalid profile does not block another valid profile");
            CheckMinimizedTrayMenu(stopFixture);
            Stopwatch quiet = Stopwatch.StartNew(); while (quiet.Elapsed.TotalSeconds < 2) { Application.DoEvents(); Thread.Sleep(20); }
            Assert(File.ReadAllLines(Path.Combine(stopFixture.WorkingDirectory, "fixture.starts")).Length == 1, "manual stop cancels automatic restart");
            ManagerAction("Quit", null); Wait(delegate { return !ManagerAlive(stopFixture); }, "stopped manager exits", 10); Application.DoEvents(); manager = null;
            Console.WriteLine("RESULT all checks passed"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex.Message); return 1; }
        finally {
            if (manager != null) ManagerAction("Quit", null);
            if (hook != IntPtr.Zero) Native.UnhookWinEvent(hook);
            foreach (SessionClient c in clients) {
                if (c.Alive) { c.Send("Stop"); Thread.Sleep(500); c.Send("Exit"); }
                // On failure, keep the authenticated connection alive long enough for graceful exit.
            }
            Stopwatch wait = Stopwatch.StartNew(); while ((clients.Any(c => c.Alive) || AnyManagerAlive()) && wait.Elapsed.TotalSeconds < 8) { Application.DoEvents(); Thread.Sleep(50); }
            foreach (SessionClient c in clients) c.Dispose();
        }
    }
}
