using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SteamMonitorTray;

internal static class LocalizationTests {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LogFont {
        public int height, width, escapement, orientation, weight;
        public byte italic, underline, strikeout, charset, outputPrecision, clipPrecision, quality, pitch;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string face;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern int GetObjectW(IntPtr handle, int size, out LogFont font);
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static void CheckLayout(Control control) {
        if (control is HelpLabel && control.Visible && control.Width > 1) {
            Size preferred = control.GetPreferredSize(new Size(control.Width, 0));
            if (control.Height < preferred.Height) throw new Exception("Clipped DPI help text: " + control.Text);
        }
        Button button = control as Button;
        if (button != null && button.Visible && button.Text.Length > 0 && button.ClientSize.Width < TextRenderer.MeasureText(button.Text, button.Font).Width) throw new Exception("Clipped DPI button: " + button.Text);
        foreach (Control child in control.Controls) CheckLayout(child);
    }
    private static void CheckFonts(Control control, float expected) {
        if (control is TextBox || control is ComboBox || control is ListBox || control is Button || control is TabControl)
            Assert(Math.Abs(control.Font.SizeInPoints - expected) < 0.1F, "consistent control font: " + control.GetType().Name);
        if ((control is TextBox || control is ComboBox || control is ListBox || control is TabControl) && control.IsHandleCreated) {
            LogFont native; GetObjectW(SendMessageW(control.Handle, 0x31, IntPtr.Zero, IntPtr.Zero), Marshal.SizeOf(typeof(LogFont)), out native);
            Console.WriteLine("INFO HWND font " + control.GetType().Name + " height=" + native.height + " expected=" + Math.Round(expected * control.DeviceDpi / 72F) + " face=" + native.face + " box=" + control.Height);
            Assert(Math.Abs(Math.Abs(native.height) - Math.Round(expected * control.DeviceDpi / 72F)) <= 1, "native HWND font matches monitor DPI: " + control.GetType().Name);
        }
        foreach (Control child in control.Controls) CheckFonts(child, expected);
    }
    private static void Quit(TrayContext context) {
        bool exited = false; context.ThreadExit += delegate { exited = true; };
        typeof(TrayContext).GetMethod("Quit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, null);
        for (int i = 0; i < 100 && !exited; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
    }
    internal static void FreshStartup(string root) {
        Settings settings = Settings.Defaults(); settings.Language = "zh-Hans";
        settings.Profiles.Add(new Profile { Name = "Font fixture", Enabled = false, WorkingDirectory = @"D:\SteamProfiles\Demo", ConfigFile = @"D:\SteamProfiles\Demo\steam_monitor.conf", EnvFile = "", Executable = @"D:\Python\Scripts\steam_monitor.exe" });
        StartupRegistration registration = new StartupRegistration(Path.Combine(root, "font-startup.lnk"), Path.Combine(Files.Bin, "SteamMonitorTray.exe"), @"Software\SteamMonitorTray\Tests\" + Guid.NewGuid().ToString("N"), "font-startup.lnk");
        TrayContext context = new TrayContext(settings, true, Path.Combine(root, "font-startup"), registration);
        try { Application.DoEvents(); SettingsForm form = context.OpenSettings(); Application.DoEvents(); CheckFonts(form, form.Font.SizeInPoints); form.ShowProfiles(); Application.DoEvents(); CheckFonts(form, form.Font.SizeInPoints); form.Close(); }
        finally { Quit(context); }
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
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(Screen.PrimaryScreen.Bounds.Right + 1000, 0);
            form.Show(); Application.DoEvents();
            Console.WriteLine("INFO Settings font " + form.Font.SizeInPoints + "pt; DPI " + form.DeviceDpi);
            Assert(form.Font.SizeInPoints >= 11F, "larger settings base font"); CheckFonts(form, form.Font.SizeInPoints);
            foreach (string code in L.Codes) {
                Assert(!L.MissingTranslations(code).Any(), "complete embedded translations: " + code);
                L.SetLanguage(code); form.PerformLayout();
                form.ShowGeneral();
                Application.DoEvents(); CheckLayout(form); ResizeTests.CheckSpacing(form);
                using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(root, "settings-" + code + ".png")); }
                Assert(true, "settings help text and buttons fit actual DPI: " + code);
                Assert(form.Text == L.Text("SteamMonitorTray 设置") && form.RightToLeftLayout == (code == "ar"), "settings update in place with correct direction: " + code);
                TabControl pages = (TabControl)typeof(SettingsForm).GetField("pages", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                Assert(pages.RightToLeftLayout == (code == "ar"), "tab headers mirror with Arabic layout: " + code);
                ComboBox combo = (ComboBox)typeof(SettingsForm).GetField("mode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                Assert(combo.Items[1].ToString() == L.Text("仅开机自启动时隐藏"), "launch mode labels localized: " + code);
                settings.Language = code;
                Assert(Json.ReadText<Settings>(Json.Text(settings)).Language == code, "manual language survives serialization: " + code);
                form.ShowProfiles(); Application.DoEvents(); CheckLayout(form); ResizeTests.CheckSpacing(form); CheckFonts(form, form.Font.SizeInPoints);
                using (Bitmap preview = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(preview, new Rectangle(Point.Empty, preview.Size)); preview.Save(Path.Combine(root, "settings-profiles-" + code + ".png")); }
                form.BeginPreparing("正在停止监测…"); Application.DoEvents(); CheckLayout(form);
                form.EditReady(); Application.DoEvents(); CheckLayout(form);
                Assert(true, "preparing and edit footer fit translated text: " + code);
            }
            var boxes = (System.Collections.Generic.Dictionary<string, TextBox>)typeof(SettingsForm).GetField("fields", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            Assert(boxes["Name"].Text == "User profile — unchanged" && boxes["ConfigFile"].Text.EndsWith("steam_monitor.conf") && boxes["ConfigFile"].RightToLeft == RightToLeft.No, "language changes preserve user names and LTR file paths");
        }
        TrayContext context = null;
        try {
            settings.Language = "zh-Hans";
            StartupRegistration registration = new StartupRegistration(Path.Combine(root, "language-startup.lnk"), Path.Combine(Files.Bin, "SteamMonitorTray.exe"), @"Software\SteamMonitorTray\Tests\" + Guid.NewGuid().ToString("N"), "language-startup.lnk");
            context = new TrayContext(settings, true, Path.Combine(root, "language-manager"), registration); Application.DoEvents();
            foreach (string code in L.Codes) {
                var language = context.CreateMenuSnapshot().Items.First(item => item.Key == "Language");
                language.Children[L.Codes.ToList().IndexOf(code) + 1].Invoke(); Application.DoEvents();
                var menu = context.CreateMenuSnapshot();
                Assert(menu.Items.First(item => item.Key == "Language").Text == L.Text("语言") && menu.RightToLeft == (code == "ar"), "tray language selection applies immediately: " + code);
                Assert(menu.Items[menu.Items.Count - 1].Text == L.Text("退出") && !menu.Items[menu.Items.Count - 1].Text.Contains("Ctrl+C"), "quit menu requires no manual Ctrl+C: " + code);
                if (code == "zh-Hans") Assert(menu.Items[menu.Items.Count - 1].Text == "退出", "concise Simplified Chinese quit label");
            }
            var languages = context.CreateMenuSnapshot().Items.First(item => item.Key == "Language");
            languages.Children[0].Invoke(); Application.DoEvents();
            Assert(Json.Read<Settings>(Path.Combine(root, "language-manager", "settings.json")).Language == "auto", "automatic language selection persists");
        } finally { if (context != null) Quit(context); L.SetLanguage("zh-Hans"); }
    }
}
