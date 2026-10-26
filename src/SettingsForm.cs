using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SteamMonitorTray {
    internal sealed class BufferedTable : TableLayoutPanel {
        public BufferedTable() { DoubleBuffered = true; }
    }
    internal sealed class HelpLabel : Label {
        private readonly Dictionary<int, Size> sizes = new Dictionary<int, Size>();
        private int measuredDpi;
        public HelpLabel(string text) {
            Text = text; AutoSize = true; Dock = DockStyle.Top; AutoEllipsis = false;
            UseMnemonic = false; ForeColor = Color.DimGray; Margin = new Padding(3, 3, 3, 10);
        }
        public override Size GetPreferredSize(Size proposedSize) {
            int width = proposedSize.Width;
            if (width <= 1 || width > 10000) width = Width > 1 ? Width : 500;
            if (measuredDpi != DeviceDpi) { sizes.Clear(); measuredDpi = DeviceDpi; }
            Size cached;
            if (sizes.TryGetValue(width, out cached)) return cached;
            Size measured = TextRenderer.MeasureText(Text, Font, new Size(width, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
            Size result = new Size(width, measured.Height + Padding.Vertical + LogicalToDeviceUnits(2));
            if (sizes.Count >= 64) sizes.Clear(); sizes[width] = result; return result;
        }
        protected override void OnTextChanged(EventArgs e) { if (sizes != null) sizes.Clear(); base.OnTextChanged(e); }
        protected override void OnFontChanged(EventArgs e) { if (sizes != null) sizes.Clear(); base.OnFontChanged(e); }
        protected override void OnPaddingChanged(EventArgs e) { if (sizes != null) sizes.Clear(); base.OnPaddingChanged(e); }
    }
    internal sealed class SettingsForm : Form {
        private readonly Settings draft, original;
        private bool locked, preparing, pausedForEdit, saving, checking, loading;
        internal bool IsLiveResizing { get; private set; }
        private int selected = -1, revision, lastCheckedRevision = -1;
        private string progress, progressDetails;
        private readonly ListBox list = new ListBox { IntegralHeight = false };
        private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, HelpLabel> descriptions = new Dictionary<string, HelpLabel>();
        private readonly Dictionary<string, HelpLabel> fieldErrors = new Dictionary<string, HelpLabel>();
        private readonly Dictionary<string, string> inspectionErrors = new Dictionary<string, string>();
        private readonly List<Button> profileBrowse = new List<Button>();
        private readonly CheckBox enabled = new CheckBox { Text = "打开托盘程序后，自动开始监测此账号", AutoSize = true };
        private readonly CheckBox restart = new CheckBox { Text = "异常退出后有限重试（默认 5 / 15 / 30 秒）", AutoSize = true };
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox startupCheck = new CheckBox { Text = "登录 Windows 后自动启动", AutoSize = true };
        private readonly TabControl pages = new TabControl { Dock = DockStyle.Fill };
        private readonly TabPage generalPage = new TabPage("常规"), profilesPage = new TabPage("监测配置");
        private readonly Panel generalContent, profileContent;
        private bool generalLayoutPaused, profileLayoutPaused;
        private readonly Func<StartupState> readStartup;
        private bool updatingStartup, updatingLanguage;
        private readonly TextBox shell = new TextBox(), python = new TextBox();
        private readonly TableLayoutPanel global, runtime, editor, profiles;
        private readonly HelpLabel instructions, environmentHelp, checkStatus, startupHelp;
        private readonly Button prepare = new Button { AutoSize = true }, recheck = new Button { Text = "检查配置", AutoSize = true };
        private readonly Button add = new Button { Text = "添加已有配置…", AutoSize = true }, remove = new Button { Text = "移除", AutoSize = true };
        private readonly Button save = new Button { AutoSize = true }, resume = new Button { Text = "保存并恢复监测", AutoSize = true };
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ShowAlways = true };
        private readonly Dictionary<Control, string> textKeys = new Dictionary<Control, string>();
        private readonly Dictionary<TextBox, string> tipKeys = new Dictionary<TextBox, string>();
        private readonly Timer healthTimer = new Timer { Interval = 3000 };
        private Font helpFont;
        private const string ModeHelp = "控制程序刚启动时是否显示 PowerShell 窗口；隐藏时只显示托盘图标。之后仍可从托盘显示或隐藏窗口。";
        private const string EnvironmentHelp = "上面的 PowerShell 和 Python 是运行环境。已自动识别到有效程序时，通常不需要修改。";
        public Settings Result { get; private set; }
        internal bool ResumeRequested { get; private set; }
        internal bool IsPreparing { get { return preparing; } }
        internal bool IsLocked { get { return locked; } }
        internal event Action PrepareRequested;
        internal event Action<Settings, bool> SaveRequested;
        internal event Action<bool> StartupRequested;
        internal event Action<string> LanguageRequested;

        public SettingsForm(Settings settings, bool sessionsExist) : this(settings, sessionsExist, null) { }
        internal SettingsForm(Settings settings, bool sessionsExist, Func<StartupState> startupReader) {
            original = Json.ReadText<Settings>(Json.Text(settings)); draft = Json.ReadText<Settings>(Json.Text(settings)); locked = sessionsExist;
            readStartup = startupReader ?? delegate { return new StartupState(false, StartupApproval.NotRecorded); };
            L.SetLanguage(draft.Language);
            Text = "SteamMonitorTray 设置"; StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 11F);
            DoubleBuffered = true;
            AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(820, 620); Size = new Size(1040, 820);
            // Dock the main sections directly: the editor only fills the rest,
            // so a root AutoSize table need not measure its entire subtree.
            Panel root = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14) }; Controls.Add(root);
            global = Table(6); global.Dock = DockStyle.Top;
            mode.Items.AddRange(new object[] { "不隐藏", "仅开机自启动时隐藏", "每次启动时隐藏" });
            mode.SelectedIndex = draft.LaunchMode == "AlwaysWindow" ? 0 : (draft.LaunchMode == "AlwaysTray" ? 2 : 1);
            mode.Dock = DockStyle.Fill; global.Controls.Add(new Label { Text = "启动时隐藏窗口", AutoSize = true }, 0, 0); global.Controls.Add(mode, 1, 0); global.SetColumnSpan(mode, 2);
            global.Controls.Add(new Label { Text = "开机自启动", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            global.Controls.Add(startupCheck, 1, 1); global.SetColumnSpan(startupCheck, 2);
            startupHelp = new HelpLabel(""); global.Controls.Add(startupHelp, 1, 2); global.SetColumnSpan(startupHelp, 2);
            startupCheck.CheckedChanged += delegate {
                if (updatingStartup) return;
                try { if (StartupRequested != null) StartupRequested(startupCheck.Checked); }
                finally { RefreshStartupState(); }
            };
            global.Controls.Add(new Label { Text = "语言", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            language.Dock = DockStyle.Fill; global.Controls.Add(language, 1, 3); global.SetColumnSpan(language, 2);
            language.SelectedIndexChanged += delegate {
                if (updatingLanguage || language.SelectedIndex < 0) return;
                string code = language.SelectedIndex == 0 ? "auto" : L.Codes[language.SelectedIndex - 1];
                if (LanguageRequested != null) LanguageRequested(code); else L.SetLanguage(code);
                if (L.Selection != code) ApplyLanguage(); // A failed save must not leave a misleading selection.
            };
            restart.Checked = draft.AutomaticRestart; global.Controls.Add(restart, 1, 4); global.SetColumnSpan(restart, 2);
            HelpLabel generalNote = new HelpLabel("开机自启动和语言即时生效；其余选项保存后生效。"); global.Controls.Add(generalNote, 0, 5); global.SetColumnSpan(generalNote, 3);
            runtime = Table(4); runtime.Dock = DockStyle.Top;
            HelpLabel runtimeHeading = new HelpLabel("运行环境") { Margin = new Padding(3, 3, 3, 6), ForeColor = SystemColors.ControlText };
            runtime.Controls.Add(runtimeHeading, 0, 0); runtime.SetColumnSpan(runtimeHeading, 3);
            AddRuntime("PowerShell", shell, draft.ShellPath, 1); AddRuntime("Python", python, draft.PythonPath, 2);
            environmentHelp = new HelpLabel(""); runtime.Controls.Add(environmentHelp, 0, 3); runtime.SetColumnSpan(environmentHelp, 3);
            TableLayoutPanel status = new BufferedTable { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            instructions = new HelpLabel("") { ForeColor = Color.FromArgb(45, 70, 105) }; status.Controls.Add(instructions);
            FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            prepare.Click += delegate { if (PrepareRequested != null) PrepareRequested(); };
            recheck.Click += async delegate { await RecheckAsync(); }; tools.Controls.Add(prepare); tools.Controls.Add(recheck); status.Controls.Add(tools);
            status.Controls.Add(new HelpLabel("检查所有配置的文件路径、配置格式和输出文件冲突，不会启动监测或发送通知。"));
            checkStatus = new HelpLabel(""); status.Controls.Add(checkStatus); root.Controls.Add(status);
            profiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); profiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            TableLayoutPanel left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            list.Dock = DockStyle.Fill; list.SelectedIndexChanged += delegate { if (!loading) SelectProfile(); }; left.Controls.Add(list);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            add.Click += delegate { AddProfile(); }; remove.Click += delegate { RemoveProfile(); }; actions.Controls.Add(add); actions.Controls.Add(remove); left.Controls.Add(actions, 0, 1); profiles.Controls.Add(left, 0, 0);
            Panel scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10, 0, 0, 0) };
            editor = Table(18); editor.Dock = DockStyle.Top; editor.Padding = new Padding(0, 0, 7, 0);
            string[] keys = { "Name", "WorkingDirectory", "ConfigFile", "EnvFile", "Executable" }, labels = { "配置名称", "工作目录", "配置文件（.conf）", "密钥文件（.env）", "监测程序（exe）" };
            string[] help = {
                "只是托盘中的显示名称，例如“主要账号”。这里不用填写 SteamID。",
                "监测运行时使用的文件夹。通常选择你原来保存 steam_monitor.conf 的目录。",
                "选择原有 steam_monitor.conf；默认监测账号、通知开关等设置保存在这个文件中。",
                "通常选择同目录的 .env；Steam API key、Discord webhook 等密钥保存在这里。只有已另行设置环境变量时才留空。",
                "选择已安装的 steam_monitor.exe；不是本托盘程序，也不是 powershell.exe 或 python.exe。通常会自动找到。"
            };
            for (int i = 0; i < keys.Length; i++) {
                string key = keys[i]; TextBox box = new TextBox { Dock = DockStyle.Fill }; fields.Add(key, box); tipKeys.Add(box, help[i]);
                box.TextChanged += delegate { Changed(); UpdateTip(box); };
                editor.Controls.Add(new Label { Text = labels[i], AutoSize = true, Anchor = AnchorStyles.Left }, 0, i * 3); editor.Controls.Add(box, 1, i * 3);
                if (i > 0) { Button browse = new Button { Text = "浏览…", AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right }; browse.Click += delegate { Browse(box, key == "WorkingDirectory"); }; profileBrowse.Add(browse); editor.Controls.Add(browse, 2, i * 3); }
                HelpLabel description = new HelpLabel(""); descriptions.Add(key, description); editor.Controls.Add(description, 1, i * 3 + 1); editor.SetColumnSpan(description, 2);
                HelpLabel error = new HelpLabel("") { ForeColor = Color.Firebrick, Visible = false }; fieldErrors.Add(key, error); editor.Controls.Add(error, 1, i * 3 + 2); editor.SetColumnSpan(error, 2);
            }
            editor.Controls.Add(enabled, 1, 15); editor.SetColumnSpan(enabled, 2);
            HelpLabel startHelp = new HelpLabel("这个勾选决定打开程序后是否开始监测。Windows 登录自启动需要在托盘菜单中单独注册。"); editor.Controls.Add(startHelp, 1, 16); editor.SetColumnSpan(startHelp, 2);
            HelpLabel note = new HelpLabel("日志保存位置由配置文件决定。新增账号请准备另一份配置，并为它使用独立的日志、CSV 和状态文件，避免互相覆盖。"); editor.Controls.Add(note, 0, 17); editor.SetColumnSpan(note, 3);
            scroll.Controls.Add(editor); profiles.Controls.Add(scroll, 1, 0); root.Controls.Add(profiles);
            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(3, 12, 3, 3), Padding = new Padding(0, 8, 0, 0) };
            Button cancel = new Button { Text = "取消", AutoSize = true };
            cancel.Click += delegate { Close(); }; CancelButton = cancel;
            save.Click += async delegate { await SaveAsync(false); }; resume.Click += async delegate { await SaveAsync(true); };
            footer.Controls.Add(resume); footer.Controls.Add(save); footer.Controls.Add(cancel); root.Controls.Add(footer);
            generalContent = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(12) };
            generalContent.Controls.Add(global); generalPage.Controls.Add(generalContent);
            profileContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            profileContent.Controls.Add(profiles); profileContent.Controls.Add(status); profileContent.Controls.Add(runtime); profilesPage.Controls.Add(profileContent);
            pages.TabPages.Add(generalPage); pages.TabPages.Add(profilesPage); root.Controls.Add(pages); root.Controls.SetChildIndex(pages, 0);
            pages.SelectedIndex = sessionsExist || draft.Profiles.Count == 0 ? 1 : 0;
            pages.SelectedIndexChanged += delegate { SyncPageLayout(); if (pages.SelectedTab == generalPage) RefreshStartupState(); };
            SyncPageLayout();
            shell.TextChanged += delegate { Changed(); }; python.TextChanged += delegate { Changed(); };
            RebuildList(); CaptureText(this); L.LanguageChanged += ApplyLanguage; ApplyLanguage();
            NormalizeFonts();
            Shown += delegate {
                NormalizeFonts(); Size = new Size(LogicalToDeviceUnits(1040), LogicalToDeviceUnits(820)); FitWorkingArea();
                if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
                RefreshIndicators(); healthTimer.Start();
            };
            healthTimer.Tick += delegate { RefreshIndicators(); RefreshStartupState(); };
            Activated += delegate { RefreshStartupState(); };
        }
        private TableLayoutPanel Table(int rows) {
            TableLayoutPanel table = rows < 18 ? new BufferedTable() : new TableLayoutPanel();
            table.AutoSize = true; table.AutoSizeMode = AutoSizeMode.GrowAndShrink; table.ColumnCount = 3; table.RowCount = rows;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < rows; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return table;
        }
        private void NormalizeFonts() {
            SuspendLayout(); Font old = helpFont; helpFont = new Font(Font.FontFamily, Font.SizeInPoints * 10F / 11F, FontStyle.Regular, GraphicsUnit.Point);
            SetFonts(this); if (old != null) old.Dispose();
            profiles.ColumnStyles[0].Width = LogicalToDeviceUnits(220);
            list.ItemHeight = Math.Max(Font.Height + LogicalToDeviceUnits(6), LogicalToDeviceUnits(24));
            MeasureColumns(global); MeasureColumns(runtime); MeasureColumns(editor); SizeHeaderRows(); ResumeLayout(true);
        }
        private void SizeHeaderRows() {
            // Native ComboBox height is enforced by Windows and may exceed a
            // TableLayoutPanel AutoSize estimate. Reserve its real height.
            SizeInputRows(global, new[] { 0, 1, 3, 4 });
            SizeInputRows(runtime, new[] { 1, 2 });
        }
        private void SizeInputRows(TableLayoutPanel table, int[] inputRows) {
            int inset = LogicalToDeviceUnits(4);
            foreach (Control c in table.Controls) if (inputRows.Contains(table.GetRow(c))) c.Margin = new Padding(3, inset, 3, inset);
            foreach (int row in inputRows) {
                int height = 0;
                foreach (Control c in table.Controls) if (table.GetRow(c) == row)
                    height = Math.Max(height, Math.Max(c.PreferredSize.Height, c is ComboBox ? ((ComboBox)c).PreferredHeight : 0) + c.Margin.Vertical);
                table.RowStyles[row].SizeType = SizeType.Absolute; table.RowStyles[row].Height = height;
            }
        }
        private void SetFonts(Control parent) {
            foreach (Control child in parent.Controls) { child.Font = child is HelpLabel ? helpFont : Font; SetFonts(child); }
        }
        private void MeasureColumns(TableLayoutPanel table) {
            int width = 0;
            foreach (Control c in table.Controls) if (c is Label && !(c is HelpLabel) && table.GetColumn(c) == 0)
                width = Math.Max(width, TextRenderer.MeasureText(c.Text, c.Font).Width + c.Margin.Horizontal + LogicalToDeviceUnits(10));
            table.ColumnStyles[0].Width = width;
        }
        private void FitWorkingArea() {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        }
        protected override void OnDpiChanged(DpiChangedEventArgs e) {
            base.OnDpiChanged(e); if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)delegate { NormalizeFonts(); FitWorkingArea(); });
        }
        protected override void OnResizeBegin(EventArgs e) { IsLiveResizing = true; healthTimer.Stop(); base.OnResizeBegin(e); }
        protected override void OnResizeEnd(EventArgs e) {
            base.OnResizeEnd(e); IsLiveResizing = false;
            if (!IsDisposed) { healthTimer.Start(); BeginInvoke((Action)RefreshIndicators); }
        }
        private void CaptureText(Control control) {
            if (!(control is TextBox) && !(control is ListBox) && !(control is ComboBox) && control != instructions && control != checkStatus && control != startupHelp && control != environmentHelp && control != prepare && control != save && !descriptions.ContainsValue(control as HelpLabel) && !fieldErrors.ContainsValue(control as HelpLabel) && !String.IsNullOrEmpty(control.Text)) textKeys[control] = control.Text;
            foreach (Control child in control.Controls) CaptureText(child);
        }
        private void ApplyLanguage() {
            draft.Language = L.Selection; SuspendLayout();
            foreach (KeyValuePair<Control, string> pair in textKeys) pair.Key.Text = L.Text(pair.Value);
            int index = mode.SelectedIndex; mode.Items.Clear(); mode.Items.AddRange(new object[] { L.Text("不隐藏"), L.Text("仅开机自启动时隐藏"), L.Text("每次启动时隐藏") }); mode.SelectedIndex = index;
            updatingLanguage = true;
            try { language.Items.Clear(); language.Items.Add(L.Text("自动（跟随系统）")); language.Items.AddRange(L.Names.Cast<object>().ToArray()); language.SelectedIndex = draft.Language == "auto" ? 0 : Array.IndexOf(L.Codes, draft.Language) + 1; }
            finally { updatingLanguage = false; }
            RightToLeft = L.IsRtl ? RightToLeft.Yes : RightToLeft.No; RightToLeftLayout = L.IsRtl;
            pages.RightToLeftLayout = L.IsRtl;
            shell.RightToLeft = python.RightToLeft = RightToLeft.No;
            foreach (var pair in fields) { if (pair.Key != "Name") pair.Value.RightToLeft = RightToLeft.No; UpdateTip(pair.Value); }
            tips.SetToolTip(mode, L.Text(ModeHelp)); UpdateControls(); RefreshIndicators(); RefreshStartupState(); MeasureColumns(global); MeasureColumns(runtime); MeasureColumns(editor); SizeHeaderRows(); ResumeLayout(true);
        }
        internal void ShowProfiles() { pages.SelectedTab = profilesPage; }
        internal void ShowGeneral() { pages.SelectedTab = generalPage; }
        private void SyncPageLayout() {
            // TabControl resizes hidden pages too. Keep their content layout
            // suspended, then catch up once when that page becomes visible.
            bool general = pages.SelectedTab == generalPage;
            if (general && generalLayoutPaused) { generalLayoutPaused = false; generalPage.ResumeLayout(true); generalContent.ResumeLayout(true); }
            else if (!general && !generalLayoutPaused) { generalPage.SuspendLayout(); generalContent.SuspendLayout(); generalLayoutPaused = true; }
            if (!general && profileLayoutPaused) { profileLayoutPaused = false; profilesPage.ResumeLayout(true); profileContent.ResumeLayout(true); }
            else if (general && !profileLayoutPaused) { profilesPage.SuspendLayout(); profileContent.SuspendLayout(); profileLayoutPaused = true; }
        }
        internal void RefreshStartupState() {
            if (IsDisposed) return;
            StartupState state = readStartup(); updatingStartup = true;
            try { startupCheck.Checked = state.Registered; } finally { updatingStartup = false; }
            startupHelp.Text = state.Description; startupHelp.ForeColor = state.HasWarning ? Color.DarkOrange : Color.DimGray;
        }
        private void UpdateTip(TextBox box) { tips.SetToolTip(box, L.Text(tipKeys[box]) + (String.IsNullOrEmpty(box.Text) ? "" : Environment.NewLine + box.Text)); }
        private void Changed() {
            if (loading) return; revision++; inspectionErrors.Clear(); RefreshIndicators();
        }
        internal void SetSessionsExist(bool exist) { if (!pausedForEdit && locked != exist) { locked = exist; UpdateControls(); } }
        internal void BeginPreparing(string message) { pausedForEdit = true; preparing = true; locked = true; progress = message; progressDetails = null; UpdateControls(); }
        internal void EditProgress(string message) { progress = message; UpdateControls(); }
        internal void EditReady() { locked = false; preparing = false; progress = progressDetails = null; UpdateControls(); }
        internal void EditBlocked(string message, string details) { locked = true; preparing = false; progress = message; progressDetails = details; UpdateControls(); }
        private void UpdateControls() {
            bool editable = !locked && !saving && selected >= 0;
            foreach (TextBox box in fields.Values) { box.ReadOnly = !editable; box.BackColor = SystemColors.Window; box.ForeColor = SystemColors.WindowText; }
            foreach (Button browse in profileBrowse) browse.Enabled = editable;
            enabled.Enabled = editable; add.Enabled = remove.Enabled = !locked && !preparing && !saving;
            prepare.Visible = locked; prepare.Enabled = !preparing && !saving;
            prepare.Text = L.Text(pausedForEdit ? "重试进入编辑模式" : "停止监测并编辑");
            recheck.Enabled = draft.Profiles.Count > 0 && !checking && !saving && !preparing;
            save.Text = L.Text(pausedForEdit ? "仅保存" : "保存"); save.Enabled = !preparing && !saving;
            resume.Visible = pausedForEdit && !locked; resume.Enabled = !preparing && !saving;
            global.Enabled = runtime.Enabled = !preparing && !saving;
            if (progress != null) instructions.Text = L.Text(progress) + (String.IsNullOrEmpty(progressDetails) ? "" : Environment.NewLine + L.Text("尚未关闭的会话：{0}", progressDetails));
            else if (locked) instructions.Text = L.Text("查看模式：路径可以复制。点击“停止监测并编辑”后，程序会正常停止全部监测并关闭专用会话，再解锁本页。");
            else if (pausedForEdit) instructions.Text = L.Text("编辑模式：监测已停止。可仅保存，或保存后恢复此前正在监测的配置。取消不会自动恢复监测。");
            else if (draft.Profiles.Count == 0) instructions.Text = L.Text("还没有添加账号配置。请点击左下方“添加已有配置…”，选择原来保存 steam_monitor.conf 的文件夹；大部分路径会自动填写。");
            else instructions.Text = L.Text("选择左侧配置，确认右侧路径，勾选自动开始监测后保存。这里只引用原有文件，不复制密钥、监测账号或历史记录到项目中。");
        }
        private void AddRuntime(string label, TextBox box, string value, int row) {
            runtime.Controls.Add(new Label { Text = label, AutoSize = true }, 0, row); box.Text = value; box.Dock = DockStyle.Fill; runtime.Controls.Add(box, 1, row);
            Button browse = new Button { Text = "浏览…", AutoSize = true }; browse.Click += delegate { Browse(box, false); }; runtime.Controls.Add(browse, 2, row);
        }
        private void Browse(TextBox box, bool folder) {
            if (folder) { using (FolderBrowserDialog dialog = new FolderBrowserDialog()) { if (Directory.Exists(box.Text)) dialog.SelectedPath = box.Text; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath; } }
            else { using (OpenFileDialog dialog = new OpenFileDialog { Filter = L.Text("所有文件|*.*"), CheckFileExists = true }) { if (File.Exists(box.Text)) dialog.FileName = box.Text; if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName; } }
        }
        private Profile CurrentProfile() {
            if (selected < 0 || selected >= draft.Profiles.Count) return null;
            Profile p = draft.Profiles[selected].Copy();
            p.Name = fields["Name"].Text.Trim(); p.WorkingDirectory = fields["WorkingDirectory"].Text.Trim(); p.ConfigFile = fields["ConfigFile"].Text.Trim(); p.EnvFile = fields["EnvFile"].Text.Trim(); p.Executable = fields["Executable"].Text.Trim(); p.Enabled = enabled.Checked; return p;
        }
        private void CommitProfile() { if (!locked && selected >= 0) draft.Profiles[selected] = CurrentProfile(); }
        private void SelectProfile() {
            CommitProfile(); selected = list.SelectedIndex; loading = true;
            try {
                if (selected < 0) { foreach (TextBox box in fields.Values) box.Clear(); enabled.Checked = false; }
                else { Profile p = draft.Profiles[selected]; fields["Name"].Text = p.Name; fields["WorkingDirectory"].Text = p.WorkingDirectory; fields["ConfigFile"].Text = p.ConfigFile; fields["EnvFile"].Text = p.EnvFile; fields["Executable"].Text = p.Executable; enabled.Checked = p.Enabled; }
            } finally { loading = false; }
            UpdateControls(); RefreshIndicators();
        }
        private void RebuildList() {
            loading = true; selected = -1; list.Items.Clear(); foreach (Profile p in draft.Profiles) list.Items.Add(p.Name);
            if (list.Items.Count > 0) list.SelectedIndex = 0; loading = false; SelectProfile();
        }
        private void RefreshIndicators() {
            if (loading || IsDisposed) return;
            Profile current = CurrentProfile(); var issues = current == null ? new Dictionary<string, string>() : Validation.PathIssues(current);
            foreach (var pair in descriptions) {
                string problem; issues.TryGetValue(pair.Key, out problem);
                pair.Value.Text = L.Text(tipKeys[fields[pair.Key]]);
                fieldErrors[pair.Key].Text = problem == null ? "" : L.Text(problem); fieldErrors[pair.Key].Visible = problem != null;
            }
            List<string> environment = new List<string>();
            if (!File.Exists(shell.Text.Trim())) environment.Add(L.Text("PowerShell 程序不存在。"));
            if (!File.Exists(python.Text.Trim())) environment.Add(L.Text("Python 程序不存在。"));
            if (environment.Count > 0) lastCheckedRevision = -1;
            environmentHelp.Text = L.Text(EnvironmentHelp) + (environment.Count == 0 ? "" : Environment.NewLine + String.Join(" ", environment)); environmentHelp.ForeColor = environment.Count == 0 ? Color.DimGray : Color.Firebrick;
            loading = true;
            try { for (int i = 0; i < draft.Profiles.Count; i++) {
                Profile p = i == selected ? current : draft.Profiles[i];
                bool missing = (i == selected ? issues : Validation.PathIssues(p)).Count > 0;
                if (missing && !inspectionErrors.ContainsKey(p.Id)) lastCheckedRevision = -1;
                string marker = missing ? L.Text("（文件缺失）") : (inspectionErrors.ContainsKey(p.Id) ? L.Text("（配置需检查）") : "");
                string text = marker.Length == 0 ? p.Name : marker.Trim() + " " + p.Name;
                if (!String.Equals(list.Items[i], text)) list.Items[i] = text;
            } } finally { loading = false; }
            string error;
            string summary = lastCheckedRevision == revision ? L.Text("检查完成：{0} 个配置通过，{1} 个需要处理。", draft.Profiles.Count - inspectionErrors.Count, inspectionErrors.Count) : "";
            checkStatus.Text = checking ? L.Text("正在检查配置…") : (current != null && inspectionErrors.TryGetValue(current.Id, out error) ? summary + Environment.NewLine + String.Join(Environment.NewLine, error.Split('\n').Select(line => L.Text(line.Trim()))) : summary);
            checkStatus.ForeColor = inspectionErrors.Count > 0 ? Color.Firebrick : Color.DimGray; checkStatus.Visible = checkStatus.Text.Length > 0;
        }
        internal async Task RecheckAsync() {
            if (checking || saving || preparing) return;
            CommitProfile(); Settings snapshot = Json.ReadText<Settings>(Json.Text(draft)); snapshot.PythonPath = python.Text.Trim(); int checkedRevision = revision;
            checking = true; UpdateControls(); RefreshIndicators();
            Dictionary<string, string> result = await Task.Run(delegate {
                Dictionary<string, string> errors = new Dictionary<string, string>();
                Dictionary<string, string> outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Profile p in snapshot.Profiles) try {
                    var missing = Validation.PathIssues(p);
                    if (missing.Count > 0) { errors[p.Id] = String.Join("\n", missing.Values); continue; }
                    Inspection inspection = Validation.Inspect(p, snapshot.PythonPath);
                    if (p.Enabled) foreach (string path in inspection.Outputs) {
                        string other;
                        if (outputs.TryGetValue(path, out other)) errors[p.Id] = errors[other] = "配置间存在重复输出文件，请为每个实例设置独立的日志、CSV 和状态文件。";
                        else outputs.Add(path, p.Id);
                    }
                }
                    catch (Exception ex) { errors[p.Id] = ex is InvalidDataException ? ex.Message : "无法安全检查配置；请确认 Python 可运行。"; }
                return errors;
            });
            if (IsDisposed) return; checking = false;
            if (checkedRevision == revision) { lastCheckedRevision = revision; inspectionErrors.Clear(); foreach (var pair in result) inspectionErrors.Add(pair.Key, pair.Value); }
            UpdateControls(); RefreshIndicators();
        }
        private void AddProfile() {
            CommitProfile(); using (FolderBrowserDialog dialog = new FolderBrowserDialog { Description = L.Text("选择原来保存 steam_monitor.conf 的文件夹（不是托盘程序源码目录）") }) {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Profile p = Files.FromDirectory(dialog.SelectedPath);
                if (!File.Exists(p.ConfigFile)) { MessageBox.Show(this, L.Text("这个文件夹中没有 steam_monitor.conf。请选择之前保存监测设置的文件夹。"), L.Text("未找到监测配置"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                p.Name = L.Text("监测 {0}", draft.Profiles.Count + 1); if (!File.Exists(p.EnvFile)) p.EnvFile = "";
                draft.Profiles.Add(p); revision++; RebuildList(); list.SelectedIndex = draft.Profiles.Count - 1;
            }
        }
        private void RemoveProfile() { if (selected < 0) return; draft.Profiles.RemoveAt(selected); revision++; RebuildList(); }
        internal async Task SaveAsync(bool restoreMonitoring) {
            if (saving || preparing || (restoreMonitoring && locked)) return;
            CommitProfile(); draft.ShellPath = shell.Text.Trim(); draft.PythonPath = python.Text.Trim(); draft.AutomaticRestart = restart.Checked;
            draft.LaunchMode = new string[] { "AlwaysWindow", "ManualWindowStartupTray", "AlwaysTray" }[mode.SelectedIndex];
            Settings snapshot = Json.ReadText<Settings>(Json.Text(draft)); saving = true; UpdateControls();
            try {
                await Task.Run(delegate { Validation.ForSave(snapshot, original); });
                if (IsDisposed) return;
                if (SaveRequested != null) SaveRequested(snapshot, restoreMonitoring);
                Result = snapshot; ResumeRequested = restoreMonitoring; DialogResult = DialogResult.OK; Close();
            } catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, Program.SafeError(ex), L.Text("检查设置"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { if (!IsDisposed) { saving = false; UpdateControls(); RefreshIndicators(); } }
        }
        protected override void Dispose(bool disposing) {
            if (disposing) { L.LanguageChanged -= ApplyLanguage; healthTimer.Dispose(); tips.Dispose(); if (helpFont != null) helpFont.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
