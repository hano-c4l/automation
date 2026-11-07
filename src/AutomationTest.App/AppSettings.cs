using System;
using System.Collections.Generic;
using AutomationTest.Core.Input;

namespace AutomationTest.App
{
    /// <summary>
    /// The user's settings, kept in Setting.ini: the shortcut keys for Start, Start Selected, Record and Pick, and two options.
    /// Missing or invalid entries fall back to the defaults.
    /// </summary>
    internal sealed class AppSettings
    {
        private const string HotkeySection = "Hotkeys", OptionSection = "Options";

        public const string DefaultStart = "F3", DefaultStartSelected = "F4", DefaultRecord = "F6", DefaultPick = "F8";

        /// <summary>Key names, as KeyCodes knows them ("F3").</summary>
        public string Start { get; set; } = DefaultStart;
        public string StartSelected { get; set; } = DefaultStartSelected;
        public string Record { get; set; } = DefaultRecord;
        public string Pick { get; set; } = DefaultPick;

        /// <summary>Record also plain mouse moves and every other event, not only drags.</summary>
        public bool RecordAllEvent { get; set; }

        /// <summary>With a process, Pick waits for the Pick key (or Freeze now) before it takes the picture.</summary>
        public bool AdvancedPositionPicker { get; set; }

        public int StartCode { get { return KeyCodes.ParseKey(Start); } }
        public int StartSelectedCode { get { return KeyCodes.ParseKey(StartSelected); } }
        public int RecordCode { get { return KeyCodes.ParseKey(Record); } }
        public int PickCode { get { return KeyCodes.ParseKey(Pick); } }

        public static AppSettings Defaults()
        {
            return new AppSettings();
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }

        /// <summary>Reads the settings; anything missing or not usable keeps its default.</summary>
        public static AppSettings Load(IniFile ini)
        {
            var settings = new AppSettings
            {
                Start = Key(ini, "Start", DefaultStart),
                StartSelected = Key(ini, "StartSelected", DefaultStartSelected),
                Record = Key(ini, "Record", DefaultRecord),
                Pick = Key(ini, "Pick", DefaultPick),
                RecordAllEvent = ini.Get(OptionSection, "RecordAllEvent") == "1",
                AdvancedPositionPicker = ini.Get(OptionSection, "AdvancedPositionPicker") == "1"
            };
            if (settings.Validate() != null)
            {
                // A hand-edited file with clashing keys: use the defaults for the keys rather than a half working set.
                settings.Start = DefaultStart; settings.StartSelected = DefaultStartSelected;
                settings.Record = DefaultRecord; settings.Pick = DefaultPick;
            }
            return settings;
        }

        /// <summary>True when Setting.ini has no shortcut keys yet (the first run).</summary>
        public static bool IsMissing(IniFile ini)
        {
            return ini.Get(HotkeySection, "Start") == null && ini.Get(OptionSection, "RecordAllEvent") == null;
        }

        public void Save(IniFile ini)
        {
            ini.Set(HotkeySection, "Start", Start);
            ini.Set(HotkeySection, "StartSelected", StartSelected);
            ini.Set(HotkeySection, "Record", Record);
            ini.Set(HotkeySection, "Pick", Pick);
            ini.Set(OptionSection, "RecordAllEvent", RecordAllEvent ? "1" : "0");
            ini.Set(OptionSection, "AdvancedPositionPicker", AdvancedPositionPicker ? "1" : "0");
        }

        /// <summary>Null when the keys can be used together; otherwise what is wrong, in words for the user.</summary>
        public string Validate()
        {
            var names = new List<Tuple<string, string>> { Tuple.Create("Start", Start), Tuple.Create("Start Selected", StartSelected), Tuple.Create("Record", Record) };
            if (AdvancedPositionPicker) names.Add(Tuple.Create("Position picker", Pick)); // the Pick key only matters while the advanced picker is on
            var seen = new Dictionary<int, string>();
            foreach (var item in names)
            {
                if (!KeyCodes.TryGetCode(item.Item2, out int code)) return "Choose a key for " + item.Item1 + ".";
                if (code == 0x1B) return "Esc cannot be a shortcut: it stops a run and cancels a Pick.";
                if (IsModifier(code)) return item.Item1 + ": Shift, Ctrl, Alt and Win cannot be used on their own.";
                if (seen.TryGetValue(code, out string other)) return item.Item1 + " and " + other + " use the same key (" + item.Item2 + ").";
                seen[code] = item.Item1;
            }
            return null;
        }

        private static bool IsModifier(int code)
        {
            return code == 0x10 || code == 0x11 || code == 0x12 || (code >= 0xA0 && code <= 0xA5) || code == 0x5B || code == 0x5C;
        }

        private static string Key(IniFile ini, string name, string fallback)
        {
            string text = ini.Get(HotkeySection, name);
            return KeyCodes.TryGetCode(text, out _) ? text.Trim() : fallback;
        }
    }
}
