using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace SteamMonitorTray {
    public static class L {
        public static readonly string[] Codes = { "zh-Hans", "en", "fr", "ru", "es", "ar" };
        public static readonly string[] Names = { "简体中文", "English", "Français", "Русский", "Español", "العربية" };
        private static readonly string[] CatalogLanguages = { "en", "fr", "ru", "es", "ar" };
        private static readonly Dictionary<string, string[]> Catalog = Load();
        public static string CurrentCode { get; private set; }
        public static string Selection { get; private set; }
        public static bool IsRtl { get { return CurrentCode == "ar"; } }
        public static event Action LanguageChanged;
        static L() { Selection = "auto"; CurrentCode = Resolve("auto", PreferredLanguages()); }
        private static Dictionary<string, string[]> Load() {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SteamMonitorTray.Translations.json")) {
                if (stream == null) throw new InvalidDataException("Missing embedded translations.");
                return (Dictionary<string, string[]>)new DataContractJsonSerializer(typeof(Dictionary<string, string[]>), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream);
            }
        }
        public static string Normalize(string selection) { return selection == "auto" || Codes.Contains(selection) ? selection : "auto"; }
        public static string Resolve(string selection, IEnumerable<string> preferred) {
            if (Codes.Contains(selection)) return selection;
            foreach (string value in preferred ?? new string[0]) {
                string family = (value ?? "").Replace('_', '-').Split('-')[0].ToLowerInvariant();
                string code = family == "zh" ? "zh-Hans" : family;
                if (Codes.Contains(code)) return code;
            }
            return "en";
        }
        public static void SetLanguage(string selection) {
            string normalized = Normalize(selection), next = Resolve(normalized, PreferredLanguages());
            if (CurrentCode == next && Selection == normalized) return;
            Selection = normalized; CurrentCode = next;
            Action changed = LanguageChanged; if (changed != null) changed();
        }
        public static string Text(string source, params object[] values) {
            if (source == null) return "";
            Match retry = Regex.Match(source, @"^等待重试（(\d+) 秒）$");
            if (retry.Success) return Text("等待重试（{0} 秒）", retry.Groups[1].Value);
            string template = source;
            if (CurrentCode != "zh-Hans") {
                string[] entries;
                int index = Array.IndexOf(CatalogLanguages, CurrentCode);
                if (index >= 0 && Catalog.TryGetValue(source, out entries)) template = entries[index];
            }
            if (values.Length == 0) return template;
            if (IsRtl) values = values.Select(v => (object)("\u2068" + Convert.ToString(v, CultureInfo.InvariantCulture) + "\u2069")).ToArray();
            return String.Format(CultureInfo.InvariantCulture, template, values);
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetUserPreferredUILanguages(uint flags, out uint count, [Out] char[] buffer, ref uint length);
        public static string[] PreferredLanguages() {
            try {
                uint count, length = 0;
                if (GetUserPreferredUILanguages(8, out count, null, ref length) && length > 0) {
                    char[] buffer = new char[length];
                    if (GetUserPreferredUILanguages(8, out count, buffer, ref length)) return new string(buffer).Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                }
            } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            return new string[] { CultureInfo.InstalledUICulture.Name, CultureInfo.CurrentUICulture.Name };
        }
        public static IEnumerable<string> MissingTranslations(string language) {
            int index = Array.IndexOf(CatalogLanguages, language);
            if (index < 0) return new string[0];
            return Catalog.Where(pair => pair.Value.Length != CatalogLanguages.Length || String.IsNullOrWhiteSpace(pair.Value[index])).Select(pair => pair.Key);
        }
    }
}
