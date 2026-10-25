using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SteamMonitorTray {
    internal sealed class HelpLabel : Label {
        public HelpLabel(string text) {
            Text = text; AutoSize = true; Dock = DockStyle.Top; AutoEllipsis = false;
            UseMnemonic = false; ForeColor = Color.DimGray; Margin = new Padding(3, 3, 3, 10);
        }
        public override Size GetPreferredSize(Size proposedSize) {
            int width = proposedSize.Width;
            if (width <= 1 || width > 10000) width = Width > 1 ? Width : 500;
            Size measured = TextRenderer.MeasureText(Text, Font, new Size(width, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            return new Size(width, measured.Height + Padding.Vertical + LogicalToDeviceUnits(2));
        }
    }
    // Settings guidance is displayed alongside each field.
    internal sealed class SettingsForm : Form {
        private readonly Settings draft;
        private readonly bool locked;
        private readonly ListBox list = new ListBox();
        private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        private readonly CheckBox enabled = new CheckBox { Text = "打开托盘程序后，自动开始监测此账号", AutoSize = true };
        private readonly CheckBox restart = new CheckBox { Text = "异常退出后有限重试（默认 5 / 15 / 30 秒）", AutoSize = true };
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox shell = new TextBox(), python = new TextBox();
        private readonly TableLayoutPanel editor;
        private readonly HelpLabel instructions;
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ShowAlways = true };
        private readonly Dictionary<Control, string> textKeys = new Dictionary<Control, string>();
        private readonly Dictionary<TextBox, string> tipKeys = new Dictionary<TextBox, string>();
        private const string ModeHelp = "控制程序刚启动时是否显示 PowerShell 窗口；隐藏时只显示托盘图标。之后仍可从托盘显示或隐藏窗口。";
        private int selected = -1;
        public Settings Result { get; private set; }
        public SettingsForm(Settings settings, bool sessionsExist) {
            draft = Json.ReadText<Settings>(Json.Text(settings)); locked = sessionsExist;
            L.SetLanguage(draft.Language);
            Text = "SteamMonitorTray 设置"; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(780, 580); Size = new Size(980, 810);
            Font = new Font("Microsoft YaHei UI", 9F); AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(14) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); Controls.Add(root);
            TableLayoutPanel global = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 5 };
            for (int i = 0; i < 5; i++) global.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            global.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); global.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); global.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mode.Items.AddRange(new object[] { "不隐藏", "仅开机自启动时隐藏", "每次启动时隐藏" });
            mode.SelectedIndex = draft.LaunchMode == "AlwaysWindow" ? 0 : (draft.LaunchMode == "AlwaysTray" ? 2 : 1);
            mode.Dock = DockStyle.Fill; global.Controls.Add(new Label { Text = "启动时隐藏窗口", AutoSize = true }, 0, 0); global.Controls.Add(mode, 1, 0);
            tips.SetToolTip(mode, L.Text(ModeHelp));
            AddGlobal(global, "PowerShell", shell, draft.ShellPath, 1); AddGlobal(global, "Python", python, draft.PythonPath, 2);
            restart.Checked = draft.AutomaticRestart; global.Controls.Add(restart, 1, 3); root.Controls.Add(global, 0, 0);
            global.SetColumnSpan(restart, 2);
            HelpLabel environmentHelp = new HelpLabel("上面的 PowerShell 和 Python 是运行环境。已自动识别到有效程序时，通常不需要修改。");
            global.Controls.Add(environmentHelp, 0, 4); global.SetColumnSpan(environmentHelp, 3);
            instructions = new HelpLabel("") { ForeColor = Color.FromArgb(45, 70, 105), Margin = new Padding(3, 8, 3, 14) };
            root.Controls.Add(instructions, 0, 1);
            TableLayoutPanel profiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215)); profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 }; left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            list.Dock = DockStyle.Fill; list.SelectedIndexChanged += delegate { SelectProfile(); }; left.Controls.Add(list);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            Button add = new Button { Text = "添加已有配置…", AutoSize = true, Enabled = !locked }, remove = new Button { Text = "移除", AutoSize = true, Width = 60, Enabled = !locked };
            add.Click += delegate { AddProfile(); }; remove.Click += delegate { RemoveProfile(); }; actions.Controls.Add(add); actions.Controls.Add(remove); left.Controls.Add(actions, 0, 1); profiles.Controls.Add(left, 0, 0);
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12, 0, 0, 0) };
            editor = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 13, Padding = new Padding(0, 0, 7, 0), Enabled = !locked };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 13; i++) editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            string[] keys = { "Name", "WorkingDirectory", "ConfigFile", "EnvFile", "Executable" }, labels = { "配置名称", "工作目录", "配置文件（.conf）", "密钥文件（.env）", "监测程序（exe）" };
            string[] help = {
                "只是托盘中的显示名称，例如“主要账号”。这里不用填写 SteamID。",
                "监测运行时使用的文件夹。通常选择你原来保存 steam_monitor.conf 的目录。",
                "选择原有 steam_monitor.conf；默认监测账号、通知开关等设置保存在这个文件中。",
                "通常选择同目录的 .env；Steam API key、Discord webhook 等密钥保存在这里。只有已另行设置环境变量时才留空。",
                "选择已安装的 steam_monitor.exe；不是本托盘程序，也不是 powershell.exe 或 python.exe。通常会自动找到。"
            };
            for (int i = 0; i < keys.Length; i++) {
                string key = keys[i]; TextBox box = new TextBox { Dock = DockStyle.Fill }; fields.Add(key, box);
                TrackTip(box, help[i]);
                editor.Controls.Add(new Label { Text = labels[i], AutoSize = true, Anchor = AnchorStyles.Left }, 0, i * 2); editor.Controls.Add(box, 1, i * 2);
                if (i > 0) { Button browse = new Button { Text = "浏览…", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right }; browse.Click += delegate { Browse(box, key == "WorkingDirectory"); }; editor.Controls.Add(browse, 2, i * 2); }
                HelpLabel description = new HelpLabel(help[i]); editor.Controls.Add(description, 1, i * 2 + 1); editor.SetColumnSpan(description, 2);
            }
            editor.Controls.Add(enabled, 1, 10); editor.SetColumnSpan(enabled, 2);
            HelpLabel startHelp = new HelpLabel("这个勾选决定打开程序后是否开始监测。Windows 登录自启动需要在托盘菜单中单独注册。");
            editor.Controls.Add(startHelp, 1, 11); editor.SetColumnSpan(startHelp, 2);
            HelpLabel note = new HelpLabel("日志保存位置由配置文件决定。新增账号请准备另一份配置，并为它使用独立的日志、CSV 和状态文件，避免互相覆盖。");
            editor.Controls.Add(note, 0, 12); editor.SetColumnSpan(note, 3);
            scroll.Controls.Add(editor); profiles.Controls.Add(scroll, 1, 0); root.Controls.Add(profiles, 0, 2);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(3, 12, 3, 3) };
            Button save = new Button { Text = "保存", AutoSize = true, MinimumSize = new Size(90, 0) }, cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 0) };
            save.Click += delegate { Save(); }; footer.Controls.Add(save); footer.Controls.Add(cancel); root.Controls.Add(footer, 0, 3); CancelButton = cancel;
            RebuildList();
            CaptureText(this); L.LanguageChanged += ApplyLanguage; ApplyLanguage();
            Shown += delegate { FitWorkingArea(); };
        }
        private void FitWorkingArea() {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e) {
            base.OnDpiChanged(e);
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)FitWorkingArea);
        }
        private void CaptureText(Control control) {
            if (!(control is TextBox) && !(control is ListBox) && !(control is ComboBox) && control != instructions && !String.IsNullOrEmpty(control.Text)) textKeys[control] = control.Text;
            foreach (Control child in control.Controls) CaptureText(child);
        }
        private void ApplyLanguage() {
            draft.Language = L.Selection;
            SuspendLayout();
            foreach (KeyValuePair<Control, string> pair in textKeys) pair.Key.Text = L.Text(pair.Value);
            int selectedMode = mode.SelectedIndex;
            mode.Items.Clear(); mode.Items.AddRange(new object[] { L.Text("不隐藏"), L.Text("仅开机自启动时隐藏"), L.Text("每次启动时隐藏") }); mode.SelectedIndex = selectedMode;
            RightToLeft = L.IsRtl ? RightToLeft.Yes : RightToLeft.No; RightToLeftLayout = L.IsRtl;
            shell.RightToLeft = python.RightToLeft = RightToLeft.No;
            foreach (KeyValuePair<string, TextBox> pair in fields) if (pair.Key != "Name") pair.Value.RightToLeft = RightToLeft.No;
            foreach (TextBox box in tipKeys.Keys) UpdateTip(box);
            tips.SetToolTip(mode, L.Text(ModeHelp)); UpdateInstructions();
            ResumeLayout(true);
        }
        private void TrackTip(TextBox box, string help) { tipKeys[box] = help; box.TextChanged += delegate { UpdateTip(box); }; UpdateTip(box); }
        private void UpdateTip(TextBox box) { tips.SetToolTip(box, L.Text(tipKeys[box]) + (String.IsNullOrEmpty(box.Text) ? "" : Environment.NewLine + box.Text)); }
        private void UpdateInstructions() {
            editor.Enabled = !locked && selected >= 0;
            if (locked) instructions.Text = L.Text("全局选项仍可修改。要编辑账号配置，请先停止监测，并在所有已有 PowerShell 窗口中输入 exit 关闭会话，然后重新打开设置。");
            else if (draft.Profiles.Count == 0) instructions.Text = L.Text("还没有添加账号配置。请点击左下方“添加已有配置…”，选择原来保存 steam_monitor.conf 的文件夹；大部分路径会自动填写。");
            else instructions.Text = L.Text("选择左侧配置，确认右侧路径，勾选自动开始监测后保存。这里只引用原有文件，不复制密钥、监测账号或历史记录到项目中。");
        }
        private void AddGlobal(TableLayoutPanel panel, string label, TextBox box, string value, int row) {
            panel.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row); box.Text = value; box.Dock = DockStyle.Fill; panel.Controls.Add(box, 1, row);
            Button browse = new Button { Text = "浏览", AutoSize = true }; browse.Click += delegate { Browse(box, false); }; panel.Controls.Add(browse, 2, row);
        }
        private void Browse(TextBox box, bool folder) {
            if (folder) { using (FolderBrowserDialog dialog = new FolderBrowserDialog()) { if (Directory.Exists(box.Text)) dialog.SelectedPath = box.Text; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath; } }
            else { using (OpenFileDialog dialog = new OpenFileDialog { Filter = L.Text("所有文件|*.*"), CheckFileExists = true }) { if (File.Exists(box.Text)) dialog.FileName = box.Text; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName; } }
        }
        private void CommitProfile() {
            if (selected < 0 || selected >= draft.Profiles.Count || locked) return;
            Profile p = draft.Profiles[selected]; p.Name = fields["Name"].Text.Trim(); p.WorkingDirectory = fields["WorkingDirectory"].Text.Trim();
            p.ConfigFile = fields["ConfigFile"].Text.Trim(); p.EnvFile = fields["EnvFile"].Text.Trim(); p.Executable = fields["Executable"].Text.Trim(); p.Enabled = enabled.Checked;
        }
        private void SelectProfile() {
            CommitProfile(); selected = list.SelectedIndex;
            if (selected < 0) { foreach (TextBox box in fields.Values) box.Clear(); enabled.Checked = false; UpdateInstructions(); return; }
            Profile p = draft.Profiles[selected]; fields["Name"].Text = p.Name; fields["WorkingDirectory"].Text = p.WorkingDirectory;
            fields["ConfigFile"].Text = p.ConfigFile; fields["EnvFile"].Text = p.EnvFile; fields["Executable"].Text = p.Executable; enabled.Checked = p.Enabled; UpdateInstructions();
        }
        private void RebuildList() {
            selected = -1; list.Items.Clear(); foreach (Profile p in draft.Profiles) list.Items.Add(p.Name);
            if (list.Items.Count > 0) list.SelectedIndex = 0; else SelectProfile();
        }
        private void AddProfile() {
            CommitProfile(); using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = L.Text("选择原来保存 steam_monitor.conf 的文件夹（不是托盘程序源码目录）") }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Profile p = Files.FromDirectory(dialog.SelectedPath);
                if (!File.Exists(p.ConfigFile)) { MessageBox.Show(this, L.Text("这个文件夹中没有 steam_monitor.conf。请选择之前保存监测设置的文件夹。"), L.Text("未找到监测配置"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                p.Name = L.Text("监测 {0}", draft.Profiles.Count + 1); if (!File.Exists(p.EnvFile)) p.EnvFile = "";
                draft.Profiles.Add(p); RebuildList(); list.SelectedIndex = draft.Profiles.Count - 1;
            }
        }
        private void RemoveProfile() { if (selected < 0) return; draft.Profiles.RemoveAt(selected); RebuildList(); }
        protected override void Dispose(bool disposing) { if (disposing) { L.LanguageChanged -= ApplyLanguage; tips.Dispose(); } base.Dispose(disposing); }
        private void Save() {
            CommitProfile(); draft.ShellPath = shell.Text.Trim(); draft.PythonPath = python.Text.Trim(); draft.AutomaticRestart = restart.Checked;
            draft.LaunchMode = new string[] { "AlwaysWindow", "ManualWindowStartupTray", "AlwaysTray" }[mode.SelectedIndex];
            try { Validation.All(draft); Result = draft; DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { MessageBox.Show(this, Program.SafeError(ex), L.Text("检查设置"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
