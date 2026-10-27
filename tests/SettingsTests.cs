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
        Settings migration = Settings.Defaults(); migration.Profiles.Add(new Profile { Name = "legacy", AutoStart = false });
        string legacyText = Json.Text(migration).Replace("\"Version\":2", "\"Version\":1").Replace("\"AutoStart\":false", "\"Enabled\":false");
        Settings upgraded = Json.ReadText<Settings>(legacyText);
        Assert(upgraded.Version == 2 && upgraded.Migrated && !upgraded.Profiles[0].AutoStart, "version one disabled choice migrates to automatic-start choice");
        string migrationPath = Path.Combine(root, "migration-settings.json"); File.WriteAllText(migrationPath, legacyText);
        Json.Save(migrationPath, upgraded); Assert(File.ReadAllText(migrationPath + ".v1.bak") == legacyText, "migration backs up original settings before first write");
        Assert(!File.ReadAllText(migrationPath).Contains("Enabled") && Json.Read<Settings>(migrationPath).Version == 2, "version two serializes AutoStart without legacy Enabled");
        Json.Save(migrationPath, upgraded); Assert(File.ReadAllText(migrationPath + ".v1.bak") == legacyText, "later saves do not replace migration backup");
        OutputLeases leases = new OutputLeases(); Assert(leases.Acquire("a", new[] { "C:\\test\\shared.csv" }) && !leases.Acquire("b", new[] { "c:\\TEST\\shared.csv" }), "session leases reject case-insensitive output conflicts");
        Assert(leases.Contains("a") && leases.Conflict("a", new[] { "C:\\test\\shared.csv" }) == null, "lease owner can reuse its own paths");
        leases.Release("a"); Assert(leases.Acquire("b", new[] { "C:\\test\\shared.csv" }), "session exit releases output for alternate version");
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
        duplicate.AutoStart = false; Validation.ForSave(changed, original); Assert(true, "manual-only alternate profile may share output paths");
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
            bool restored = false; form.SessionExists = delegate { return true; };
            form.RestoreWindowRequested += delegate { restored = true; };
            Pump(form.OpenWindowAsync(false)); Assert(restored, "existing window restores even when its configuration file is missing");
            form.ShowInTaskbar = false; Pump(form.SaveAsync(false));
            Assert(form.Result.Profiles[0].ConfigFile.EndsWith(".missing"), "view-mode save preserves broken paths rather than replacing them");
        }
        Settings collisions = Copy(original); duplicate = p.Copy(); duplicate.Id = Guid.NewGuid().ToString("N"); collisions.Profiles.Add(duplicate);
        using (SettingsForm form = new SettingsForm(collisions, false)) {
            form.ShowInTaskbar = false; Pump(form.RecheckAsync());
            Assert(Field<Dictionary<string, string>>(form, "inspectionErrors").Count == 2, "recheck identifies both profiles sharing output files");
        }
        collisions.Profiles[1].AutoStart = false;
        using (SettingsForm form = new SettingsForm(collisions, false)) {
            form.ShowInTaskbar = false; Pump(form.RecheckAsync());
            Assert(Field<Dictionary<string, string>>(form, "inspectionErrors").Count == 0 && Field<Dictionary<string, string>>(form, "inspectionWarnings").Count == 2, "shared alternate output is a warning, not a broken configuration");
            var fields = Field<Dictionary<string, TextBox>>(form, "fields"); fields["Name"].Text = "unsaved name";
            string selectedId = Field<Settings>(form, "draft").Profiles[0].Id;
            form.MoveProfile(0, 1);
            Assert(Field<Settings>(form, "draft").Profiles[1].Id == selectedId && fields["Name"].Text == "unsaved name", "reordering preserves stable identity, selection and unsaved field edits");
            Assert(Field<Button>(form, "moveUp").Enabled && !Field<Button>(form, "moveDown").Enabled, "reorder arrows follow list boundaries");
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(Screen.PrimaryScreen.Bounds.Right + 1000, 0); form.Show(); form.ShowProfiles(); Application.DoEvents();
            ListBox orderList = Field<ListBox>(form, "list");
            Pump(form.RecheckAsync()); Application.DoEvents();
            HelpLabel checkStatus = Field<HelpLabel>(form, "checkStatus");
            TableLayoutPanel profileArea = Field<TableLayoutPanel>(form, "profiles");
            int stableTop = profileArea.Top, statusChanges = 0; bool jumped = false, overlapped = false;
            EventHandler statusChanged = delegate { statusChanges++; };
            EventHandler moved = delegate { jumped |= profileArea.Top != stableTop; overlapped |= profileArea.Top < profileArea.Parent.PointToClient(checkStatus.PointToScreen(new Point(0, checkStatus.Height))).Y; };
            checkStatus.TextChanged += statusChanged; profileArea.LocationChanged += moved;
            for (int repeat = 0; repeat < 8; repeat++) { orderList.SelectedIndex = 1 - orderList.SelectedIndex; Application.DoEvents(); }
            checkStatus.TextChanged -= statusChanged; profileArea.LocationChanged -= moved;
            Assert(checkStatus.Text.Contains("共用输出") && statusChanges == 0 && !jumped && !overlapped, "switching checked shared-output profiles never publishes a partial summary or moves the profile area");
            orderList.TopIndex = 0; Point dropPoint = orderList.PointToScreen(new Point(4, orderList.GetItemRectangle(0).Top + 1));
            DataObject dragData = new DataObject("SteamMonitorTray.Profile", selectedId);
            DragEventArgs drag = new DragEventArgs(dragData, 1, dropPoint.X, dropPoint.Y, DragDropEffects.Move, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(orderList, new object[] { drag });
            Assert(drag.Effect == DragDropEffects.Move && Field<int>(form, "insertion") == 0, "drag callback offers move with an insertion marker");
            int dragInvalidations = 0;
            InvalidateEventHandler dragPaint = delegate { dragInvalidations++; };
            orderList.Invalidated += dragPaint;
            for (int repeat = 0; repeat < 12; repeat++) typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(orderList, new object[] { drag });
            orderList.Invalidated -= dragPaint;
            Assert(dragInvalidations == 0, "stationary drag feedback does not continuously erase and repaint the list");
            drag.Effect = DragDropEffects.None;
            typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(orderList, new object[] { drag });
            Assert(drag.Effect == DragDropEffects.Move, "entering the reorder list immediately keeps the move cursor effect");
            ReorderListBox dragList = (ReorderListBox)orderList; Cursor savedCursor = Cursor.Current;
            try {
                dragList.Dragging = true;
                GiveFeedbackEventArgs feedback = new GiveFeedbackEventArgs(DragDropEffects.Move, true);
                typeof(Control).GetMethod("OnGiveFeedback", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dragList, new object[] { feedback });
                Message cursorMessage = Message.Create(dragList.Handle, 0x20, IntPtr.Zero, IntPtr.Zero);
                object[] cursorArguments = { cursorMessage };
                typeof(ReorderListBox).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dragList, cursorArguments);
                Assert(!feedback.UseDefaultCursors && Cursor.Current.Handle == Cursors.SizeAll.Handle && ((Message)cursorArguments[0]).Result == new IntPtr(1), "native list cursor messages cannot replace the active drag feedback cursor");
            } finally { dragList.Dragging = false; Cursor.Current = savedCursor; }
            Point bottomPoint = orderList.PointToScreen(new Point(4, orderList.ClientSize.Height - 2));
            DragEventArgs bottomDrag = new DragEventArgs(dragData, 1, bottomPoint.X, bottomPoint.Y, DragDropEffects.Move, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(orderList, new object[] { bottomDrag });
            Assert(!Field<System.Windows.Forms.Timer>(form, "dragTimer").Enabled && orderList.TopIndex == 0, "a short list never starts edge scrolling when all profiles already fit");
            typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(orderList, new object[] { drag });
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(orderList, new object[] { drag });
            Assert(Field<Settings>(form, "draft").Profiles[0].Id == selectedId && orderList.SelectedIndex == 0 && fields["Name"].Text == "unsaved name", "drop callback reorders the selected profile without losing its draft");
            bool requested = false;
            form.WindowRequested += delegate(Settings next, string id) { requested = id == selectedId; form.ReturnToView(next, true); };
            Pump(form.OpenWindowAsync(false)); Assert(requested && !form.IsDisposed && form.IsLocked, "open window saves draft and returns same settings form to view mode");
            Assert(!Field<Button>(form, "moveUp").Enabled, "live sessions prevent reordering");
        }
        Settings scrolling = Copy(original); scrolling.Profiles.Clear();
        for (int i = 0; i < 45; i++) { Profile item = p.Copy(); item.Id = Guid.NewGuid().ToString("N"); item.Name = "Scroll " + i; item.AutoStart = false; scrolling.Profiles.Add(item); }
        using (SettingsForm form = new SettingsForm(scrolling, false)) {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(Screen.PrimaryScreen.Bounds.Right + 1000, 0); form.Show(); form.ShowProfiles(); Application.DoEvents();
            ListBox dragList = Field<ListBox>(form, "list");
            Point edge = new Point(4, dragList.ClientSize.Height - 2), edgeScreen = dragList.PointToScreen(edge);
            DataObject data = new DataObject("SteamMonitorTray.Profile", scrolling.Profiles[0].Id);
            DragEventArgs over = new DragEventArgs(data, 1, edgeScreen.X, edgeScreen.Y, DragDropEffects.Move, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dragList, new object[] { over });
            int before = dragList.TopIndex, oldInsertion = Field<int>(form, "insertion");
            Assert(Field<System.Windows.Forms.Timer>(form, "dragTimer").Enabled, "overflowing profile list enables edge scrolling");
            form.ScrollDrag(edge);
            Assert(dragList.TopIndex == before + 1 && Field<int>(form, "insertion") > oldInsertion, "edge scrolling recalculates the insertion target without mouse movement");
            Point middle = dragList.PointToScreen(new Point(4, dragList.ClientSize.Height / 2));
            DragEventArgs middleOver = new DragEventArgs(data, 1, middle.X, middle.Y, DragDropEffects.Move, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dragList, new object[] { middleOver });
            Assert(!Field<System.Windows.Forms.Timer>(form, "dragTimer").Enabled, "leaving the scroll edge stops the timer");
            DragEventArgs foreign = new DragEventArgs(new DataObject("SteamMonitorTray.Profile", "unknown-profile"), 1, middle.X, middle.Y, DragDropEffects.Move, DragDropEffects.Move);
            typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dragList, new object[] { foreign });
            Assert(foreign.Effect == DragDropEffects.None && Field<int>(form, "insertion") == -1 && !Field<System.Windows.Forms.Timer>(form, "dragTimer").Enabled, "foreign drag clears feedback and cannot reorder profiles");
            typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dragList, new object[] { over });
            typeof(Control).GetMethod("OnDragLeave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(dragList, new object[] { EventArgs.Empty });
            Assert(Field<int>(form, "insertion") == -1 && !Field<System.Windows.Forms.Timer>(form, "dragTimer").Enabled, "drag leave or cancellation clears the insertion and scroll timer");
        }
        Settings empty = Copy(original); empty.Profiles.Clear();
        using (SettingsForm form = new SettingsForm(empty, false)) Assert(!Field<Button>(form, "recheck").Enabled, "empty profile list cannot report a successful config check");
    }
}
