using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SteamMonitorTray;

internal static class UiPreview {
    internal static int RunDrag() {
        Settings settings = Settings.Defaults(); settings.Language = "zh-Hans";
        for (int i = 0; i < 32; i++) settings.Profiles.Add(new Profile { Name = "Demo " + (i + 1).ToString("00"), AutoStart = false, WorkingDirectory = @"D:\SteamProfiles\Demo", ConfigFile = @"D:\SteamProfiles\Demo\steam_monitor.conf", EnvFile = "", Executable = @"D:\Python\Scripts\steam_monitor.exe" });
        using (SettingsForm form = new SettingsForm(settings, false)) {
            form.Text = "SteamMonitorTray drag test"; form.ShowProfiles();
            ListBox list = (ListBox)typeof(SettingsForm).GetField("list", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(form);
            int moves = 0, none = 0, paints = 0, drops = 0;
            Action receipt = delegate { File.WriteAllText(Path.Combine(Files.Bin, "drag-preview.json"), "{\"moveFeedback\":" + moves + ",\"noneFeedback\":" + none + ",\"invalidations\":" + paints + ",\"drops\":" + drops + ",\"first\":\"" + list.Items[0].ToString() + "\"}"); };
            list.GiveFeedback += delegate(object sender, GiveFeedbackEventArgs e) { if ((e.Effect & DragDropEffects.Move) != 0) moves++; else none++; };
            list.Invalidated += delegate { paints++; };
            list.DragDrop += delegate { drops++; receipt(); };
            form.FormClosed += delegate { receipt(); };
            using (Timer timeout = new Timer { Interval = 300000 }) { timeout.Tick += delegate { form.Close(); }; timeout.Start(); Application.Run(form); }
        }
        return 0;
    }
    internal static int Run() {
        Settings settings = Settings.Defaults(); settings.Language = "zh-Hans"; settings.LaunchMode = "AlwaysTray";
        settings.Profiles.Add(new Profile { Name = "Demo monitor", AutoStart = false, WorkingDirectory = @"D:\SteamProfiles\Demo", ConfigFile = @"D:\SteamProfiles\Demo\steam_monitor.conf", EnvFile = "", Executable = @"D:\Python\Scripts\steam_monitor.exe" });
        string data = Path.Combine(Files.Bin, "preview-data");
        StartupRegistration startup = new StartupRegistration(Path.Combine(data, "SteamMonitorTray.lnk"), Path.Combine(Files.Bin, "SteamMonitorTray.exe"), @"Software\SteamMonitorTray\Preview\" + Guid.NewGuid().ToString("N"), "SteamMonitorTray.lnk");
        TrayContext context = new TrayContext(settings, true, data, startup);
        using (Form window = new Form { Text = "SteamMonitorTray native menu preview", Size = new Size(700, 350), StartPosition = FormStartPosition.CenterScreen, AutoScaleMode = AutoScaleMode.Dpi }) {
            FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), FlowDirection = FlowDirection.TopDown };
            panel.Controls.Add(new Label { Text = "Local UI fixture. No enabled monitor, Steam request or real settings change.", AutoSize = true });
            Button menu = new Button { Text = "Open native tray menu", AutoSize = true };
            menu.Click += delegate { context.ShowMenu(new Point(window.Left + 100, window.Top + 130)); };
            Button edit = new Button { Text = "Preview settings", AutoSize = true };
            edit.Click += delegate { using (SettingsForm form = new SettingsForm(settings, false)) form.ShowDialog(window); };
            panel.Controls.Add(menu); panel.Controls.Add(edit); window.Controls.Add(panel);
            bool exited = false;
            context.ThreadExit += delegate { exited = true; window.Close(); };
            window.FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!exited) { e.Cancel = true; context.CreateMenuSnapshot().Items[context.CreateMenuSnapshot().Items.Count - 1].Invoke(); } };
            using (Timer timeout = new Timer { Interval = 180000 }) {
                timeout.Tick += delegate { window.Close(); }; timeout.Start(); Application.Run(window);
            }
        }
        return 0;
    }
}
