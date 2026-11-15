using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>A small INI file ("[Section]" lines and "key=value" lines); unknown entries are kept when it is saved again.</summary>
    internal sealed class IniFile
    {
        public const string FileName = "Setting.ini";

        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Setting.ini next to the exe.</summary>
        public static string DefaultPath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), FileName); }
        }

        public static bool Exists(string path)
        {
            return File.Exists(path);
        }

        /// <summary>Reads the file; a missing or unreadable file gives an empty one.</summary>
        public static IniFile Load(string path)
        {
            var ini = new IniFile();
            string[] lines;
            try { lines = File.ReadAllLines(path, Encoding.UTF8); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return ini; }
            string section = "";
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[line.Length - 1] == ']') { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                ini.Set(section, line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim());
            }
            return ini;
        }

        public string Get(string section, string key)
        {
            return _sections.TryGetValue(section, out var keys) && keys.TryGetValue(key, out string value) ? value : null;
        }

        public int? GetInt(string section, string key)
        {
            return int.TryParse(Get(section, key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v) ? v : (int?)null;
        }

        public void Set(string section, string key, string value)
        {
            if (!_sections.TryGetValue(section, out var keys))
                _sections[section] = keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            keys[key] = value;
        }

        public void Set(string section, string key, int value)
        {
            Set(section, key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void Remove(string section, string key)
        {
            if (_sections.TryGetValue(section, out var keys)) keys.Remove(key);
        }

        /// <exception cref="IOException">The file cannot be written.</exception>
        public void Save(string path)
        {
            var text = new StringBuilder();
            foreach (var section in _sections)
            {
                if (section.Value.Count == 0) continue;
                if (section.Key.Length > 0) text.AppendLine("[" + section.Key + "]");
                foreach (var pair in section.Value) text.AppendLine(pair.Key + "=" + pair.Value);
                text.AppendLine();
            }
            try { File.WriteAllText(path, text.ToString(), new UTF8Encoding(false)); }
            catch (UnauthorizedAccessException ex) { throw new IOException(ex.Message, ex); }
        }
    }
}
