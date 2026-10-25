using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SteamMonitorTray {
    internal static class Program {
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { Files.Log("ui", e.Exception.GetType().Name); MessageBox.Show("界面操作失败，请检查本地包装器日志。监测不会因此被强制终止。", "SteamMonitorTray"); };
            try {
                Files.EnsurePrivateDirectory(Files.DataRoot);
                if (args.Length == 1 && args[0] == "--install-startup") { Startup.Install(); return 0; }
                if (args.Length == 1 && args[0] == "--uninstall-startup") { Startup.Remove(); return 0; }
                bool owns;
                using (Mutex mutex = new Mutex(true, "Local\\SteamMonitorTray." + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value, out owns)) {
                    if (!owns) { MessageBox.Show("SteamMonitorTray 已在运行，请使用通知区域图标。导入配置前请先从托盘退出。", "SteamMonitorTray"); return args.Length == 2 ? 2 : 0; }
                    Settings settings = File.Exists(Files.SettingsPath) ? Json.Read<Settings>(Files.SettingsPath) : Settings.Defaults();
                    if (args.Length == 2 && args[0] == "--import-directory") {
                        Profile profile = Files.FromDirectory(args[1]); profile.Name = "监测 " + (settings.Profiles.Count + 1);
                        settings.Profiles.Add(profile); Validation.All(settings); Json.Save(Files.SettingsPath, settings); return 0;
                    }
                    Application.Run(new TrayContext(settings, Array.IndexOf(args, "--startup") >= 0, Files.DataRoot));
                }
                return 0;
            } catch (Exception ex) {
                Files.Log("app", ex.GetType().Name);
                MessageBox.Show("操作失败：" + SafeError(ex), "SteamMonitorTray", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }
        public static string SafeError(Exception ex) {
            if (ex is InvalidDataException) return ex.Message;
            return ex.GetType().Name + "。请检查路径、权限和程序依赖。";
        }
    }
    internal static class Startup {
        public static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "SteamMonitorTray.lnk"); } }
        public static void Install() {
            // Do not touch StartupApproved. In particular, reinstalling does not re-enable a disabled item.
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type), shortcut = null;
            try {
                shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { Shortcut });
                Type linkType = shortcut.GetType();
                Action<string, object> set = delegate(string name, object value) { linkType.InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { value }); };
                set("TargetPath", Path.Combine(Files.Bin, "SteamMonitorTray.exe")); set("Arguments", "--startup");
                set("WorkingDirectory", Files.Bin); set("Description", "SteamMonitorTray"); set("IconLocation", Path.Combine(Files.Bin, "SteamMonitorTray.exe") + ",0");
                linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            } finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
        }
        public static void Remove() { if (File.Exists(Shortcut)) File.Delete(Shortcut); }
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
        private readonly NotifyIcon icon;
        private readonly Icon runningIcon = StatusIcon(Color.FromArgb(34, 160, 85), "play"), stoppedIcon = StatusIcon(Color.SlateGray, "stop"), warningIcon = StatusIcon(Color.DarkOrange, "warning");
        private readonly Control dispatcher = new Control();
        private readonly Dictionary<string, Runtime> runtimes = new Dictionary<string, Runtime>();
        private readonly System.Windows.Forms.Timer timer;
        private bool quitting;
        private bool menuRefreshPending;
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
        public TrayContext(Settings initial, bool isStartup, string localDataRoot) {
            settings = initial; startup = isStartup; dataRoot = localDataRoot;
            IntPtr handle = dispatcher.Handle;
            icon = new NotifyIcon { Icon = SystemIcons.Application, Text = "SteamMonitorTray", Visible = true };
            icon.MouseDoubleClick += delegate { ShowAll(); };
            SynchronizeProfiles(); RefreshMenu();
            timer = new System.Windows.Forms.Timer { Interval = 250 }; timer.Tick += delegate { Tick(); }; timer.Start();
            dispatcher.BeginInvoke((Action)delegate {
                if (settings.Profiles.Count == 0) { EditSettings(); foreach (Runtime r in runtimes.Values) if (r.Profile.Enabled) Start(r, true); return; }
                try { settings.Validate(); SaveSettings(); foreach (Runtime r in runtimes.Values) if (r.Profile.Enabled) Start(r, true); }
                catch (Exception ex) { Notice(Program.SafeError(ex)); EditSettings(); }
            });
        }
        private void Notice(string text) { icon.ShowBalloonTip(5000, "SteamMonitorTray", text, ToolTipIcon.Info); }
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
            else { r.Desired = false; r.Status = "异常"; Notice(r.Profile.Name + " 已停止，请查看控制台。自动重试已结束或未开启。"); }
        }
        private void Receive(Runtime r, Wire message) {
            switch (message.Kind) {
                // Visibility acknowledgements do not change any monitoring menu state.
                case "Hidden": case "Visible": return;
                case "Welcome": if (settings.ShowOnLaunch(startup)) r.Client.Send("Show"); break;
                case "Prompt": r.Prompt = true; TryStart(r); break;
                case "ShellBusy": r.Prompt = false; break;
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
                    r.Status = "仍在等待停止"; r.StopDeadline = DateTime.MaxValue; Notice(r.Profile.Name + " 尚未退出，可以继续等待或再次选择停止监测。"); RefreshMenu();
                }
                if (quitting && r.Alive && !r.Running && !r.StartSent && r.Prompt && !r.ExitSent) { r.ExitSent = true; r.Client.Send("Exit"); }
            }
            if (quitting && runtimes.Values.All(r => !r.Alive)) {
                timer.Stop(); icon.Visible = false; icon.Dispose(); runningIcon.Dispose(); stoppedIcon.Dispose(); warningIcon.Dispose(); foreach (Runtime r in runtimes.Values) if (r.Client != null) r.Client.Dispose(); dispatcher.Dispose(); ExitThread();
            }
        }
        private void ShowAll() { foreach (Runtime r in runtimes.Values) if (r.Alive) r.Client.Send("Show"); }
        private void SaveSettings() { Json.Save(Path.Combine(dataRoot, "settings.json"), settings); }
        private void Quit() { quitting = true; foreach (Runtime r in runtimes.Values) Stop(r); }
        private void EditSettings() {
            using (SettingsForm form = new SettingsForm(settings, runtimes.Values.Any(r => r.Alive))) {
                if (form.ShowDialog() == DialogResult.OK) {
                    settings = form.Result; SaveSettings(); SynchronizeProfiles(); RefreshMenu();
                }
            }
        }
        private ToolStripMenuItem Item(string text, Action action) { ToolStripMenuItem item = new ToolStripMenuItem(text); item.Click += delegate { action(); }; return item; }
        private void RefreshMenu() {
            if (icon == null || dispatcher.IsDisposed) return;
            int running = runtimes.Values.Count(r => r.Running), total = settings.Profiles.Count(p => p.Enabled);
            bool fault = runtimes.Values.Any(r => r.Status.Contains("异常") || r.Status.Contains("等待"));
            Icon nextIcon = fault ? warningIcon : (running > 0 ? runningIcon : stoppedIcon);
            string nextText = "SteamMonitorTray — 运行 " + running + "/" + total + (fault ? "（需关注）" : "");
            if (icon.Icon != nextIcon) icon.Icon = nextIcon;
            if (icon.Text != nextText) icon.Text = nextText;
            ContextMenuStrip old = icon.ContextMenuStrip;
            // Keep both the active menu and its submenus alive until the user's interaction ends.
            if (old != null && old.Visible) { menuRefreshPending = true; return; }
            menuRefreshPending = false;
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Closed += delegate {
                if (menuRefreshPending && !dispatcher.IsDisposed) dispatcher.BeginInvoke((Action)delegate {
                    if (menuRefreshPending && !dispatcher.IsDisposed) RefreshMenu();
                });
            };
            menu.Items.Add(new ToolStripMenuItem("监测进程运行 " + running + "/" + total) { Enabled = false });
            menu.Items.Add(Item("全部启动", delegate { foreach (Runtime r in runtimes.Values) if (r.Profile.Enabled) Start(r, true); }));
            menu.Items.Add(Item("全部停止", delegate { foreach (Runtime r in runtimes.Values) Stop(r); }));
            menu.Items.Add(new ToolStripSeparator());
            foreach (Runtime r in runtimes.Values) {
                ToolStripMenuItem profile = new ToolStripMenuItem(r.Profile.Name + " — " + r.Status);
                profile.ForeColor = r.Running ? Color.ForestGreen : (r.Status.Contains("异常") || r.Status.Contains("等待") ? Color.DarkOrange : Color.DimGray);
                profile.DropDownItems.Add(Item("显示窗口", delegate { if (r.Alive) r.Client.Send("Show"); }));
                profile.DropDownItems.Add(Item("隐藏窗口", delegate { if (r.Alive) r.Client.Send("Hide"); }));
                ToolStripMenuItem start = Item("启动监测", delegate { Start(r, true); }); start.Enabled = !r.Running && !r.StartSent && !r.Stopping && r.Profile.Enabled; profile.DropDownItems.Add(start);
                profile.DropDownItems.Add(Item(r.Stopping ? "再次发送 Ctrl+C" : "停止监测", delegate { Stop(r); }));
                profile.DropDownItems.Add(Item("打开日志文件夹", delegate { try { Process.Start("explorer.exe", Native.Quote(r.Profile.LogDirectory)); } catch (Exception ex) { Notice(Program.SafeError(ex)); } }));
                menu.Items.Add(profile);
            }
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem modes = new ToolStripMenuItem("启动显示方式");
            string[] keys = { "ManualWindowStartupTray", "AlwaysTray", "AlwaysWindow" }, labels = { "手动显示，自动进托盘", "两种启动都进托盘", "两种启动都显示窗口" };
            for (int i = 0; i < keys.Length; i++) {
                string key = keys[i]; ToolStripMenuItem item = Item(labels[i], delegate { settings.LaunchMode = key; SaveSettings(); RefreshMenu(); });
                item.Checked = settings.LaunchMode == key; modes.DropDownItems.Add(item);
            }
            menu.Items.Add(modes); menu.Items.Add(Item("设置与多配置管理…", EditSettings));
            menu.Items.Add(Item("打开本地设置目录", delegate { Process.Start("explorer.exe", Native.Quote(Files.DataRoot)); }));
            menu.Items.Add(Item("注册登录自启动", delegate { try { Startup.Install(); Notice("启动项已注册。可以在任务管理器中启用或禁用。"); } catch (Exception ex) { Notice(Program.SafeError(ex)); } }));
            menu.Items.Add(Item("移除登录自启动", delegate { try { Startup.Remove(); Notice("已移除本工具的启动项。"); } catch (Exception ex) { Notice(Program.SafeError(ex)); } }));
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(Item("退出程序（先发送 Ctrl+C）", Quit));
            // Do not dispose a menu while one of its click handlers is executing.
            icon.ContextMenuStrip = menu;
            if (old != null) dispatcher.BeginInvoke((Action)delegate { old.Dispose(); });
        }
    }
}
