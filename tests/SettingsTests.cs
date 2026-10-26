using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamMonitorTray;

internal static class SettingsTests {
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static Settings Copy(Settings s) { return Json.ReadText<Settings>(Json.Text(s)); }
    private static bool Rejects(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    private static T Field<T>(object o, string name) { return (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
    private static void Pump(Task task) {
        Stopwatch watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 15) { Application.DoEvents(); Thread.Sleep(15); }
        Assert(task.IsCompleted && !task.IsFaulted, "asynchronous settings check/save completes"); task.GetAwaiter().GetResult();
    }
    public static void Run(string root) {
        L.SetLanguage("zh-Hans"); string directory = Path.Combine(root, "settings-unit"); Directory.CreateDirectory(directory);
        string config = Path.Combine(directory, "steam_monitor.conf");
        string contents = "TARGET_STEAM_ID = 'fixture'\nFILE_SUFFIX = 'fixture'\nSTEAM_STATUS_FILE = 'state.json'\n"; File.WriteAllText(config, contents);
        Settings original = Settings.Defaults(); original.Language = "zh-Hans";
        Profile p = new Profile { Name = "Settings fixture", WorkingDirectory = directory, ConfigFile = config, EnvFile = "", Executable = Path.Combine(Files.Bin, "FakeMonitor.exe") }; original.Profiles.Add(p);
        Assert(Validation.PathIssues(p).Count == 0, "empty optional environment file is valid");
        Profile broken = p.Copy(); broken.WorkingDirectory += ".missing"; broken.ConfigFile += ".missing"; broken.EnvFile = Path.Combine(directory, "missing.env"); broken.Executable += ".missing";
        var issues = Validation.PathIssues(broken);
        Assert(issues.Count == 4 && issues.ContainsKey("WorkingDirectory") && issues.ContainsKey("ConfigFile") && issues.ContainsKey("EnvFile") && issues.ContainsKey("Executable"), "missing files identified per field");
        Assert(Rejects(delegate { Validation.Inspect(broken, original.PythonPath); }), "missing monitor files block a start inspection");
        Settings changed = Copy(original); changed.Profiles[0].ConfigFile += ".missing";
        Assert(Rejects(delegate { Validation.ForSave(changed, original); }), "new invalid enabled paths cannot be saved");
        Settings legacy = Copy(changed); changed = Copy(legacy); changed.LaunchMode = "AlwaysTray";
        Validation.ForSave(changed, legacy); Assert(true, "unrelated global changes preserve an existing missing profile");
        changed.Profiles[0].ConfigFile = config; Validation.ForSave(changed, legacy); Assert(true, "repaired profile can be saved");
        Settings noPython = Copy(original); noPython.PythonPath += ".missing"; changed = Copy(noPython); changed.AutomaticRestart = false;
        Validation.ForSave(changed, noPython); Assert(true, "existing missing Python does not block unrelated settings");
        changed.PythonPath += ".new"; Assert(Rejects(delegate { Validation.ForSave(changed, noPython); }), "new invalid Python path is rejected");
        Settings nullPaths = Copy(original); nullPaths.Profiles[0].WorkingDirectory = nullPaths.Profiles[0].ConfigFile = nullPaths.Profiles[0].Executable = nullPaths.Profiles[0].EnvFile = null;
        changed = Copy(nullPaths); changed.Profiles[0].WorkingDirectory = changed.Profiles[0].ConfigFile = changed.Profiles[0].Executable = changed.Profiles[0].EnvFile = "";
        Validation.ForSave(changed, nullPaths); Assert(true, "legacy null paths can be retained while saving unrelated options");
        changed = Copy(original); Profile duplicate = p.Copy(); duplicate.Id = Guid.NewGuid().ToString("N"); changed.Profiles.Add(duplicate);
        Assert(Rejects(delegate { Validation.ForSave(changed, original); }), "adding an output collision is rejected");
        Profile otherBroken = broken.Copy(); otherBroken.Id = Guid.NewGuid().ToString("N"); changed = Copy(original); changed.Profiles.Add(otherBroken);
        Validation.ForStart(changed, changed.Profiles[0]); Assert(true, "missing sibling profile does not block a valid start");
        using (SettingsForm form = new SettingsForm(original, true)) {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(Screen.PrimaryScreen.Bounds.Right + 1000, 0); form.Show(); Application.DoEvents();
            var fields = Field<Dictionary<string, TextBox>>(form, "fields");
            TabPage profilesPage = Field<TabPage>(form, "profilesPage");
            TextBox shell = Field<TextBox>(form, "shell"), python = Field<TextBox>(form, "python");
            Assert(profilesPage.Contains(shell) && profilesPage.Contains(python) && shell.Visible && python.Visible, "runtime paths appear on the monitoring profiles page");
            Assert(shell.Enabled && !shell.ReadOnly && python.Enabled && !python.ReadOnly, "moving runtime paths preserves global edit behavior while profile paths are locked");
            form.LanguageRequested += delegate(string code) { }; // Simulate rejected persistence.
            Field<ComboBox>(form, "language").SelectedIndex = 2;
            Assert(Field<ComboBox>(form, "language").SelectedIndex == 1 && L.Selection == "zh-Hans", "failed language persistence restores actual selection");
            foreach (TextBox box in fields.Values) Assert(box.ReadOnly && box.Enabled && box.ForeColor == SystemColors.WindowText, "view mode keeps readable, copyable paths");
            Assert(Field<Button>(form, "prepare").Visible && Field<Button>(form, "prepare").Enabled, "one-click edit entry is available");
            form.BeginPreparing("正在停止监测…"); Assert(form.IsLocked && !Field<Button>(form, "save").Enabled, "cannot save paths during graceful stop");
            Assert(!shell.Enabled && !python.Enabled, "runtime paths are disabled while preparing to edit");
            form.EditReady(); Assert(!fields["ConfigFile"].ReadOnly && Field<Button>(form, "resume").Visible, "same window unlocks after sessions close");
            fields["ConfigFile"].Text = config + ".missing";
            Assert(Field<Dictionary<string, HelpLabel>>(form, "fieldErrors")["ConfigFile"].Text.Contains("配置文件不存在"), "missing path appears beside its field");
            Assert(Field<ListBox>(form, "list").Items[0].ToString().Contains("文件缺失"), "profile list marks missing files");
            fields["ConfigFile"].Text = config; Pump(form.RecheckAsync());
            Assert(Field<HelpLabel>(form, "checkStatus").Text == "检查完成：1 个配置通过，0 个需要处理。", "recheck clears repaired path errors and reports totals");
            File.WriteAllText(config, "import os\n"); Pump(form.RecheckAsync());
            Assert(Field<ListBox>(form, "list").Items[0].ToString().Contains("配置需检查"), "static configuration errors are visible without executing config");
            File.WriteAllText(config, contents); Pump(form.RecheckAsync());
            Pump(form.SaveAsync(false)); Assert(form.Result != null && !form.ResumeRequested, "save only records settings without requesting restart");
        }
        using (SettingsForm form = new SettingsForm(legacy, true)) {
            form.ShowInTaskbar = false; Pump(form.SaveAsync(false));
            Assert(form.Result.Profiles[0].ConfigFile.EndsWith(".missing"), "view-mode save preserves broken paths rather than replacing them");
        }
        Settings collisions = Copy(original); duplicate = p.Copy(); duplicate.Id = Guid.NewGuid().ToString("N"); collisions.Profiles.Add(duplicate);
        using (SettingsForm form = new SettingsForm(collisions, false)) {
            form.ShowInTaskbar = false; Pump(form.RecheckAsync());
            Assert(Field<Dictionary<string, string>>(form, "inspectionErrors").Count == 2, "recheck identifies both profiles sharing output files");
        }
        Settings empty = Copy(original); empty.Profiles.Clear();
        using (SettingsForm form = new SettingsForm(empty, false)) Assert(!Field<Button>(form, "recheck").Enabled, "empty profile list cannot report a successful config check");
    }
}
