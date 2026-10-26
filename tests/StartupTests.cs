using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using SteamMonitorTray;

internal static class StartupTests {
    private static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private static byte[] Approval(uint code) { byte[] bytes = new byte[12]; Array.Copy(BitConverter.GetBytes(code), bytes, 4); return bytes; }
    private static MenuSnapshot Menu(TrayContext context) { return context.CreateMenuSnapshot(); }
    private static MenuEntry Toggle(TrayContext context) { return Menu(context).Items.First(item => item.Key == "StartupToggle"); }
    private static void OpenMenu(TrayContext context) { context.CreateMenuSnapshot(); }
    private static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(value); }
    public static void Run(string root) {
        const string prefix = @"Software\SteamMonitorTray\Tests\";
        string testKey = prefix + Guid.NewGuid().ToString("N");
        string folder = Path.Combine(root, "startup-fixture"); Directory.CreateDirectory(folder);
        string shortcut = Path.Combine(folder, "SteamMonitorTray.lnk");
        StartupRegistration registration = new StartupRegistration(shortcut, Path.Combine(Files.Bin, "SteamMonitorTray.exe"), testKey, "SteamMonitorTray.lnk");
        TrayContext context = null;
        try {
            Assert(!registration.Read().Registered, "startup unchecked when no shortcut is registered");
            registration.SetRegistered(true); registration.SetRegistered(true);
            Assert(registration.Read().Registered && registration.Read().Approval == StartupApproval.NotRecorded && Directory.GetFiles(folder, "*.lnk").Length == 1, "startup registration is idempotent with no approval record");
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(testKey)) {
                key.SetValue("SteamMonitorTray.lnk", Approval(3), RegistryValueKind.Binary);
                key.SetValue("OtherApp.lnk", Approval(7), RegistryValueKind.Binary);
                registration.SetRegistered(true);
                Assert(registration.Read().Approval == StartupApproval.Disabled && ((byte[])key.GetValue("SteamMonitorTray.lnk"))[0] == 3, "registration preserves task manager disable");
                Settings settings = Settings.Defaults(); settings.Language = "zh-Hans"; settings.Profiles.Add(new Profile { Name = "startup fixture", Enabled = false });
                context = new TrayContext(settings, true, Path.Combine(folder, "settings"), registration);
                Application.DoEvents();
                string[] order = Menu(context).Items.Where(item => new string[] { "Settings", "LocalSettings", "StartupToggle", "LaunchDisplay", "Language" }.Contains(item.Key)).Select(item => item.Key).ToArray();
                Assert(order.SequenceEqual(new string[] { "Settings", "LocalSettings", "StartupToggle", "LaunchDisplay", "Language" }), "requested tray settings/startup/display/language order");
                Assert(Toggle(context).Checked && Toggle(context).Text.Contains("任务管理器已禁用"), "registered disabled startup stays checked with a warning");
                SettingsForm form = context.OpenSettings(); form.ShowGeneral(); Application.DoEvents();
                CheckBox startupBox = Field<CheckBox>(form, "startupCheck");
                Assert(startupBox.Checked && Field<HelpLabel>(form, "startupHelp").Text.Contains("任务管理器禁用"), "settings shows registered startup with task manager warning");
                startupBox.Checked = false;
                Assert(!registration.Read().Registered && !Toggle(context).Checked, "settings startup checkbox updates tray immediately");
                startupBox.Checked = true;
                Assert(registration.Read().Registered && registration.Read().Approval == StartupApproval.Disabled, "settings cannot override task manager disable");
                Toggle(context).Invoke(); Assert(!startupBox.Checked, "tray startup change updates open settings immediately");
                Toggle(context).Invoke(); Assert(startupBox.Checked, "tray startup registration is reflected in settings");
                key.SetValue("SteamMonitorTray.lnk", Approval(2), RegistryValueKind.Binary); form.RefreshStartupState();
                Assert(registration.Read().Approval == StartupApproval.Enabled && Field<HelpLabel>(form, "startupHelp").Text == registration.Read().Description, "settings refresh reads external task manager enable");
                key.SetValue("SteamMonitorTray.lnk", Approval(3), RegistryValueKind.Binary); form.RefreshStartupState();
                Field<ComboBox>(form, "mode").SelectedIndex = 2;
                ComboBox language = Field<ComboBox>(form, "language"); language.SelectedIndex = 2;
                Settings saved = Json.Read<Settings>(Path.Combine(folder, "settings", "settings.json"));
                Assert(saved.Language == "en" && saved.LaunchMode != "AlwaysTray", "settings language applies immediately without committing unrelated draft options");
                Menu(context).Items.First(item => item.Key == "Language").Children[3].Invoke();
                Assert(language.SelectedIndex == 3, "tray language change synchronizes open settings selector");
                language.SelectedIndex = 2; form.Close(); Application.DoEvents();
                Assert(registration.Read().Registered && Json.Read<Settings>(Path.Combine(folder, "settings", "settings.json")).Language == "en", "cancel preserves explicitly immediate startup and language changes");
                Menu(context).Items.First(item => item.Key == "Language").Children[1].Invoke();
                Toggle(context).Invoke();
                Assert(!registration.Read().Registered && !Toggle(context).Checked && Toggle(context).Text.Contains("未注册"), "startup checkbox removes its own shortcut");
                Assert(((byte[])key.GetValue("SteamMonitorTray.lnk"))[0] == 3 && ((byte[])key.GetValue("OtherApp.lnk"))[0] == 7, "unchecking preserves approval records and other entries");
                Toggle(context).Invoke();
                Assert(Toggle(context).Checked && Toggle(context).Text.Contains("任务管理器已禁用"), "rechecking cannot bypass task manager disable");
                key.SetValue("SteamMonitorTray.lnk", Approval(2), RegistryValueKind.Binary);
                OpenMenu(context);
                Assert(Toggle(context).Checked && Toggle(context).Text == "开机自启动", "menu opening reads external enable changes without restarting");
                key.SetValue("SteamMonitorTray.lnk", Approval(7), RegistryValueKind.Binary);
                OpenMenu(context);
                Assert(Toggle(context).Checked && Toggle(context).Text.Contains("任务管理器已禁用"), "menu opening reads external disable changes without restarting");
                key.SetValue("SteamMonitorTray.lnk", "unexpected", RegistryValueKind.String);
                OpenMenu(context);
                Assert(Toggle(context).Checked && Toggle(context).Text.Contains("系统状态未知"), "unrecognized startup metadata is reported as unknown");
                registration.SetRegistered(false); OpenMenu(context);
                Assert(!Toggle(context).Checked, "menu opening detects externally removed shortcut");
            }
            Assert(StartupRegistration.ParseApproval(Approval(6)) == StartupApproval.Enabled && StartupRegistration.ParseApproval(Approval(7)) == StartupApproval.Disabled, "alternate Windows approval states supported");
            Assert(StartupRegistration.ParseApproval(new byte[4]) == StartupApproval.Unknown && StartupRegistration.ParseApproval(Approval(11)) == StartupApproval.Unknown, "unknown approval layouts and codes never treated as enabled");
        } finally {
            if (context != null) {
                bool exited = false; context.ThreadExit += delegate { exited = true; };
                typeof(TrayContext).GetMethod("Quit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, null);
                Stopwatch watch = Stopwatch.StartNew(); while (!exited && watch.Elapsed.TotalSeconds < 3) { Application.DoEvents(); Thread.Sleep(20); }
            }
            registration.SetRegistered(false);
            Guid keyId;
            if (!testKey.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(testKey.Substring(prefix.Length), "N", out keyId)) throw new InvalidOperationException("Invalid test cleanup scope.");
            Registry.CurrentUser.DeleteSubKeyTree(testKey, false);
        }
    }
}
