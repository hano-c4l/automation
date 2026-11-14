using System;
using System.Collections.Generic;

namespace AutomationTest.Core.Web
{
    /// <summary>A key as the browser protocol wants it.</summary>
    public sealed class KeyDefinition
    {
        public string Key = "";
        public string Code = "";
        public int VirtualKey;
        /// <summary>The character the key types (null for keys that type nothing).</summary>
        public string Text;
        /// <summary>Editing commands that Ctrl+key stands for in a text field (selectAll, copy...).</summary>
        public string Command;
    }

    /// <summary>Turns a key description such as Enter, Ctrl+A or Shift+Tab into what Input.dispatchKeyEvent needs.</summary>
    public static class WebKeys
    {
        public const int Alt = 1, Ctrl = 2, Meta = 4, Shift = 8;

        private static readonly Dictionary<string, KeyDefinition> Named = new Dictionary<string, KeyDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            { "enter", new KeyDefinition { Key = "Enter", Code = "Enter", VirtualKey = 13, Text = "\r" } },
            { "return", new KeyDefinition { Key = "Enter", Code = "Enter", VirtualKey = 13, Text = "\r" } },
            { "tab", new KeyDefinition { Key = "Tab", Code = "Tab", VirtualKey = 9 } },
            { "escape", new KeyDefinition { Key = "Escape", Code = "Escape", VirtualKey = 27 } },
            { "esc", new KeyDefinition { Key = "Escape", Code = "Escape", VirtualKey = 27 } },
            { "backspace", new KeyDefinition { Key = "Backspace", Code = "Backspace", VirtualKey = 8 } },
            { "delete", new KeyDefinition { Key = "Delete", Code = "Delete", VirtualKey = 46 } },
            { "del", new KeyDefinition { Key = "Delete", Code = "Delete", VirtualKey = 46 } },
            { "insert", new KeyDefinition { Key = "Insert", Code = "Insert", VirtualKey = 45 } },
            { "space", new KeyDefinition { Key = " ", Code = "Space", VirtualKey = 32, Text = " " } },
            { "up", new KeyDefinition { Key = "ArrowUp", Code = "ArrowUp", VirtualKey = 38 } },
            { "arrowup", new KeyDefinition { Key = "ArrowUp", Code = "ArrowUp", VirtualKey = 38 } },
            { "down", new KeyDefinition { Key = "ArrowDown", Code = "ArrowDown", VirtualKey = 40 } },
            { "arrowdown", new KeyDefinition { Key = "ArrowDown", Code = "ArrowDown", VirtualKey = 40 } },
            { "left", new KeyDefinition { Key = "ArrowLeft", Code = "ArrowLeft", VirtualKey = 37 } },
            { "arrowleft", new KeyDefinition { Key = "ArrowLeft", Code = "ArrowLeft", VirtualKey = 37 } },
            { "right", new KeyDefinition { Key = "ArrowRight", Code = "ArrowRight", VirtualKey = 39 } },
            { "arrowright", new KeyDefinition { Key = "ArrowRight", Code = "ArrowRight", VirtualKey = 39 } },
            { "home", new KeyDefinition { Key = "Home", Code = "Home", VirtualKey = 36 } },
            { "end", new KeyDefinition { Key = "End", Code = "End", VirtualKey = 35 } },
            { "pageup", new KeyDefinition { Key = "PageUp", Code = "PageUp", VirtualKey = 33 } },
            { "pagedown", new KeyDefinition { Key = "PageDown", Code = "PageDown", VirtualKey = 34 } }
        };

        private static readonly Dictionary<char, string> EditCommands = new Dictionary<char, string>
        {
            { 'a', "selectAll" }, { 'c', "copy" }, { 'x', "cut" }, { 'v', "paste" }, { 'z', "undo" }, { 'y', "redo" }
        };

        /// <summary>The modifiers (a sum of <see cref="Alt"/>, <see cref="Ctrl"/>, <see cref="Meta"/>, <see cref="Shift"/>) and the key of a description.</summary>
        /// <returns>A message when the description is not understood, otherwise null.</returns>
        public static string Parse(string spec, out int modifiers, out KeyDefinition key)
        {
            modifiers = 0;
            key = null;
            if (string.IsNullOrWhiteSpace(spec)) return "Enter the key to press, for example Enter, Tab, Ctrl+A.";
            string[] parts = spec.Trim().Split('+');
            // A plus sign as the key itself ("Ctrl++") leaves an empty last part.
            var list = new List<string>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0 && i == parts.Length - 1 && list.Count > 0) list.Add("+");
                else if (parts[i].Length > 0) list.Add(parts[i].Trim());
            }
            if (list.Count == 0) return "Enter the key to press, for example Enter, Tab, Ctrl+A.";
            for (int i = 0; i < list.Count - 1; i++)
            {
                switch (list[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= Ctrl; break;
                    case "alt": modifiers |= Alt; break;
                    case "shift": modifiers |= Shift; break;
                    case "meta": case "win": case "cmd": modifiers |= Meta; break;
                    default: return "\"" + list[i] + "\" is not a modifier: use Ctrl, Alt, Shift or Meta.";
                }
            }

            string name = list[list.Count - 1];
            if (Named.TryGetValue(name, out var named))
            {
                key = named;
                return null;
            }
            if (name.Length >= 2 && (name[0] == 'F' || name[0] == 'f') && int.TryParse(name.Substring(1), out int f) && f >= 1 && f <= 12)
            {
                key = new KeyDefinition { Key = "F" + f, Code = "F" + f, VirtualKey = 111 + f };
                return null;
            }
            if (name.Length == 1)
            {
                char c = name[0];
                bool typed = (modifiers & (Ctrl | Alt | Meta)) == 0;
                if (char.IsLetter(c) && c < 128)
                {
                    char lower = char.ToLowerInvariant(c);
                    bool upper = (modifiers & Shift) != 0 || char.IsUpper(c);
                    key = new KeyDefinition
                    {
                        Key = upper ? char.ToUpperInvariant(c).ToString() : lower.ToString(),
                        Code = "Key" + char.ToUpperInvariant(c),
                        VirtualKey = char.ToUpperInvariant(c),
                        Text = typed ? (upper ? char.ToUpperInvariant(c).ToString() : lower.ToString()) : null
                    };
                    if ((modifiers & Ctrl) != 0 && (modifiers & (Alt | Meta)) == 0 && EditCommands.TryGetValue(lower, out string command)) key.Command = command;
                    return null;
                }
                if (char.IsDigit(c))
                {
                    key = new KeyDefinition { Key = c.ToString(), Code = "Digit" + c, VirtualKey = c, Text = typed ? c.ToString() : null };
                    return null;
                }
                key = new KeyDefinition { Key = c.ToString(), Code = "", VirtualKey = 0, Text = typed ? c.ToString() : null };
                return null;
            }
            return "\"" + name + "\" is not a key I know: use Enter, Tab, Escape, Backspace, Delete, Space, Up, Down, Left, Right, Home, End, PageUp, PageDown, F1-F12, or one character.";
        }
    }
}
