using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace SteamMonitorTray {
    [DataContract] public class Profile {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Name = L.Text("默认监测");
        [DataMember] public bool AutoStart = true;
        [DataMember(Name = "Enabled", EmitDefaultValue = false)] private bool? legacyEnabled;
        [OnDeserializing] private void BeforeRead(StreamingContext context) { AutoStart = true; }
        [OnDeserialized] private void AfterRead(StreamingContext context) { if (legacyEnabled.HasValue) AutoStart = legacyEnabled.Value; legacyEnabled = null; }
        [DataMember] public string WorkingDirectory;
        [DataMember] public string ConfigFile;
        [DataMember] public string EnvFile;
        [DataMember] public string LogDirectory;
        [DataMember] public string Executable;
        public Profile Copy() { return Json.ReadText<Profile>(Json.Text(this)); }
    }
    [DataContract] public class Settings {
        [DataMember] public int Version = 2;
        public bool Migrated { get; private set; }
        [DataMember] public string LaunchMode = "ManualWindowStartupTray";
        [DataMember] public string Language = "auto";
        [OnDeserializing] private void BeforeRead(StreamingContext context) { Language = "auto"; }
        [OnDeserialized] private void AfterRead(StreamingContext context) { Language = L.Normalize(Language); if (Version == 1) { Version = 2; Migrated = true; } }
        [DataMember] public string ShellPath;
        [DataMember] public string PythonPath;
        [DataMember] public bool AutomaticRestart = true;
        [DataMember] public int[] RetrySeconds = new int[] { 5, 15, 30 };
        [DataMember] public int StableSeconds = 600;
        [DataMember] public int StopTimeoutSeconds = 30;
        [DataMember] public List<Profile> Profiles = new List<Profile>();
        public static Settings Defaults() {
            Settings s = new Settings();
            s.ShellPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            s.PythonPath = Files.FindExecutable("python.exe"); return s;
        }
        public bool ShowOnLaunch(bool startup) { return LaunchMode == "AlwaysWindow" || (LaunchMode == "ManualWindowStartupTray" && !startup); }
        public void ValidateStructure() {
            Language = L.Normalize(Language);
            if (Version != 2 || Profiles == null || RetrySeconds == null || RetrySeconds.Length != 3 || StableSeconds < 1 || StopTimeoutSeconds < 1) throw new InvalidDataException("设置格式无效。");
            if (LaunchMode != "AlwaysWindow" && LaunchMode != "AlwaysTray" && LaunchMode != "ManualWindowStartupTray") throw new InvalidDataException("启动显示策略无效。");
            foreach (int delay in RetrySeconds) if (delay < 1 || delay > 3600) throw new InvalidDataException("重试间隔必须为 1–3600 秒。");
            HashSet<string> ids = new HashSet<string>();
            foreach (Profile p in Profiles) if (p == null || String.IsNullOrWhiteSpace(p.Name) || String.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id)) throw new InvalidDataException("配置名称或标识无效。");
        }
        public void Validate() {
            ValidateStructure();
            if (!File.Exists(ShellPath)) throw new InvalidDataException("PowerShell 程序不存在。");
            if (!File.Exists(PythonPath)) throw new InvalidDataException("Python 程序不存在。");
        }
    }
    [DataContract] public class SessionSpec {
        [DataMember] public string Language;
        [DataMember] public Profile Profile;
        [DataMember] public string ShellPath;
        [DataMember] public string ControllerPipe;
        [DataMember] public string ShellPipe;
        [DataMember] public string Token;
    }
    [DataContract] public class Wire {
        [DataMember] public string Kind;
        [DataMember] public string Value;
        [DataMember] public int Code;
        [DataMember] public long Handle;
        public Wire(string kind, string value) { Kind = kind; Value = value; }
        public Wire() { }
    }
    public static class Json {
        public static string Text<T>(T value) { using (MemoryStream m = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(m, value); return Encoding.UTF8.GetString(m.ToArray()); } }
        public static T ReadText<T>(string text) { using (MemoryStream m = new MemoryStream(Encoding.UTF8.GetBytes(text))) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(m); }
        public static T Read<T>(string path) { return ReadText<T>(File.ReadAllText(path, Encoding.UTF8)); }
        public static void Save<T>(string path, T value) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp = path + ".new";
            if (value is Settings && File.Exists(path) && Read<Settings>(path).Migrated) {
                string backup = path + ".v1.bak";
                if (!File.Exists(backup)) File.Copy(path, backup, false);
            }
            File.WriteAllText(temp, Text(value), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
    }
    public static class Files {
        public static string DataRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamMonitorTray"); } }
        public static string SettingsPath { get { return Path.Combine(DataRoot, "settings.json"); } }
        public static string Bin { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        public static void EnsurePrivateDirectory(string path) {
            Directory.CreateDirectory(path);
            DirectorySecurity security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            SecurityIdentifier owner = WindowsIdentity.GetCurrent().User;
            security.SetOwner(owner);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(path, security);
        }
        public static string FindExecutable(string name) {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
                try { string candidate = Path.Combine(directory.Trim('"'), name); if (File.Exists(candidate)) return Path.GetFullPath(candidate); } catch (ArgumentException) { }
            }
            if (name == "steam_monitor.exe") {
                string pythonRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Python");
                if (Directory.Exists(pythonRoot)) foreach (string directory in Directory.GetDirectories(pythonRoot)) {
                    string candidate = Path.Combine(directory, "Scripts", name); if (File.Exists(candidate)) return candidate;
                }
            }
            return "";
        }
        public static Profile FromDirectory(string directory) {
            return new Profile { WorkingDirectory = Path.GetFullPath(directory), ConfigFile = Path.Combine(directory, "steam_monitor.conf"), EnvFile = Path.Combine(directory, ".env"), LogDirectory = directory, Executable = FindExecutable("steam_monitor.exe") };
        }
        public static void Log(string id, string kind) {
            try {
                string path = Path.Combine(DataRoot, "launcher.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.WriteAllText(path, "");
                File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + id + " " + kind + Environment.NewLine);
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public static class Pipes {
        public static NamedPipeServerStream Server(string name) {
            PipeSecurity security = new PipeSecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
        }
        public static StreamReader Reader(Stream stream) { return new StreamReader(stream, new UTF8Encoding(false), false, 4096, true); }
        public static StreamWriter Writer(Stream stream) { return new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true }; }
    }
    [DataContract] public class Inspection {
        [DataMember(Name = "valid")] public bool Valid;
        [DataMember(Name = "outputs")] public string[] Outputs;
        [DataMember(Name = "logDirectory")] public string LogDirectory;
        [DataMember(Name = "error")] public string Error;
    }
    public static class Validation {
        public static Dictionary<string, string> PathIssues(Profile p) {
            Dictionary<string, string> issues = new Dictionary<string, string>();
            if (!Directory.Exists(p.WorkingDirectory)) issues.Add("WorkingDirectory", "工作目录不存在。");
            if (!File.Exists(p.ConfigFile)) issues.Add("ConfigFile", "配置文件不存在。");
            if (!String.IsNullOrEmpty(p.EnvFile) && !File.Exists(p.EnvFile)) issues.Add("EnvFile", "密钥文件不存在。");
            if (!File.Exists(p.Executable)) issues.Add("Executable", "监测程序不存在。");
            return issues;
        }
        public static Inspection Inspect(Profile p, string python) {
            Dictionary<string, string> issues = PathIssues(p);
            if (issues.Count > 0) throw new InvalidDataException(String.Join("\n", new List<string>(issues.Values).ConvertAll(value => L.Text(value)).ToArray()));
            ProcessStartInfo info = new ProcessStartInfo(python, Native.Quote(Path.Combine(Files.Bin, "InspectConfig.py")) + " " + Native.Quote(p.ConfigFile) + " " + Native.Quote(p.WorkingDirectory));
            info.UseShellExecute = false; info.CreateNoWindow = true; info.RedirectStandardOutput = true; info.RedirectStandardError = true;
            info.StandardOutputEncoding = Encoding.UTF8; info.StandardErrorEncoding = Encoding.UTF8;
            using (Process process = Process.Start(info)) {
                string result = process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); process.WaitForExit();
                if (process.ExitCode != 0 || String.IsNullOrWhiteSpace(result)) throw new InvalidDataException("无法安全检查配置；请确认 Python 可运行。");
                Inspection inspection = Json.ReadText<Inspection>(result);
                if (!inspection.Valid) throw new InvalidDataException(inspection.Error);
                return inspection;
            }
        }
        public static void All(Settings settings) { settings.Validate(); ForSave(settings, settings); }
        private static bool SamePath(string first, string second) { return String.Equals(first ?? "", second ?? "", StringComparison.OrdinalIgnoreCase); }
        private static bool PathsChanged(Profile p, Profile old) {
            return old == null || !SamePath(p.WorkingDirectory, old.WorkingDirectory) || !SamePath(p.ConfigFile, old.ConfigFile) || !SamePath(p.EnvFile, old.EnvFile) || !SamePath(p.Executable, old.Executable);
        }
        // Preserve already broken entries when saving unrelated options.
        public static void ForSave(Settings settings, Settings original) {
            settings.ValidateStructure();
            if (!SamePath(settings.ShellPath, original.ShellPath) && !File.Exists(settings.ShellPath)) throw new InvalidDataException("PowerShell 程序不存在。");
            if (!SamePath(settings.PythonPath, original.PythonPath) && !File.Exists(settings.PythonPath)) throw new InvalidDataException("Python 程序不存在。");
            Dictionary<string, string> automatic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Profile p in settings.Profiles) {
                Profile old = original.Profiles.Find(item => item.Id == p.Id);
                bool changed = PathsChanged(p, old) || (p.AutoStart && old != null && !old.AutoStart);
                if (!File.Exists(settings.PythonPath)) { if (changed) throw new InvalidDataException("Python 程序不存在。"); continue; }
                Inspection inspection;
                try { inspection = Inspect(p, settings.PythonPath); }
                catch (InvalidDataException) { if (changed) throw; else continue; }
                p.LogDirectory = inspection.LogDirectory;
                if (p.AutoStart) foreach (string path in inspection.Outputs) {
                    string other;
                    if (automatic.TryGetValue(path, out other)) throw new InvalidDataException(L.Text("自动监测配置共用输出：{0} / {1}。请只勾选其中一个。", other, p.Name));
                    automatic[path] = p.Name;
                }
            }
        }
        public static void ForStart(Settings settings, Profile target) {
            settings.Validate(); Inspection current = Inspect(target, settings.PythonPath); target.LogDirectory = current.LogDirectory;
        }
    }
    // A lease lasts for the entire dedicated session, including its idle prompt.
    public sealed class OutputLeases {
        private readonly Dictionary<string, HashSet<string>> owned = new Dictionary<string, HashSet<string>>();
        public string Conflict(string id, IEnumerable<string> paths) {
            foreach (string other in Conflicts(id, paths)) return other;
            return null;
        }
        public IEnumerable<string> Conflicts(string id, IEnumerable<string> paths) {
            HashSet<string> candidate = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            List<string> result = new List<string>();
            foreach (var pair in owned) if (pair.Key != id && pair.Value.Overlaps(candidate)) result.Add(pair.Key);
            return result;
        }
        public bool Acquire(string id, IEnumerable<string> paths) {
            HashSet<string> candidate = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            HashSet<string> existing;
            if (owned.TryGetValue(id, out existing)) candidate.UnionWith(existing);
            if (Conflict(id, candidate) != null) return false;
            owned[id] = candidate; return true;
        }
        public void Release(string id) { owned.Remove(id); }
        public bool Contains(string id) { return owned.ContainsKey(id); }
    }
    public sealed class RetryPolicy {
        private int used;
        public int Used { get { return used; } }
        public int Next(Settings settings, TimeSpan lifetime) {
            if (lifetime.TotalSeconds >= settings.StableSeconds) used = 0;
            if (!settings.AutomaticRestart || used >= settings.RetrySeconds.Length) return -1;
            return settings.RetrySeconds[used++];
        }
        public void Reset() { used = 0; }
    }
}
