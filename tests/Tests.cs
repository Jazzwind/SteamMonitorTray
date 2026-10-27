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
    private static void ManagerStart(Profile p) { managerType.GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, new object[] { ManagedRuntime(p), true }); }
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
            Assert(Selection(client, true), "select monitor output before minimizing");
            Native.ShowWindow(client.Window, 7);
            Wait(delegate { return !Native.IsWindowVisible(client.Window); }, "minimize fixture into tray", 5);
            Wait(delegate { return !Selection(client, false); }, "minimize releases blocked console output", 5);
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
        string dataRoot = Path.Combine(root, suffix);
        StartupRegistration registration = new StartupRegistration(Path.Combine(dataRoot, "test-startup.lnk"), Path.Combine(Files.Bin, "SteamMonitorTray.exe"), @"Software\SteamMonitorTray\Tests\" + Guid.NewGuid().ToString("N"), "test-startup.lnk");
        manager = new TrayContext(settings, true, dataRoot, registration); managerType = manager.GetType();
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
    private static bool Selection(SessionClient c, bool select) {
        ProcessStartInfo info = new ProcessStartInfo(Path.Combine(Files.Bin, "InputDriver.exe"), c.Process.Id + (select ? " select unused" : " selected unused"));
        info.UseShellExecute = false; info.CreateNoWindow = true;
        using (Process process = Process.Start(info)) {
            process.WaitForExit();
            if (process.ExitCode != 0 && process.ExitCode != 6) throw new Exception("Selection probe failed: " + process.ExitCode);
            return process.ExitCode == 0;
        }
    }
    private static T UiField<T>(object value, string name) { return (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value); }
    private static void AwaitSettings(System.Threading.Tasks.Task task) {
        Wait(delegate { return task.IsCompleted; }, "settings async save", 15); task.GetAwaiter().GetResult();
    }
    private static void CheckSettingsWorkflow() {
        Settings s = Settings.Defaults(); s.LaunchMode = "AlwaysTray"; s.RetrySeconds = new int[] { 1, 1, 1 };
        Profile a = Fixture("edit-running", false), b = Fixture("edit-stopped", false), c = Fixture("edit-disabled", false); c.AutoStart = false;
        s.Profiles.Add(a); s.Profiles.Add(b); s.Profiles.Add(c); OpenManager(s, "edit-manager-data"); TrayContext context = (TrayContext)manager;
        Wait(delegate { return ManagedStatus(a) == "监测进程运行" && ManagedStatus(b) == "监测进程运行" && File.Exists(Path.Combine(a.WorkingDirectory, "fixture.starts")) && File.Exists(Path.Combine(b.WorkingDirectory, "fixture.starts")); }, "start editing workflow fixtures", 15);
        ManagerAction("Stop", ManagedRuntime(b));
        Wait(delegate { return ManagedStatus(b) == "已停止" && (bool)ManagedRuntime(b).GetType().GetField("Prompt").GetValue(ManagedRuntime(b)); }, "stopped fixture keeps idle PowerShell", 10);
        SettingsForm form = context.OpenSettings();
        Assert(Object.ReferenceEquals(form, context.OpenSettings()), "only one settings window is created");
        Assert(form.IsLocked && UiField<Dictionary<string, TextBox>>(form, "fields")["ConfigFile"].ReadOnly, "live sessions open settings in copyable view mode");
        form.ShowGeneral(); UiField<ComboBox>(form, "language").SelectedIndex = 2; UiField<CheckBox>(form, "startupCheck").Checked = true;
        Assert(ManagedStatus(a) == "监测进程运行" && !File.Exists(Path.Combine(a.WorkingDirectory, "fixture.cleanup")), "general startup and language changes do not stop active monitoring");
        Assert(File.Exists(Path.Combine(root, "edit-manager-data", "test-startup.lnk")), "running-session settings startup action remains isolated to fixture");
        form.ShowProfiles();
        SessionClient client = (SessionClient)ManagedRuntime(a).GetType().GetField("Client").GetValue(ManagedRuntime(a));
        client.Send("Show", "NoActivate"); Wait(delegate { return Native.IsWindowVisible(client.Window); }, "show monitor before edit preparation", 5);
        Assert(Selection(client, true), "select output before stop-and-edit");
        UiField<Button>(form, "prepare").PerformClick();
        Assert(!context.CreateMenuSnapshot().Items.First(item => item.Key == "StartAll").Enabled, "tray cannot start monitors during edit preparation");
        Wait(delegate { return !form.IsLocked && !form.IsPreparing && !AnyManagerAlive(); }, "stop, close all shells and unlock same settings window", 15);
        Assert(File.ReadAllText(Path.Combine(a.WorkingDirectory, "fixture.cleanup")).Contains("CTRL_C_EVENT"), "stop-and-edit performs real Ctrl+C cleanup");
        UiField<Dictionary<string, TextBox>>(form, "fields")["Name"].Text = "Saved fixture name";
        UiField<CheckBox>(form, "enabled").Checked = false;
        AwaitSettings(form.SaveAsync(true));
        Wait(delegate { return ManagedStatus(a) == "监测进程运行" && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length == 2; }, "save resumes originally running profile", 15);
        SessionClient resumedClient = (SessionClient)ManagedRuntime(a).GetType().GetField("Client").GetValue(ManagedRuntime(a));
        Assert(Native.IsWindowVisible(resumedClient.Window), "resume preserves visible state instead of applying tray-only launch policy");
        Assert(!ManagerAlive(b) && File.ReadAllLines(Path.Combine(b.WorkingDirectory, "fixture.starts")).Length == 1 && !File.Exists(Path.Combine(c.WorkingDirectory, "fixture.starts")), "resume leaves stopped and disabled profiles stopped");
        Assert(UiField<Settings>(context, "settings").Profiles[0].Name == "Saved fixture name", "edited profile saved in manager");
        form = context.OpenSettings(); UiField<Button>(form, "prepare").PerformClick();
        Wait(delegate { return !form.IsLocked && !AnyManagerAlive(); }, "prepare save-only workflow", 15);
        AwaitSettings(form.SaveAsync(false));
        Stopwatch quiet = Stopwatch.StartNew(); while (quiet.Elapsed.TotalSeconds < 2) { Application.DoEvents(); Thread.Sleep(20); }
        Assert(!AnyManagerAlive() && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length == 2, "save only leaves monitoring stopped and cancels retries");
        ManagerStart(a); Wait(delegate { return ManagedStatus(a) == "监测进程运行" && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length >= 3; }, "restart before cancel test", 10);
        form = context.OpenSettings(); UiField<Button>(form, "prepare").PerformClick();
        Wait(delegate { return !form.IsLocked && !AnyManagerAlive(); }, "prepare cancel workflow", 15);
        UiField<Dictionary<string, TextBox>>(form, "fields")["Name"].Text = "Unsaved fixture name"; form.Close(); Application.DoEvents();
        Assert(UiField<Settings>(context, "settings").Profiles[0].Name == "Saved fixture name" && !AnyManagerAlive(), "cancel discards draft without resuming monitoring");
        ManagerStart(a); Wait(delegate { return ManagedStatus(a) == "监测进程运行" && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length >= 4; }, "restart before busy prompt test", 10);
        ManagerAction("Stop", ManagedRuntime(a));
        Wait(delegate { return (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "idle prompt for input preservation", 10);
        client = (SessionClient)ManagedRuntime(a).GetType().GetField("Client").GetValue(ManagedRuntime(a));
        string marker = Path.Combine(a.WorkingDirectory, "edit-preserved.txt");
        Input(client, "Set-Content -LiteralPath '" + marker.Replace("'", "''") + "' -Value preserved", false); Thread.Sleep(200);
        form = context.OpenSettings(); UiField<Button>(form, "prepare").PerformClick();
        Wait(delegate { return !form.IsPreparing; }, "unsubmitted input blocks edit preparation", 10);
        Assert(form.IsLocked && client.Alive && !File.Exists(marker), "busy edit keeps shell alive and does not replace input");
        Input(client, "\n", false); Wait(delegate { return File.Exists(marker) && (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "original command remains executable", 10);
        Input(client, "Start-Sleep -Seconds 2\n", false);
        Wait(delegate { return !(bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "arbitrary command executing", 5);
        UiField<Button>(form, "prepare").PerformClick();
        managerType.GetField("editDeadline", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, DateTime.UtcNow.AddSeconds(-1)); ManagerAction("Tick", null);
        Assert(form.IsLocked && !form.IsPreparing && client.Alive, "edit timeout keeps active command and offers retry");
        Wait(delegate { return (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "arbitrary command completes normally", 5);
        UiField<Button>(form, "prepare").PerformClick();
        Wait(delegate { return !form.IsLocked && !AnyManagerAlive(); }, "edit retry succeeds after command completes", 10); form.Close();
        ManagerStart(a);
        Wait(delegate { return ManagedStatus(a) == "监测进程运行" && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length >= 5; }, "ready for delayed-start cancellation regression", 10);
        object pending = ManagedRuntime(a); Type runtimeType = pending.GetType();
        runtimeType.GetField("Running").SetValue(pending, false); runtimeType.GetField("StartSent").SetValue(pending, true);
        runtimeType.GetField("Desired").SetValue(pending, false); runtimeType.GetField("Stopping").SetValue(pending, true);
        // A stop can be requested before a queued Started acknowledgement arrives.
        managerType.GetMethod("Receive", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, new object[] { pending, new Wire("Started", "") });
        Wait(delegate { return (bool)runtimeType.GetField("Prompt").GetValue(pending); }, "delayed-start cancellation returns prompt", 10);
        Assert(File.ReadAllText(Path.Combine(a.WorkingDirectory, "fixture.cleanup")).Contains("count=1"), "pending stop sends exactly one Ctrl+C after Started");
        ManagerAction("Quit", null); Wait(delegate { return !AnyManagerAlive(); }, "workflow manager closes its final PowerShell session", 10); Application.DoEvents(); manager = null;
    }
    private static void CheckAlternateVersions() {
        Settings settings = Settings.Defaults(); settings.LaunchMode = "AlwaysTray";
        Profile a = Fixture("alternate", false); a.AutoStart = false;
        Profile b = a.Copy(); b.Id = Guid.NewGuid().ToString("N"); b.Name = "alternate PR2";
        Profile independent = Fixture("independent", false); independent.AutoStart = false;
        settings.Profiles.Add(a); settings.Profiles.Add(b); settings.Profiles.Add(independent);
        OpenManager(settings, "alternate-manager-data"); TrayContext context = (TrayContext)manager;
        Application.DoEvents(); Assert(!ManagerAlive(a) && !ManagerAlive(b), "unchecked auto-start leaves profiles stopped");
        MenuEntry entry = context.CreateMenuSnapshot().Items.First(item => item.Key == "Profile." + a.Id);
        Assert(entry.Children.First(item => item.Key.EndsWith(".Start")).Enabled, "manual start remains enabled when automatic start is off");
        context.OpenProfileWindow(a.Id);
        Wait(delegate { return ManagerAlive(a) && (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "open profile creates an idle interactive shell", 15);
        SessionClient client = (SessionClient)ManagedRuntime(a).GetType().GetField("Client").GetValue(ManagedRuntime(a));
        Assert(!File.Exists(Path.Combine(a.WorkingDirectory, "fixture.starts")), "opening a shell does not start monitoring");
        context.OpenProfileWindow(a.Id); Assert(Object.ReferenceEquals(client, ManagedRuntime(a).GetType().GetField("Client").GetValue(ManagedRuntime(a))), "restoring window reuses the original session");
        string marker = Path.Combine(a.WorkingDirectory, "preserved-input.txt");
        Input(client, "Set-Content -LiteralPath '" + marker + "' -Value preserved", false);
        context.OpenProfileWindow(b.Id);
        Wait(delegate { return UiField<object>(context, "pendingSwitch") == null; }, "unsubmitted input cancels automatic version switch", 10);
        Assert(client.Alive && !ManagerAlive(b) && !File.Exists(marker), "switch refuses to discard unsubmitted PowerShell input");
        Input(client, "\n", false); Wait(delegate { return File.Exists(marker) && (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "preserved input remains executable", 10);
        Input(client, "Start-Sleep -Seconds 2\n", false); Wait(delegate { return !(bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "other PowerShell command begins", 5);
        context.OpenProfileWindow(b.Id); Assert(client.Alive && !ManagerAlive(b), "switch does not interrupt another PowerShell command");
        Wait(delegate { return (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "other PowerShell command finishes", 8);
        Input(client, "steam_monitor\n", false);
        Wait(delegate { return ManagedStatus(a) == "监测进程运行" && File.Exists(Path.Combine(a.WorkingDirectory, "fixture.starts")); }, "manual shell command updates manager state without auto-start", 15);
        ManagerStart(b); Assert(!ManagerAlive(b), "manual tray start cannot bypass an active output reservation");
        managerType.GetMethod("Receive", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(manager, new object[] { ManagedRuntime(a), new Wire("ConnectionEnded", "") });
        context.OpenProfileWindow(b.Id); Assert(client.Alive && !ManagerAlive(b), "IPC close notification does not release a living host reservation");
        ManagerAction("Stop", ManagedRuntime(a));
        Wait(delegate { return ManagedStatus(a) == "已停止" && (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "manual-only profile stops with Ctrl+C and returns to prompt", 10);
        ManagerStart(b);
        Wait(delegate { return !ManagerAlive(a) && ManagedStatus(b) == "监测进程运行" && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length == 2; }, "starting alternate automatically closes idle old shell and starts exactly once", 15);
        Assert(File.ReadAllText(Path.Combine(a.WorkingDirectory, "fixture.cleanup")).Contains("CTRL_C_EVENT"), "automatic switching preserves completed cleanup and existing output");
        ManagerAction("Stop", ManagedRuntime(b));
        Wait(delegate { return ManagedStatus(b) == "已停止" && (bool)ManagedRuntime(b).GetType().GetField("Prompt").GetValue(ManagedRuntime(b)); }, "alternate stops normally before opening old version", 10);
        Type switchType = typeof(TrayContext).GetNestedType("PendingSwitch", BindingFlags.NonPublic); object timeoutSwitch = Activator.CreateInstance(switchType, true);
        Array owners = Array.CreateInstance(ManagedRuntime(b).GetType(), 1); owners.SetValue(ManagedRuntime(b), 0);
        switchType.GetField("Target", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(timeoutSwitch, ManagedRuntime(a));
        switchType.GetField("Owners", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(timeoutSwitch, owners);
        switchType.GetField("Deadline", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(timeoutSwitch, DateTime.UtcNow.AddSeconds(-1));
        ManagedRuntime(b).GetType().GetField("ExitSent").SetValue(ManagedRuntime(b), true);
        typeof(TrayContext).GetField("pendingSwitch", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(context, timeoutSwitch); ManagerAction("Tick", null);
        Assert(UiField<object>(context, "pendingSwitch") == null && ManagerAlive(b) && !(bool)ManagedRuntime(b).GetType().GetField("ExitSent").GetValue(ManagedRuntime(b)), "timed-out switch preserves the old shell and allows a fresh retry");
        context.OpenProfileWindow(a.Id);
        Wait(delegate { return !ManagerAlive(b) && ManagerAlive(a) && (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "opening alternate automatically hands over to an idle shell", 15);
        Assert(File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length == 2, "window-only switch does not start monitoring");
        context.CreateMenuSnapshot().Items.First(item => item.Key == "StartAll").Invoke();
        Wait(delegate { return ManagedStatus(independent) == "监测进程运行" && File.Exists(Path.Combine(independent.WorkingDirectory, "fixture.starts")); }, "Start All includes manual-only independent profiles", 15);
        Wait(delegate { return ManagedStatus(a) == "监测进程运行" && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length == 3; }, "Start All starts the first shared-output profile", 15);
        Assert(!ManagerAlive(b), "Start All skips later conflicting profiles");
        context.CreateMenuSnapshot().Items.First(item => item.Key == "StartAll").Invoke();
        Application.DoEvents();
        Assert(File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length == 3, "repeated Start All does not duplicate a running monitor");
        settings.Profiles.Reverse();
        string[] menuIds = context.CreateMenuSnapshot().Items.Where(item => item.Key != null && item.Key.StartsWith("Profile.")).Select(item => item.Key).ToArray();
        Assert(menuIds[0] == "Profile." + independent.Id && menuIds[2] == "Profile." + a.Id, "tray menu follows saved profile ordering");
        settings.Profiles.Remove(independent); settings.Profiles.Add(independent);
        context.CreateMenuSnapshot().Items.First(item => item.Key == "StopAll").Invoke();
        Wait(delegate { return ManagedStatus(a) == "已停止" && ManagedStatus(independent) == "已停止" && (bool)ManagedRuntime(a).GetType().GetField("Prompt").GetValue(ManagedRuntime(a)); }, "batch monitors stop gracefully before reordered start", 10);
        context.CreateMenuSnapshot().Items.First(item => item.Key == "StartAll").Invoke();
        Wait(delegate { return ManagedStatus(b) == "监测进程运行" && ManagedStatus(independent) == "监测进程运行" && !ManagerAlive(a) && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length >= 4; }, "reordered Start All chooses the new first alternate and starts independent profiles during idle handover", 15);
        Assert(File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length == 4, "reordered batch handover starts the winner exactly once");
        context.CreateMenuSnapshot().Items.First(item => item.Key == "StopAll").Invoke();
        Wait(delegate { return ManagedStatus(b) == "已停止" && ManagedStatus(independent) == "已停止" && (bool)ManagedRuntime(b).GetType().GetField("Prompt").GetValue(ManagedRuntime(b)); }, "ready to cancel batch during handover", 10);
        settings.Profiles.Remove(a); settings.Profiles.Insert(0, a);
        context.CreateMenuSnapshot().Items.First(item => item.Key == "StartAll").Invoke();
        Assert(UiField<object>(context, "pendingSwitch") != null && ((ICollection)UiField<object>(context, "batchStarts")).Count == 1, "batch waits for idle handover before starting later independent profiles");
        context.CreateMenuSnapshot().Items.First(item => item.Key == "StopAll").Invoke();
        Wait(delegate { return !ManagerAlive(b); }, "cancelled handover lets the already exiting idle shell finish", 10);
        Assert(UiField<object>(context, "pendingSwitch") == null && ((ICollection)UiField<object>(context, "batchStarts")).Count == 0 && !ManagerAlive(a) && ManagedStatus(independent) == "已停止" && File.ReadAllLines(Path.Combine(a.WorkingDirectory, "fixture.starts")).Length == 4, "Stop All cancels pending handover and queued starts without a late restart");
        ManagerAction("Quit", null); Wait(delegate { return !AnyManagerAlive(); }, "alternate-version fixture closes normally", 15); Application.DoEvents(); manager = null;
        Settings failureSettings = Settings.Defaults(); Profile failure = Fixture("creation-failure", false); failure.AutoStart = false; failureSettings.Profiles.Add(failure);
        string failureRoot = Path.Combine(root, "creation-failure-data"); Directory.CreateDirectory(failureRoot); File.WriteAllText(Path.Combine(failureRoot, "sessions"), "block directory creation");
        OpenManager(failureSettings, "creation-failure-data"); ((TrayContext)manager).OpenProfileWindow(failure.Id);
        Assert(!UiField<OutputLeases>(manager, "leases").Contains(failure.Id), "session creation failure releases output reservation");
        ManagerAction("Quit", null); Application.DoEvents(); manager = null;
    }
    private static void CheckBatchPriority() {
        Settings settings = Settings.Defaults(); settings.LaunchMode = "AlwaysTray";
        Profile first = Fixture("batch-first", false), skipped = Fixture("batch-skipped", false), last = Fixture("batch-last", false);
        foreach (Profile p in new[] { first, skipped, last }) p.AutoStart = false;
        string sharedState = Path.Combine(root, "batch-shared-state.json"), sharedCsv = Path.Combine(root, "batch-shared.csv");
        File.AppendAllText(first.ConfigFile, "DISABLE_LOGGING = True\nSTEAM_STATUS_FILE = " + Json.Text(sharedState) + "\n");
        File.AppendAllText(skipped.ConfigFile, "DISABLE_LOGGING = True\nSTEAM_STATUS_FILE = " + Json.Text(sharedState) + "\nCSV_FILE = " + Json.Text(sharedCsv) + "\n");
        File.AppendAllText(last.ConfigFile, "DISABLE_LOGGING = True\nCSV_FILE = " + Json.Text(sharedCsv) + "\n");
        Profile invalid = first.Copy(); invalid.Id = Guid.NewGuid().ToString("N"); invalid.Name = "invalid first"; invalid.Executable += ".missing";
        settings.Profiles.AddRange(new[] { invalid, first, skipped, last });
        OpenManager(settings, "batch-priority-data"); Application.DoEvents();
        ((TrayContext)manager).CreateMenuSnapshot().Items.First(item => item.Key == "StartAll").Invoke();
        Wait(delegate { return ManagedStatus(first) == "监测进程运行" && ManagedStatus(last) == "监测进程运行" && File.Exists(Path.Combine(first.WorkingDirectory, "fixture.starts")) && File.Exists(Path.Combine(last.WorkingDirectory, "fixture.starts")); }, "invalid leading profile and rejected intermediate outputs do not block later valid batch winners", 15);
        Assert(!ManagerAlive(invalid) && !ManagerAlive(skipped), "batch skips invalid profiles and only later direct output collisions");
        ManagerAction("Quit", null); Wait(delegate { return !AnyManagerAlive(); }, "priority batch cleans up with Ctrl+C", 15); Application.DoEvents(); manager = null;
    }
    private static void CheckNativeWindowStartup() {
        Settings settings = Settings.Defaults(); settings.LaunchMode = "AlwaysTray";
        Profile profile = Fixture("native-window-startup", false); profile.AutoStart = false; settings.Profiles.Add(profile);
        OpenManager(settings, "native-window-startup-data"); TrayContext context = (TrayContext)manager;
        bool visible = false; int ticks = 0;
        using (System.Windows.Forms.Timer driver = new System.Windows.Forms.Timer { Interval = 150 }) {
            driver.Tick += delegate {
                if (++ticks == 1) { context.RequestSettings(); return; }
                SettingsForm form = UiField<SettingsForm>(context, "settingsForm");
                if (form != null && form.Visible && Native.IsWindowVisible(form.Handle)) {
                    visible = true; form.Close(); driver.Stop(); ManagerAction("Quit", null);
                } else if (ticks >= 25) { if (form != null) form.Close(); driver.Stop(); ManagerAction("Quit", null); }
            };
            driver.Start(); Application.Run(context);
        }
        manager = null;
        Assert(visible, "first settings form is natively visible in Application.Run despite hidden process startup");
    }
    private static void CheckConsoleColors() {
        var environment = new System.Collections.Specialized.StringDictionary(); environment["CODEX_THREAD_ID"] = "fixture"; environment["NO_COLOR"] = "1"; environment["TERM"] = "dumb";
        Native.NormalizeConsoleEnvironment(environment, null, null); Assert(!environment.ContainsKey("NO_COLOR") && !environment.ContainsKey("TERM"), "known automation monochrome defaults do not leak into interactive shells");
        environment["NO_COLOR"] = "1"; environment["TERM"] = "dumb";
        Native.NormalizeConsoleEnvironment(environment, "1", "custom"); Assert(environment["NO_COLOR"] == "1" && environment["TERM"] == "custom", "explicit Windows console preferences remain authoritative");
        environment.Remove("CODEX_THREAD_ID"); Native.NormalizeConsoleEnvironment(environment, null, null); Assert(environment["NO_COLOR"] == "1", "ordinary terminal NO_COLOR preference is preserved");
        string savedTag = Environment.GetEnvironmentVariable("CODEX_THREAD_ID"), savedColor = Environment.GetEnvironmentVariable("NO_COLOR"), savedTerm = Environment.GetEnvironmentVariable("TERM");
        try {
            Environment.SetEnvironmentVariable("CODEX_THREAD_ID", "fixture"); Environment.SetEnvironmentVariable("NO_COLOR", "1"); Environment.SetEnvironmentVariable("TERM", "dumb");
            Profile fixture = Fixture("console-color", false); fixture.Executable = Path.Combine(Files.Bin, "ColorProbe.exe");
            SessionClient client = Open(fixture, Settings.Defaults()); Input(client, "steam_monitor\n", false);
            Wait(delegate { return File.Exists(Path.Combine(fixture.WorkingDirectory, "fixture.color-attributes.txt")) && Has(client, "Stopped", 0) && Has(client, "Prompt", 0); }, "upstream colour probe finishes", 20);
            Assert(File.ReadAllText(Path.Combine(fixture.WorkingDirectory, "fixture.color.json")).Contains("\"colored\": true") && File.ReadAllText(Path.Combine(fixture.WorkingDirectory, "fixture.color-attributes.txt")) == "green", "installed upstream emits actual green console cells in a managed PowerShell session");
            Assert(Environment.GetEnvironmentVariable("NO_COLOR") == "1", "console normalization does not mutate the tray process environment");
            client.Send("Exit"); Wait(delegate { return !client.Alive; }, "colour probe shell exits normally", 10);
        } finally { Environment.SetEnvironmentVariable("CODEX_THREAD_ID", savedTag); Environment.SetEnvironmentVariable("NO_COLOR", savedColor); Environment.SetEnvironmentVariable("TERM", savedTerm); }
    }
    private static void CheckSettingsMenu() {
        Settings settings = Settings.Defaults(); settings.LaunchMode = "AlwaysTray";
        Profile a = Fixture("settings-menu", false), b = a.Copy(); b.Id = Guid.NewGuid().ToString("N"); b.AutoStart = false;
        settings.Profiles.Add(a); settings.Profiles.Add(b); OpenManager(settings, "settings-menu-data"); TrayContext context = (TrayContext)manager;
        Wait(delegate { return File.Exists(Path.Combine(a.WorkingDirectory, "fixture.starts")); }, "settings menu fixture ready", 15);
        Thread worker = null; Exception workerError = null; bool timedOut = false;
        using (System.Windows.Forms.Timer watchdog = new System.Windows.Forms.Timer { Interval = 12000 })
        using (System.Windows.Forms.Timer select = new System.Windows.Forms.Timer { Interval = 200 }) {
            watchdog.Tick += delegate { timedOut = true; MenuNative.PostMessageW(context.MenuPresenter.Owner, 0x1F, IntPtr.Zero, IntPtr.Zero); };
            select.Tick += delegate {
                select.Stop(); IntPtr popup = NativeMenuTests.PopupWindow();
                worker = new Thread(delegate() {
                    try {
                        AutomationElement rootMenu = AutomationElement.FromHandle(popup);
                        AutomationElement item = rootMenu.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, "设置与多配置管理…"));
                        ((InvokePattern)item.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    } catch (Exception ex) { workerError = ex; MenuNative.PostMessageW(context.MenuPresenter.Owner, 0x1F, IntPtr.Zero, IntPtr.Zero); }
                }); worker.IsBackground = true; worker.Start();
            };
            watchdog.Start(); select.Start(); context.ShowMenu(new System.Drawing.Point(40, 40)); watchdog.Stop();
        }
        if (worker != null) worker.Join(1000); if (workerError != null) throw workerError;
        if (timedOut) throw new Exception("Native Settings menu automation timed out.");
        Wait(delegate { SettingsForm shown = UiField<SettingsForm>(context, "settingsForm"); return shown != null && shown.Visible && Native.IsWindowVisible(shown.Handle); }, "native Settings command opens a natively visible window with two alternate profiles", 10);
        SettingsForm form = UiField<SettingsForm>(context, "settingsForm"); form.Hide();
        context.CreateMenuSnapshot().Items.First(item => item.Key == "Settings").Invoke();
        Assert(form.Visible && Native.IsWindowVisible(form.Handle), "reopening settings restores a hidden existing window");
        form.Close(); context.CreateMenuSnapshot().Items.First(item => item.Key == "Settings").Invoke();
        Assert(!Object.ReferenceEquals(form, UiField<SettingsForm>(context, "settingsForm")) && UiField<SettingsForm>(context, "settingsForm").Visible, "closing and reopening settings creates a fresh visible form");
        UiField<SettingsForm>(context, "settingsForm").Close(); ManagerAction("Quit", null); Wait(delegate { return !AnyManagerAlive(); }, "settings menu fixture exits", 15); Application.DoEvents(); manager = null;
    }
    [STAThread] public static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 1 && args[0] == "--preview") return UiPreview.Run();
        if (args.Length == 1 && args[0] == "--drag-preview") return UiPreview.RunDrag();
        IntPtr hook = IntPtr.Zero;
        try {
            root = Path.Combine(Path.GetDirectoryName(Files.Bin.TrimEnd(Path.DirectorySeparatorChar)), "artifacts", "tests", Guid.NewGuid().ToString("N"));
            Files.EnsurePrivateDirectory(root);
            if (args.Length == 1 && args[0] == "--resize-benchmark") { ResizeTests.Run(root, false); return 0; }
            if (args.Length == 1 && args[0] == "--resize-test") { ResizeTests.Run(root, true); return 0; }
            if (args.Length == 1 && args[0] == "--font-startup") { LocalizationTests.FreshStartup(root); return 0; }
            if (args.Length == 1 && args[0] == "--settings-only") { LocalizationTests.Run(root); SettingsTests.Run(root); return 0; }
            if (args.Length == 1 && args[0] == "--alternates-only") { CheckAlternateVersions(); CheckBatchPriority(); return 0; }
            if (args.Length == 1 && args[0] == "--settings-menu-only") { CheckSettingsMenu(); return 0; }
            if (args.Length == 1 && args[0] == "--colors-only") { CheckConsoleColors(); return 0; }
            if (args.Length == 1 && args[0] == "--native-window-startup") { CheckNativeWindowStartup(); return 0; }
            if (args.Length == 1 && args[0] == "--workflow-only") { CheckSettingsWorkflow(); return 0; }
            Settings s = Settings.Defaults();
            Assert(File.Exists(s.ShellPath) && File.Exists(s.PythonPath), "dependencies discovered");
            ProcessStartInfo nativeProbe = new ProcessStartInfo(Path.Combine(Files.Bin, "SteamMonitorTray.Tests.exe"), "--native-window-startup") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process process = Process.Start(nativeProbe)) {
                var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                Assert(process.WaitForExit(15000), "hidden-process native visibility probe completes"); Console.Write(output.Result);
                if (process.ExitCode != 0) Console.Error.Write(errors.Result);
                Assert(process.ExitCode == 0, "real message-loop settings visibility regression passes");
            }
            ProcessStartInfo fontProbe = new ProcessStartInfo(Path.Combine(Files.Bin, "SteamMonitorTray.Tests.exe"), "--font-startup") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process process = Process.Start(fontProbe)) {
                var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                Assert(process.WaitForExit(15000), "fresh-process font probe completes");
                Console.Write(output.Result); if (process.ExitCode != 0) Console.Error.Write(errors.Result);
                Assert(process.ExitCode == 0, "real tray startup uses correct native font metrics in a fresh process");
            }
            NativeMenuTests.Run();
            LocalizationTests.Run(root);
            SettingsTests.Run(root);
            ResizeTests.Run(root, true);
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
            Assert(Selection(a, true), "fixture enters console selection mode");
            Thread.Sleep(350); Assert(Selection(a, false), "visible console preserves selection for copying");
            a.Send("Hide"); Wait(delegate { return !Native.IsWindowVisible(a.Window); }, "hide original console", 5);
            Wait(delegate { return !Selection(a, false); }, "hide cancels console selection", 5);
            Assert(a.Alive && b.Alive, "visibility changes preserve sessions");
            int before = Count(a); a.Send("Stop");
            Wait(delegate { return File.Exists(Path.Combine(one.WorkingDirectory, "fixture.cleanup")) && Has(a, "Prompt", before); }, "Ctrl+C cleanup and prompt return", 15);
            Assert(File.ReadAllText(Path.Combine(one.WorkingDirectory, "fixture.cleanup")).Contains("CTRL_C_EVENT"), "real Ctrl+C observed and cleanup complete");
            Assert(!File.Exists(Path.Combine(two.WorkingDirectory, "fixture.cleanup")) && b.Alive, "stopping one console leaves the other running");
            string preserved = Path.Combine(one.WorkingDirectory, "preserved.txt");
            Input(a, "Set-Content -LiteralPath '" + preserved.Replace("'", "''") + "' -Value preserved", false);
            Thread.Sleep(200);
            a.Send("Show", "NoActivate"); Wait(delegate { return Native.IsWindowVisible(a.Window); }, "show prompt with unsubmitted input", 5);
            Assert(Selection(a, true), "select output above unsubmitted command");
            before = Count(a); a.Send("Start");
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
            b.Send("Show", "NoActivate"); Wait(delegate { return Native.IsWindowVisible(b.Window); }, "show prompt before selected exit", 5);
            Assert(Selection(b, true), "prompt selection before exit");
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
            object runtime = ManagedRuntime(stopFixture);
            SessionClient managedClient = (SessionClient)runtime.GetType().GetField("Client").GetValue(runtime);
            FieldInfo writerField = typeof(SessionClient).GetField("writer", BindingFlags.NonPublic | BindingFlags.Instance);
            object savedWriter = writerField.GetValue(managedClient);
            try {
                writerField.SetValue(managedClient, null); ManagerAction("Quit", null); ManagerAction("Tick", null);
                Assert(!(bool)runtime.GetType().GetField("ExitSent").GetValue(runtime) && !(bool)managerType.GetField("quitting", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(manager), "failed exit send remains retryable");
            } finally { writerField.SetValue(managedClient, savedWriter); }
            // Lost/stale prompt state must surface a timeout, not a silent exit hang.
            runtime.GetType().GetField("Prompt").SetValue(runtime, false);
            ManagerAction("Quit", null);
            Assert(((TrayContext)manager).CreateMenuSnapshot().Items[0].Text == "正在退出…", "tray exposes pending quit state");
            Assert(!((TrayContext)manager).CreateMenuSnapshot().Items.First(item => item.Key == "StartAll").Enabled, "start action is disabled while quitting");
            managerType.GetField("quitDeadline", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, DateTime.UtcNow.AddSeconds(-1));
            ManagerAction("Tick", null);
            Wait(delegate { return ManagedStatus(stopFixture) == "仍在等待退出" && Native.IsWindowVisible(managedClient.Window); }, "exit timeout restores console without killing session", 5);
            Assert(ManagerAlive(stopFixture), "exit timeout preserves session for recovery");
            runtime.GetType().GetField("Prompt").SetValue(runtime, true);
            Assert(Selection(managedClient, true), "selected console before tray quit");
            ManagerAction("Quit", null); Wait(delegate { return !ManagerAlive(stopFixture); }, "stopped manager exits", 10); Application.DoEvents(); manager = null;
            managed = Settings.Defaults(); managed.LaunchMode = "AlwaysTray";
            Profile quitFixture = Fixture("selected-quit-manager", false); managed.Profiles.Add(quitFixture); OpenManager(managed, "selected-quit-manager-data");
            Wait(delegate { return ManagedStatus(quitFixture) == "监测进程运行" && File.Exists(Path.Combine(quitFixture.WorkingDirectory, "fixture.starts")); }, "start monitor before selected tray quit", 10);
            runtime = ManagedRuntime(quitFixture); managedClient = (SessionClient)runtime.GetType().GetField("Client").GetValue(runtime);
            managedClient.Send("Show", "NoActivate"); Wait(delegate { return Native.IsWindowVisible(managedClient.Window); }, "show running monitor before selected quit", 5);
            Assert(Selection(managedClient, true), "select running output before tray quit");
            ManagerAction("Quit", null); Wait(delegate { return !ManagerAlive(quitFixture); }, "selected running manager gracefully exits", 15);
            Assert(File.ReadAllText(Path.Combine(quitFixture.WorkingDirectory, "fixture.cleanup")).Contains("CTRL_C_EVENT"), "tray quit releases selection and completes real Ctrl+C cleanup");
            Application.DoEvents(); manager = null;
            CheckSettingsWorkflow();
            CheckAlternateVersions();
            CheckBatchPriority();
            CheckSettingsMenu();
            CheckConsoleColors();
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
