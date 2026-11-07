using System;
using System.Collections.Generic;
using System.Linq;

namespace AutomationTest.Core.Input
{
    /// <summary>Maps between key names stored in script files (such as "LeftCtrl", "A", "F2") and Windows virtual-key codes.</summary>
    public static class KeyCodes
    {
        private static readonly Dictionary<string, int> NameToCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> CodeToName = new Dictionary<int, string>();

        static KeyCodes()
        {
            for (char c = 'A'; c <= 'Z'; c++) Add(c.ToString(), c);
            for (char c = '0'; c <= '9'; c++) Add(c.ToString(), c);
            for (int i = 1; i <= 24; i++) Add("F" + i, 0x70 + i - 1);
            for (int i = 0; i <= 9; i++) Add("Num" + i, 0x60 + i);

            Add("Backspace", 0x08); Add("Tab", 0x09); Add("Enter", 0x0D); Add("Pause", 0x13); Add("CapsLock", 0x14);
            Add("Escape", 0x1B); Add("Space", 0x20); Add("PageUp", 0x21); Add("PageDown", 0x22); Add("End", 0x23);
            Add("Home", 0x24); Add("Left", 0x25); Add("Up", 0x26); Add("Right", 0x27); Add("Down", 0x28);
            Add("PrintScreen", 0x2C); Add("Insert", 0x2D); Add("Delete", 0x2E);
            Add("LeftWin", 0x5B); Add("RightWin", 0x5C); Add("Apps", 0x5D);
            Add("NumMultiply", 0x6A); Add("NumAdd", 0x6B); Add("NumSubtract", 0x6D); Add("NumDecimal", 0x6E); Add("NumDivide", 0x6F);
            Add("NumLock", 0x90); Add("ScrollLock", 0x91);
            Add("LeftShift", 0xA0); Add("RightShift", 0xA1); Add("LeftCtrl", 0xA2); Add("RightCtrl", 0xA3);
            Add("LeftAlt", 0xA4); Add("RightAlt", 0xA5);
            Add("Semicolon", 0xBA); Add("Equals", 0xBB); Add("Comma", 0xBC); Add("Minus", 0xBD); Add("Period", 0xBE);
            Add("Slash", 0xBF); Add("Backtick", 0xC0); Add("LeftBracket", 0xDB); Add("Backslash", 0xDC);
            Add("RightBracket", 0xDD); Add("Quote", 0xDE);
        }

        private static void Add(string name, int code)
        {
            NameToCode[name] = code;
            CodeToName[code] = name;
        }

        public static bool TryGetCode(string name, out int code)
        {
            code = 0;
            return !string.IsNullOrWhiteSpace(name) && NameToCode.TryGetValue(name.Trim(), out code);
        }

        public static bool TryGetName(int code, out string name)
        {
            return CodeToName.TryGetValue(code, out name);
        }

        /// <summary>Splits "LeftCtrl+LeftShift+S" into virtual-key codes, in order.</summary>
        /// <exception cref="ArgumentException">A name is unknown or the combination is empty.</exception>
        public static List<int> ParseCombo(string combo)
        {
            var codes = new List<int>();
            foreach (string part in (combo ?? "").Split('+'))
            {
                if (part.Trim().Length == 0) continue;
                if (!TryGetCode(part, out int code)) throw new ArgumentException("Unknown key: \"" + part.Trim() + "\".");
                codes.Add(code);
            }
            if (codes.Count == 0) throw new ArgumentException("No key is set.");
            return codes;
        }

        /// <exception cref="ArgumentException">The name is unknown or empty.</exception>
        public static int ParseKey(string name)
        {
            if (!TryGetCode(name, out int code))
                throw new ArgumentException(string.IsNullOrWhiteSpace(name) ? "No key is set." : "Unknown key: \"" + name.Trim() + "\".");
            return code;
        }

        public static string Join(IEnumerable<string> names)
        {
            return string.Join("+", names.ToArray());
        }
    }
}
