using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SteamMonitorTray.Tests")]

namespace SteamMonitorTray {
    internal static class Program {
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Files.Log("ui", e.Exception.GetType().Name); MessageBox.Show(L.Text("界面操作失败，请检查本地包装器日志。监测不会因此被强制终止。"), "SteamMonitorTray"); };
            try {
                Files.EnsurePrivateDirectory(Files.DataRoot);
                if (args.Length == 1 && args[0] == "--install-startup") { Startup.Install(); return 0; }
                if (args.Length == 1 && args[0] == "--uninstall-startup") { Startup.Remove(); return 0; }
                Settings settings = File.Exists(Files.SettingsPath) ? Json.Read<Settings>(Files.SettingsPath) : Settings.Defaults();
                L.SetLanguage(settings.Language);
                bool owns;
                using (Mutex mutex = new Mutex(true, "Local\\SteamMonitorTray." + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value, out owns)) {
                    if (!owns) { MessageBox.Show(L.Text("SteamMonitorTray 已在运行，请使用通知区域图标。导入配置前请先从托盘退出。"), "SteamMonitorTray"); return args.Length == 2 ? 2 : 0; }
                    if (args.Length == 2 && args[0] == "--import-directory") {
                        Profile profile = Files.FromDirectory(args[1]); profile.Name = L.Text("监测 {0}", settings.Profiles.Count + 1);
                        settings.Profiles.Add(profile); Validation.All(settings); Json.Save(Files.SettingsPath, settings); return 0;
                    }
                    Application.Run(new TrayContext(settings, Array.IndexOf(args, "--startup") >= 0, Files.DataRoot));
                }
                return 0;
            } catch (Exception ex) {
                Files.Log("app", ex.GetType().Name);
                MessageBox.Show(L.Text("操作失败：{0}", SafeError(ex)), "SteamMonitorTray", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }
        public static string SafeError(Exception ex) {
            if (ex is InvalidDataException) return L.Text(ex.Message);
            return L.Text("{0}。请检查路径、权限和程序依赖。", ex.GetType().Name);
        }
    }
    internal sealed class Runtime {
        public Profile Profile;
        public SessionClient Client;
        public string Status = "已停止";
        public bool Running, Desired, NeedsStart, StartSent, Prompt, Stopping, ExitSent, EndHandled;
        public DateTime Started, StopDeadline, RetryAt;
        public RetryPolicy Retry = new RetryPolicy();
        public bool Alive { get { return Client != null && Client.Alive; } }
    }
    internal sealed class TrayContext : ApplicationContext {
        private Settings settings;
        private readonly string dataRoot;
        private readonly bool startup;
        private readonly StartupRegistration startupRegistration;
        private readonly NotifyIcon icon;
        private readonly Icon runningIcon = StatusIcon(Color.FromArgb(34, 160, 85), "play"), stoppedIcon = StatusIcon(Color.SlateGray, "stop"), warningIcon = StatusIcon(Color.DarkOrange, "warning");
        private readonly Control dispatcher = new Control();
        private readonly Dictionary<string, Runtime> runtimes = new Dictionary<string, Runtime>();
        private readonly System.Windows.Forms.Timer timer;
        private bool quitting;
        private DateTime quitDeadline;
        internal readonly NativeMenu MenuPresenter = new NativeMenu();
        private static Icon StatusIcon(Color color, string state) {
            using (Bitmap bitmap = new Bitmap(32, 32)) {
                using (Graphics g = Graphics.FromImage(bitmap)) {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                    using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, 1, 1, 30, 30);
                    if (state == "play") g.FillPolygon(Brushes.White, new Point[] { new Point(12, 8), new Point(24, 16), new Point(12, 24) });
                    else if (state == "stop") g.FillRectangle(Brushes.White, 10, 10, 12, 12);
                    else { g.FillRectangle(Brushes.White, 14, 7, 4, 12); g.FillEllipse(Brushes.White, 14, 22, 4, 4); }
                }
                IntPtr native = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(native).Clone(); } finally { Native.DestroyIcon(native); }
            }
        }
        public TrayContext(Settings initial, bool isStartup, string localDataRoot) : this(initial, isStartup, localDataRoot, Startup.Registration) { }
        internal TrayContext(Settings initial, bool isStartup, string localDataRoot, StartupRegistration registration) {
            settings = initial; startup = isStartup; dataRoot = localDataRoot;
            L.SetLanguage(settings.Language);
            startupRegistration = registration;
            IntPtr handle = dispatcher.Handle;
            icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "SteamMonitorTray", Visible = true };
            icon.MouseDoubleClick += delegate { ShowAll(); };
            icon.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Right) ShowMenu(Cursor.Position); };
            SynchronizeProfiles(); RefreshMenu();
            timer = new System.Windows.Forms.Timer { Interval = 250 }; timer.Tick += delegate { Tick(); }; timer.Start();
            dispatcher.BeginInvoke((Action)delegate {
                if (settings.Profiles.Count == 0) { EditSettings(); foreach (Runtime r in runtimes.Values) if (r.Profile.Enabled) Start(r, true); return; }
                try { settings.Validate(); SaveSettings(); foreach (Runtime r in runtimes.Values) if (r.Profile.Enabled) Start(r, true); }
                catch (Exception ex) { Notice(Program.SafeError(ex)); EditSettings(); }
            });
        }
        private void Notice(string text) { icon.ShowBalloonTip(5000, "SteamMonitorTray", L.Text(text), ToolTipIcon.Info); }
        private void SynchronizeProfiles() {
            foreach (Profile p in settings.Profiles) {
                Runtime runtime;
                if (!runtimes.TryGetValue(p.Id, out runtime)) runtimes.Add(p.Id, new Runtime { Profile = p });
                else runtime.Profile = p;
            }
            foreach (string key in runtimes.Keys.ToArray()) if (!settings.Profiles.Any(p => p.Id == key) && !runtimes[key].Alive) runtimes.Remove(key);
        }
        private void Start(Runtime r, bool manual) {
            if (quitting || r.Running || r.StartSent || r.Stopping) return;
            try {
                Validation.ForStart(settings, r.Profile);
                if (manual) r.Retry.Reset();
                r.Desired = true; r.NeedsStart = true; r.RetryAt = DateTime.MinValue; r.Status = "正在启动";
                if (!r.Alive) {
                    if (r.Client != null) r.Client.Dispose();
                    r.Client = new SessionClient(); r.EndHandled = false; r.Prompt = false; r.ExitSent = false;
                    SessionClient client = r.Client;
                    client.Message += delegate(Wire message) {
                        if (!dispatcher.IsDisposed) try { dispatcher.BeginInvoke((Action)delegate { if (r.Client == client) Receive(r, message); }); } catch (InvalidOperationException) { }
                    };
                    client.Start(r.Profile, settings, Path.Combine(dataRoot, "sessions"));
                } else TryStart(r);
            } catch (Exception ex) { r.Desired = false; r.NeedsStart = false; r.Status = "异常"; Files.Log(r.Profile.Id, ex.GetType().Name); Notice(Program.SafeError(ex)); }
            RefreshMenu();
        }
        private void TryStart(Runtime r) {
            if (r.NeedsStart && r.Prompt && !r.StartSent && !r.Running && !quitting) {
                r.StartSent = true; r.NeedsStart = false;
                if (!r.Client.Send("Start")) { r.StartSent = false; r.Status = "异常"; }
            }
        }
        private void Stop(Runtime r) {
            r.Desired = false; r.NeedsStart = false; r.RetryAt = DateTime.MinValue; r.Retry.Reset();
            if (r.Running) {
                r.Stopping = true; r.Status = "正在停止"; r.StopDeadline = DateTime.UtcNow.AddSeconds(settings.StopTimeoutSeconds); r.Client.Send("Stop");
            } else if (r.StartSent) { r.Stopping = true; r.Status = "正在停止"; r.StopDeadline = DateTime.UtcNow.AddSeconds(settings.StopTimeoutSeconds); }
            else r.Status = "已停止";
            RefreshMenu();
        }
        private void Failed(Runtime r, TimeSpan lifetime) {
            int delay = r.Desired && !quitting ? r.Retry.Next(settings, lifetime) : -1;
            if (delay >= 0) { r.RetryAt = DateTime.UtcNow.AddSeconds(delay); r.Status = "等待重试（" + delay + " 秒）"; }
            else { r.Desired = false; r.Status = "异常"; Notice(L.Text("{0} 已停止，请查看控制台。自动重试已结束或未开启。", r.Profile.Name)); }
        }
        private void Receive(Runtime r, Wire message) {
            switch (message.Kind) {
                // Visibility acknowledgements do not change any monitoring menu state.
                case "Hidden": case "Visible": return;
                case "Welcome": if (settings.ShowOnLaunch(startup)) r.Client.Send("Show"); break;
                case "Prompt": r.Prompt = true; TryStart(r); break;
                case "ShellBusy": r.Prompt = false; break;
                case "ExitRequested": r.Prompt = false; break;
                case "Started":
                    r.StartSent = false; r.NeedsStart = false; r.Running = true; r.Prompt = false; r.Started = DateTime.UtcNow;
                    if (!r.Desired && r.Stopping) r.Client.Send("Stop");
                    else if (!r.Desired && !quitting) { r.Desired = true; r.Retry.Reset(); }
                    r.Status = "监测进程运行";
                    if (quitting || r.Stopping) Stop(r);
                    break;
                case "Interrupted":
                    if (r.Running) { r.Desired = false; r.NeedsStart = false; r.Stopping = true; r.RetryAt = DateTime.MinValue; r.Status = "正在停止"; r.StopDeadline = DateTime.UtcNow.AddSeconds(settings.StopTimeoutSeconds); }
                    break;
                case "Stopped":
                    TimeSpan lifetime = DateTime.UtcNow - r.Started;
                    bool intentional = r.Stopping || !r.Desired || message.Code == 0;
                    r.Running = false; r.StartSent = false; r.Stopping = false; r.Prompt = false;
                    if (intentional) { r.Desired = false; r.Status = "已停止"; r.RetryAt = DateTime.MinValue; }
                    else Failed(r, lifetime);
                    break;
                case "Busy":
                    r.StartSent = false; r.NeedsStart = false; r.Desired = r.Running; r.ExitSent = false;
                    if (!r.Running) r.Status = "已停止（控制台忙碌）";
                    if (quitting) quitting = false;
                    Notice("控制台正在执行命令或有未提交输入，请完成或清空后重试。不会覆盖输入。"); break;
                case "SessionEnded": EndSession(r, message.Code != 0); break;
                case "Fault": Files.Log(r.Profile.Id, message.Value ?? "SessionFault"); r.Status = "异常"; Notice("会话发生错误，请显示控制台检查。"); break;
                case "ConnectionEnded": if (!r.EndHandled) EndSession(r, true); break;
            }
            if (message.Kind == "Started" || message.Kind == "Stopped" || message.Kind == "SessionEnded") Files.Log(r.Profile.Id, message.Kind);
            RefreshMenu();
        }
        private void EndSession(Runtime r, bool unexpected) {
            if (r.EndHandled) return; r.EndHandled = true;
            bool shouldRetry = r.Desired && (unexpected || r.Running);
            r.Running = false; r.Prompt = false; r.StartSent = false; r.Stopping = false; r.ExitSent = false;
            if (shouldRetry) Failed(r, DateTime.UtcNow - r.Started);
            else { r.Desired = false; r.Status = "已停止"; }
        }
        private void Tick() {
            foreach (Runtime r in runtimes.Values) {
                if (r.RetryAt != DateTime.MinValue && DateTime.UtcNow >= r.RetryAt) { r.RetryAt = DateTime.MinValue; Start(r, false); }
                if (r.Stopping && DateTime.UtcNow >= r.StopDeadline) {
                    r.Status = "仍在等待停止"; r.StopDeadline = DateTime.MaxValue; Notice(L.Text("{0} 尚未退出，可以继续等待或再次选择停止监测。", r.Profile.Name)); RefreshMenu();
                }
                if (quitting && r.Alive && !r.Running && !r.StartSent && r.Prompt && !r.ExitSent) {
                    r.ExitSent = r.Client.Send("Exit");
                    if (r.ExitSent) { r.Status = "正在退出"; RefreshMenu(); }
                    else { quitting = false; ShowExitWait(); }
                }
            }
            if (quitting && DateTime.UtcNow >= quitDeadline && runtimes.Values.Any(r => r.Alive)) {
                quitDeadline = DateTime.MaxValue; ShowExitWait();
            }
            if (quitting && runtimes.Values.All(r => !r.Alive)) {
                timer.Stop(); MenuPresenter.Dispose(); icon.Visible = false; icon.Dispose(); runningIcon.Dispose(); stoppedIcon.Dispose(); warningIcon.Dispose(); foreach (Runtime r in runtimes.Values) if (r.Client != null) r.Client.Dispose(); dispatcher.Dispose(); ExitThread();
            }
        }
        private void ShowAll() { foreach (Runtime r in runtimes.Values) if (r.Alive) r.Client.Send("Show"); }
        private void SaveSettings() { Json.Save(Path.Combine(dataRoot, "settings.json"), settings); }
        private void SetLanguage(string code) {
            string previous = settings.Language;
            try { settings.Language = L.Normalize(code); SaveSettings(); L.SetLanguage(settings.Language); RefreshMenu(); }
            catch (Exception ex) { settings.Language = previous; L.SetLanguage(previous); Notice(Program.SafeError(ex)); }
        }
        private void ToggleStartup() {
            try {
                bool enable = !startupRegistration.Read().Registered;
                startupRegistration.SetRegistered(enable);
                StartupState state = startupRegistration.Read();
                RefreshMenu();
                if (enable && state.HasWarning) Notice(state.Description);
            } catch (Exception ex) { Notice(Program.SafeError(ex)); }
        }
        private void ShowExitWait() {
            foreach (Runtime r in runtimes.Values) if (r.Alive) {
                if (!r.Running && !r.StartSent) r.Status = "仍在等待退出";
                r.Client.Send("Show");
            }
            Notice("退出尚未完成，托盘会继续保留。请检查显示的控制台，完成或清空命令后重试退出；程序不会强制结束进程。"); RefreshMenu();
        }
        private void Quit() {
            quitting = true; quitDeadline = DateTime.UtcNow.AddSeconds(settings.StopTimeoutSeconds);
            foreach (Runtime r in runtimes.Values) Stop(r);
        }
        private void EditSettings() {
            using (SettingsForm form = new SettingsForm(settings, runtimes.Values.Any(r => r.Alive))) {
                if (form.ShowDialog() == DialogResult.OK) {
                    settings = form.Result; SaveSettings(); SynchronizeProfiles(); RefreshMenu();
                }
            }
        }
        private MenuEntry Item(string key, string text, Action action, bool enabled = true, bool isChecked = false) { return new MenuEntry(key, L.Text(text), action, enabled, isChecked); }
        private void RefreshMenu() {
            if (icon == null || dispatcher.IsDisposed) return;
            int running = runtimes.Values.Count(r => r.Running), total = settings.Profiles.Count(p => p.Enabled);
            bool fault = runtimes.Values.Any(r => r.Status.Contains("异常") || r.Status.Contains("等待"));
            Icon nextIcon = fault ? warningIcon : (running > 0 ? runningIcon : stoppedIcon);
            string nextText = L.Text("SteamMonitorTray — 运行 {0}/{1}{2}", running, total, fault ? L.Text("（需关注）") : "");
            if (nextText.Length > 63) nextText = nextText.Substring(0, 62) + "…";
            if (icon.Icon != nextIcon) icon.Icon = nextIcon;
            if (icon.Text != nextText) icon.Text = nextText;
            // The native popup owns its snapshot until it closes. Only icon state
            // changes here; the next opening builds the latest menu from scratch.
        }
        internal MenuSnapshot CreateMenuSnapshot() {
            List<MenuEntry> items = new List<MenuEntry>();
            items.Add(new MenuEntry("Status", quitting ? L.Text("正在退出…") : L.Text("监测进程运行 {0}/{1}", runtimes.Values.Count(r => r.Running), settings.Profiles.Count(p => p.Enabled)), enabled: false));
            items.Add(Item("StartAll", "全部启动", delegate { foreach (Runtime r in runtimes.Values) if (r.Profile.Enabled) Start(r, true); }, !quitting));
            items.Add(Item("StopAll", "全部停止", delegate { foreach (Runtime r in runtimes.Values) Stop(r); }));
            items.Add(MenuEntry.Separator());
            foreach (Runtime r in runtimes.Values) {
                string key = "Profile." + r.Profile.Id;
                items.Add(new MenuEntry(key, L.Text("{0} — {1}", r.Profile.Name, L.Text(r.Status)), children: new MenuEntry[] {
                    Item(key + ".Show", "显示窗口", delegate { if (r.Alive) r.Client.Send("Show"); }),
                    Item(key + ".Hide", "隐藏窗口", delegate { if (r.Alive) r.Client.Send("Hide"); }),
                    Item(key + ".Start", "启动监测", delegate { Start(r, true); }, !quitting && !r.Running && !r.StartSent && !r.Stopping && r.Profile.Enabled),
                    Item(key + ".Stop", r.Stopping ? "再次发送 Ctrl+C" : "停止监测", delegate { Stop(r); }),
                    Item(key + ".Logs", "打开日志文件夹", delegate { try { Process.Start("explorer.exe", Native.Quote(r.Profile.LogDirectory)); } catch (Exception ex) { Notice(Program.SafeError(ex)); } })
                }));
            }
            items.Add(MenuEntry.Separator());
            items.Add(Item("Settings", "设置与多配置管理…", EditSettings));
            items.Add(Item("LocalSettings", "打开本地设置目录", delegate { Process.Start("explorer.exe", Native.Quote(Files.DataRoot)); }));
            StartupState startupState = startupRegistration.Read();
            items.Add(new MenuEntry("StartupToggle", startupState.MenuText, ToggleStartup, isChecked: startupState.Registered));
            List<MenuEntry> modes = new List<MenuEntry>();
            string[] keys = { "AlwaysWindow", "ManualWindowStartupTray", "AlwaysTray" }, labels = { "不隐藏", "仅开机自启动时隐藏", "每次启动时隐藏" };
            for (int i = 0; i < keys.Length; i++) {
                string key = keys[i];
                modes.Add(Item("LaunchDisplay." + key, labels[i], delegate { settings.LaunchMode = key; SaveSettings(); RefreshMenu(); }, isChecked: settings.LaunchMode == key));
            }
            items.Add(new MenuEntry("LaunchDisplay", L.Text("启动时隐藏窗口"), children: modes.ToArray()));
            List<MenuEntry> languages = new List<MenuEntry>();
            languages.Add(Item("Language.auto", "自动（跟随系统）", delegate { SetLanguage("auto"); }, isChecked: settings.Language == "auto"));
            for (int i = 0; i < L.Codes.Length; i++) {
                string code = L.Codes[i];
                languages.Add(new MenuEntry("Language." + code, L.Names[i], delegate { SetLanguage(code); }, isChecked: settings.Language == code));
            }
            items.Add(new MenuEntry("Language", L.Text("语言"), children: languages.ToArray()));
            items.Add(MenuEntry.Separator()); items.Add(Item("Quit", "退出", Quit));
            return new MenuSnapshot(items, L.IsRtl);
        }
        internal void ShowMenu(Point position) {
            if (dispatcher.IsDisposed || MenuPresenter.IsOpen) return;
            try {
                MenuEntry selected = MenuPresenter.Show(CreateMenuSnapshot(), position);
                // Native handles have been released before any action can open a
                // dialog, change language, stop a session or exit the application.
                if (selected != null && !dispatcher.IsDisposed) selected.Invoke();
            } catch (Exception ex) {
                Files.Log("menu", ex.GetType().Name);
                if (!dispatcher.IsDisposed) Notice("界面操作失败，请检查本地包装器日志。监测不会因此被强制终止。");
            }
        }
    }
}
