using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SteamMonitorTray;

internal static class LocalizationTests {
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static NotifyIcon Icon(TrayContext context) { return (NotifyIcon)typeof(TrayContext).GetField("icon", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(context); }
    private static void Quit(TrayContext context) {
        bool exited = false; context.ThreadExit += delegate { exited = true; };
        typeof(TrayContext).GetMethod("Quit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, null);
        for (int i = 0; i < 100 && !exited; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
    }
    public static void Run(string root) {
        Assert(Settings.Defaults().Language == "auto", "default language follows Windows UI preferences");
        string legacy = Json.Text(Settings.Defaults()).Replace("\"Language\":\"auto\",", "");
        Assert(!legacy.Contains("\"Language\"") && Json.ReadText<Settings>(legacy).Language == "auto", "legacy settings without language remain compatible");
        Assert(Json.ReadText<Settings>("{\"Language\":\"unsupported\"}").Language == "auto", "unsupported saved language falls back to auto");
        Assert(L.Resolve("auto", new string[] { "de-DE", "fr-CA", "en-US" }) == "fr" && L.Resolve("auto", new string[] { "zh-TW" }) == "zh-Hans" && L.Resolve("auto", new string[] { "ja-JP" }) == "en", "ordered UI language families and English fallback");
        Settings settings = Settings.Defaults(); settings.Language = "zh-Hans";
        settings.Profiles.Add(new Profile { Name = "User profile — unchanged", Enabled = false, WorkingDirectory = @"D:\SteamProfiles\Primary", ConfigFile = @"D:\SteamProfiles\Primary\steam_monitor.conf", EnvFile = @"D:\SteamProfiles\Primary\.env", Executable = @"D:\Python\Scripts\steam_monitor.exe" });
        using (SettingsForm form = new SettingsForm(settings, false)) {
            foreach (string code in L.Codes) {
                Assert(!L.MissingTranslations(code).Any(), "complete embedded translations: " + code);
                L.SetLanguage(code); form.PerformLayout();
                Assert(form.Text == L.Text("SteamMonitorTray 设置") && form.RightToLeftLayout == (code == "ar"), "settings update in place with correct direction: " + code);
                ComboBox combo = (ComboBox)typeof(SettingsForm).GetField("mode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                Assert(combo.Items[1].ToString() == L.Text("仅开机自启动时隐藏"), "launch mode labels localized: " + code);
                settings.Language = code;
                Assert(Json.ReadText<Settings>(Json.Text(settings)).Language == code, "manual language survives serialization: " + code);
            }
            var boxes = (System.Collections.Generic.Dictionary<string, TextBox>)typeof(SettingsForm).GetField("fields", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            Assert(boxes["Name"].Text == "User profile — unchanged" && boxes["ConfigFile"].Text.EndsWith("steam_monitor.conf") && boxes["ConfigFile"].RightToLeft == RightToLeft.No, "language changes preserve user names and LTR file paths");
        }
        TrayContext context = null;
        try {
            settings.Language = "zh-Hans";
            context = new TrayContext(settings, true, Path.Combine(root, "language-manager")); Application.DoEvents();
            foreach (string code in L.Codes) {
                var language = Icon(context).ContextMenuStrip.Items.OfType<ToolStripMenuItem>().First(item => item.Name == "Language");
                language.DropDownItems[L.Codes.ToList().IndexOf(code) + 1].PerformClick(); Application.DoEvents();
                var menu = Icon(context).ContextMenuStrip;
                Assert(menu.Items.OfType<ToolStripMenuItem>().First(item => item.Name == "Language").Text == L.Text("语言") && menu.RightToLeft == (code == "ar" ? RightToLeft.Yes : RightToLeft.No), "tray language selection applies immediately: " + code);
                Assert(menu.Items[menu.Items.Count - 1].Text == L.Text("退出程序") && !menu.Items[menu.Items.Count - 1].Text.Contains("Ctrl+C"), "quit menu requires no manual Ctrl+C: " + code);
            }
            var languages = Icon(context).ContextMenuStrip.Items.OfType<ToolStripMenuItem>().First(item => item.Name == "Language");
            languages.DropDownItems[0].PerformClick(); Application.DoEvents();
            Assert(Json.Read<Settings>(Path.Combine(root, "language-manager", "settings.json")).Language == "auto", "automatic language selection persists");
        } finally { if (context != null) Quit(context); L.SetLanguage("zh-Hans"); }
    }
}
