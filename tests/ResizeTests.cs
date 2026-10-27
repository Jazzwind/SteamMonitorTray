using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SteamMonitorTray;

internal static class ResizeTests {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    internal static void CheckSpacing(SettingsForm form) {
        ComboBox mode = (ComboBox)typeof(SettingsForm).GetField("mode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        TextBox shell = (TextBox)typeof(SettingsForm).GetField("shell", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        TextBox python = (TextBox)typeof(SettingsForm).GetField("python", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        TabControl pages = (TabControl)typeof(SettingsForm).GetField("pages", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        ComboBox language = (ComboBox)typeof(SettingsForm).GetField("language", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
        if (pages.SelectedIndex == 0 && language.Top - mode.Bottom < form.LogicalToDeviceUnits(6)) throw new Exception("General input rows overlap or have insufficient separation.");
        if (pages.SelectedIndex == 1 && python.Top - shell.Bottom < form.LogicalToDeviceUnits(6)) throw new Exception("Runtime input rows overlap or have insufficient separation.");
        if (pages.SelectedIndex == 1) {
            TableLayoutPanel runtime = (TableLayoutPanel)shell.Parent;
            TableLayoutPanel profiles = (TableLayoutPanel)typeof(SettingsForm).GetField("profiles", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            if (profiles.Top < runtime.Bottom || profiles.Height < form.LogicalToDeviceUnits(80)) throw new Exception("Runtime section overlaps or obscures the profile editor.");
        }
        Control root = form.Controls[0];
        FlowLayoutPanel footer = root.Controls.OfType<FlowLayoutPanel>().Single();
        if (footer.Top < pages.Bottom || footer.Bottom > root.ClientSize.Height - root.Padding.Bottom) throw new Exception("Resize clips or overlaps the fixed settings footer.");
    }
    internal static void Run(string root, bool verifySpacing) {
        Settings s = Settings.Defaults(); s.Language = "zh-Hans";
        s.Profiles.Add(new Profile { Name = "Resize fixture", AutoStart = false, WorkingDirectory = @"D:\SteamProfiles\Demo", ConfigFile = @"D:\SteamProfiles\Demo\steam_monitor.conf", EnvFile = "", Executable = @"D:\Python\Scripts\steam_monitor.exe" });
        StartupRegistration registration = new StartupRegistration(Path.Combine(root, "resize-startup.lnk"), Path.Combine(Files.Bin, "SteamMonitorTray.exe"), @"Software\SteamMonitorTray\Tests\" + Guid.NewGuid().ToString("N"), "resize-startup.lnk");
        TrayContext context = new TrayContext(s, true, Path.Combine(root, "resize-data"), registration);
        try {
            Application.DoEvents(); SettingsForm form = context.OpenSettings(); Application.DoEvents();
            TableLayoutPanel header = (TableLayoutPanel)typeof(SettingsForm).GetField("global", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            ComboBox mode = (ComboBox)typeof(SettingsForm).GetField("mode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            TextBox shell = (TextBox)typeof(SettingsForm).GetField("shell", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            TextBox python = (TextBox)typeof(SettingsForm).GetField("python", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            Console.WriteLine("INFO header rows=" + String.Join(",", header.GetRowHeights()) + " mode=" + mode.Bounds + " shell=" + shell.Bounds + " python=" + python.Bounds);
            Console.WriteLine("INFO runtime input gap=" + (python.Top - shell.Bottom));
            if (verifySpacing) CheckSpacing(form);
            form.ShowProfiles(); Application.DoEvents();
            SendMessageW(form.Handle, 0x231, IntPtr.Zero, IntPtr.Zero);
            if (!form.IsLiveResizing) throw new Exception("Resize loop did not enter interactive state.");
            System.Windows.Forms.Timer checks = (System.Windows.Forms.Timer)typeof(SettingsForm).GetField("healthTimer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            if (checks.Enabled) throw new Exception("Path checks should pause during interactive resize.");
            List<double> times = new List<double>();
            double layoutMs = 0, paintMs = 0;
            for (int i = 0; i < 70; i++) {
                int width = form.LogicalToDeviceUnits(900 + i * 3);
                Stopwatch frame = Stopwatch.StartNew();
                form.Width = width; double layout = frame.Elapsed.TotalMilliseconds; form.Update(); Application.DoEvents();
                if (i >= 10) { times.Add(frame.Elapsed.TotalMilliseconds); layoutMs += layout; paintMs += frame.Elapsed.TotalMilliseconds - layout; }
                if (verifySpacing) CheckSpacing(form);
            }
            times.Sort(); Console.WriteLine("INFO resize frames=" + times.Count + " medianMs=" + times[times.Count / 2].ToString("F2") + " p95Ms=" + times[(int)(times.Count * .95)].ToString("F2") + " maxMs=" + times.Last().ToString("F2"));
            Console.WriteLine("INFO average layoutMs=" + (layoutMs / times.Count).ToString("F2") + " paintAndPumpMs=" + (paintMs / times.Count).ToString("F2"));
            SendMessageW(form.Handle, 0x232, IntPtr.Zero, IntPtr.Zero); Application.DoEvents();
            if (form.IsLiveResizing || !checks.Enabled) throw new Exception("Path checks did not resume after interactive resize.");
            Console.WriteLine("PASS global rows and fixed footer remain separated throughout resizing; background checks pause and resume");
            form.Close();
        } finally {
            bool exited = false; context.ThreadExit += delegate { exited = true; };
            typeof(TrayContext).GetMethod("Quit", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(context, null);
            Stopwatch wait = Stopwatch.StartNew(); while (!exited && wait.Elapsed.TotalSeconds < 3) { Application.DoEvents(); Thread.Sleep(20); }
            if (!exited) throw new Exception("Resize test context did not exit normally.");
        }
    }
}
