using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SteamMonitorTray {
    internal sealed class SettingsForm : Form {
        private readonly Settings draft;
        private readonly bool locked;
        private readonly ListBox list = new ListBox();
        private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        private readonly CheckBox enabled = new CheckBox { Text = "启动程序时自动监测此配置", AutoSize = true };
        private readonly CheckBox restart = new CheckBox { Text = "异常退出后有限重试（默认 5 / 15 / 30 秒）", AutoSize = true };
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox shell = new TextBox(), python = new TextBox();
        private int selected = -1;
        public Settings Result { get; private set; }
        public SettingsForm(Settings settings, bool sessionsExist) {
            draft = Json.ReadText<Settings>(Json.Text(settings)); locked = sessionsExist;
            Text = "SteamMonitorTray 设置"; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(850, 680); Size = new Size(930, 740);
            Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 145)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); Controls.Add(root);
            TableLayoutPanel global = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
            global.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); global.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); global.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
            mode.Items.AddRange(new object[] { "手动显示，自动进托盘", "两种启动都进托盘", "两种启动都显示窗口" });
            mode.SelectedIndex = draft.LaunchMode == "AlwaysTray" ? 1 : (draft.LaunchMode == "AlwaysWindow" ? 2 : 0);
            mode.Dock = DockStyle.Fill; global.Controls.Add(new Label { Text = "启动显示方式", AutoSize = true }, 0, 0); global.Controls.Add(mode, 1, 0);
            AddGlobal(global, "PowerShell", shell, draft.ShellPath, 1); AddGlobal(global, "Python", python, draft.PythonPath, 2);
            restart.Checked = draft.AutomaticRestart; global.Controls.Add(restart, 1, 3); root.Controls.Add(global, 0, 0);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = locked ? "全局选项可修改。编辑配置前，请停止监测并关闭相应 PowerShell 窗口。" : "每份配置对应一个账号。仅引用原有文件，不复制凭据或历史。空环境文件表示禁用 dotenv 自动发现。", ForeColor = Color.DimGray }, 0, 1);
            TableLayoutPanel profiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 }; left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            list.Dock = DockStyle.Fill; list.SelectedIndexChanged += delegate { SelectProfile(); }; left.Controls.Add(list);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
            Button add = new Button { Text = "添加…", Enabled = !locked }, remove = new Button { Text = "移除", Enabled = !locked };
            add.Click += delegate { AddProfile(); }; remove.Click += delegate { RemoveProfile(); }; actions.Controls.Add(add); actions.Controls.Add(remove); left.Controls.Add(actions, 0, 1); profiles.Controls.Add(left, 0, 0);
            TableLayoutPanel editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 8, Padding = new Padding(12, 0, 0, 0), Enabled = !locked };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
            string[] keys = { "Name", "WorkingDirectory", "ConfigFile", "EnvFile", "Executable" }, labels = { "配置名称", "工作目录", "监测配置", "环境文件", "监测程序" };
            for (int i = 0; i < keys.Length; i++) {
                string key = keys[i]; TextBox box = new TextBox { Dock = DockStyle.Fill }; fields.Add(key, box);
                editor.Controls.Add(new Label { Text = labels[i], AutoSize = true }, 0, i); editor.Controls.Add(box, 1, i);
                if (i > 0) { Button browse = new Button { Text = "浏览", Dock = DockStyle.Fill }; browse.Click += delegate { Browse(box, key == "WorkingDirectory"); }; editor.Controls.Add(browse, 2, i); }
                editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            }
            editor.Controls.Add(enabled, 1, 5);
            Label note = new Label { Text = "日志目录从所选配置解析。新增账号请先用 steam_monitor --setup 保存另一份配置，并分开输出文件。", Dock = DockStyle.Fill, ForeColor = Color.DimGray };
            editor.Controls.Add(note, 1, 6); editor.SetColumnSpan(note, 2);
            profiles.Controls.Add(editor, 1, 0); root.Controls.Add(profiles, 0, 2);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            Button save = new Button { Text = "保存", Width = 90 }, cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90 };
            save.Click += delegate { Save(); }; footer.Controls.Add(save); footer.Controls.Add(cancel); root.Controls.Add(footer, 0, 3); CancelButton = cancel;
            RebuildList();
        }
        private void AddGlobal(TableLayoutPanel panel, string label, TextBox box, string value, int row) {
            panel.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row); box.Text = value; box.Dock = DockStyle.Fill; panel.Controls.Add(box, 1, row);
            Button browse = new Button { Text = "浏览" }; browse.Click += delegate { Browse(box, false); }; panel.Controls.Add(browse, 2, row);
        }
        private void Browse(TextBox box, bool folder) {
            if (folder) { using (FolderBrowserDialog dialog = new FolderBrowserDialog()) { if (Directory.Exists(box.Text)) dialog.SelectedPath = box.Text; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath; } }
            else { using (OpenFileDialog dialog = new OpenFileDialog { Filter = "所有文件|*.*", CheckFileExists = true }) { if (File.Exists(box.Text)) dialog.FileName = box.Text; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName; } }
        }
        private void CommitProfile() {
            if (selected < 0 || selected >= draft.Profiles.Count || locked) return;
            Profile p = draft.Profiles[selected]; p.Name = fields["Name"].Text.Trim(); p.WorkingDirectory = fields["WorkingDirectory"].Text.Trim();
            p.ConfigFile = fields["ConfigFile"].Text.Trim(); p.EnvFile = fields["EnvFile"].Text.Trim(); p.Executable = fields["Executable"].Text.Trim(); p.Enabled = enabled.Checked;
        }
        private void SelectProfile() {
            CommitProfile(); selected = list.SelectedIndex;
            if (selected < 0) { foreach (TextBox box in fields.Values) box.Clear(); enabled.Checked = false; return; }
            Profile p = draft.Profiles[selected]; fields["Name"].Text = p.Name; fields["WorkingDirectory"].Text = p.WorkingDirectory;
            fields["ConfigFile"].Text = p.ConfigFile; fields["EnvFile"].Text = p.EnvFile; fields["Executable"].Text = p.Executable; enabled.Checked = p.Enabled;
        }
        private void RebuildList() {
            selected = -1; list.Items.Clear(); foreach (Profile p in draft.Profiles) list.Items.Add(p.Name);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        private void AddProfile() {
            CommitProfile(); using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = "选择包含 steam_monitor.conf 的配置目录" }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Profile p = Files.FromDirectory(dialog.SelectedPath); p.Name = "监测 " + (draft.Profiles.Count + 1); draft.Profiles.Add(p); RebuildList(); list.SelectedIndex = draft.Profiles.Count - 1;
            }
        }
        private void RemoveProfile() { if (selected < 0) return; draft.Profiles.RemoveAt(selected); RebuildList(); }
        private void Save() {
            CommitProfile(); draft.ShellPath = shell.Text.Trim(); draft.PythonPath = python.Text.Trim(); draft.AutomaticRestart = restart.Checked;
            draft.LaunchMode = new string[] { "ManualWindowStartupTray", "AlwaysTray", "AlwaysWindow" }[mode.SelectedIndex];
            try { Validation.All(draft); Result = draft; DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { MessageBox.Show(this, Program.SafeError(ex), "检查设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
