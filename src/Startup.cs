using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace SteamMonitorTray {
    internal enum StartupApproval { NotRecorded, Enabled, Disabled, Unknown, Unreadable }
    internal sealed class StartupState {
        public bool Registered { get; private set; }
        public StartupApproval Approval { get; private set; }
        public StartupState(bool registered, StartupApproval approval) { Registered = registered; Approval = approval; }
        public bool HasWarning { get { return Registered && Approval != StartupApproval.Enabled && Approval != StartupApproval.NotRecorded; } }
        public string MenuText {
            get {
                if (!Registered) return L.Text("开机自启动（未注册）");
                if (Approval == StartupApproval.Disabled) return L.Text("开机自启动（任务管理器已禁用）");
                if (Approval == StartupApproval.Unknown) return L.Text("开机自启动（系统状态未知）");
                if (Approval == StartupApproval.Unreadable) return L.Text("开机自启动（状态读取失败）");
                return L.Text("开机自启动");
            }
        }
        public string Description {
            get {
                if (!Registered) return L.Text("未注册登录自启动。点击勾选后创建启动项。");
                if (Approval == StartupApproval.Disabled) return L.Text("启动项已注册，但已被任务管理器禁用。请在任务管理器 → 启动应用中启用 SteamMonitorTray；程序不会自动解除禁用。");
                if (HasWarning) return L.Text("启动项已注册，但无法确认 Windows 是否允许启动。请在任务管理器 → 启动应用中核对。");
                return L.Text("已注册登录自启动，未检测到任务管理器禁用。点击取消勾选可移除启动项。");
            }
        }
    }
    internal sealed class StartupRegistration {
        private readonly string shortcutPath, applicationPath, approvalKey, valueName;
        // Separate paths also let integration tests exercise COM/registry reads without touching real startup entries.
        public StartupRegistration(string shortcut, string application, string registryKey, string registryValue) {
            shortcutPath = shortcut; applicationPath = application; approvalKey = registryKey; valueName = registryValue;
        }
        public StartupState Read() {
            if (!File.Exists(shortcutPath)) return new StartupState(false, StartupApproval.NotRecorded);
            try {
                RegistryView view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32;
                using (RegistryKey user = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                using (RegistryKey key = user.OpenSubKey(approvalKey, false)) {
                    object value = key == null ? null : key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    return new StartupState(true, ParseApproval(value));
                }
            } catch (UnauthorizedAccessException) { return new StartupState(true, StartupApproval.Unreadable); }
              catch (SecurityException) { return new StartupState(true, StartupApproval.Unreadable); }
              catch (IOException) { return new StartupState(true, StartupApproval.Unreadable); }
        }
        internal static StartupApproval ParseApproval(object value) {
            if (value == null) return StartupApproval.NotRecorded;
            byte[] bytes = value as byte[];
            // This Windows compatibility value is not a stable public API. Do not guess unknown formats/codes.
            if (bytes == null || bytes.Length != 12) return StartupApproval.Unknown;
            uint code = BitConverter.ToUInt32(bytes, 0);
            if (code == 2 || code == 6) return StartupApproval.Enabled;
            if (code == 3 || code == 7) return StartupApproval.Disabled;
            return StartupApproval.Unknown;
        }
        public void SetRegistered(bool enabled) {
            if (!enabled) { if (File.Exists(shortcutPath)) File.Delete(shortcutPath); return; }
            // Never modify StartupApproved: a task-manager disable survives removal/re-registration.
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type), shortcut = null;
            try {
                shortcut = type.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type linkType = shortcut.GetType();
                Action<string, object> set = delegate(string name, object value) { linkType.InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, shortcut, new object[] { value }); };
                set("TargetPath", applicationPath); set("Arguments", "--startup");
                set("WorkingDirectory", Path.GetDirectoryName(applicationPath)); set("Description", "SteamMonitorTray"); set("IconLocation", applicationPath + ",0");
                linkType.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, null);
            } finally { if (shortcut != null) Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
        }
    }
    internal static class Startup {
        public static string Shortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "SteamMonitorTray.lnk"); } }
        public static StartupRegistration Registration {
            get { return new StartupRegistration(Shortcut, Path.Combine(Files.Bin, "SteamMonitorTray.exe"), @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder", "SteamMonitorTray.lnk"); }
        }
        public static void Install() { Registration.SetRegistered(true); }
        public static void Remove() { Registration.SetRegistered(false); }
    }
}
