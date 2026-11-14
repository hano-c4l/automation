using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutomationTest.Core.Excel
{
    /// <summary>Checks and picks apart cell addresses.</summary>
    public static class ExcelAddress
    {
        private const string Cell = @"\$?[A-Za-z]{1,3}\$?[0-9]{1,7}";
        private static readonly Regex CellOrArea = new Regex("^" + Cell + "(:" + Cell + ")?$", RegexOptions.Compiled);
        private static readonly Regex ColumnOrRow = new Regex(@"^(\$?[A-Za-z]{1,3}:\$?[A-Za-z]{1,3}|\$?[0-9]{1,7}:\$?[0-9]{1,7})$", RegexOptions.Compiled);
        private static readonly Regex Name = new Regex(@"^[A-Za-z_\\][A-Za-z0-9_.]*$", RegexOptions.Compiled);
        private static readonly Regex ColumnLetters = new Regex(@"^\$?([A-Za-z]{1,3})", RegexOptions.Compiled);
        private static readonly Regex RowNumber = new Regex(@"([0-9]{1,7})$", RegexOptions.Compiled);

        /// <summary>A cell (B2, $B$2), an area (A1:C9) or a name defined in the workbook.</summary>
        public static bool IsValid(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            address = address.Trim();
            return CellOrArea.IsMatch(address) || ColumnOrRow.IsMatch(address) || (Name.IsMatch(address) && !LooksLikeCell(address));
        }

        private static bool LooksLikeCell(string text)
        {
            return Regex.IsMatch(text, @"^[A-Za-z]{1,3}[0-9]{1,7}$"); // R1C1 style and plain cells are not names
        }

        /// <summary>A message when the text cannot be an address, otherwise null.</summary>
        public static string Check(string address, string what)
        {
            if (string.IsNullOrWhiteSpace(address)) return "Enter the " + what + " (for example B2).";
            if (!IsValid(address)) return "\"" + address.Trim() + "\" is not a valid " + what + ": use B2, $B$2, A1:C9, or a name defined in the workbook.";
            return null;
        }

        /// <summary>The column letters of a cell or column reference ("B7" and "B:B" give "B"); null when there are none.</summary>
        public static string ColumnOf(string reference)
        {
            var m = ColumnLetters.Match((reference ?? "").Trim());
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        /// <summary>The row number of a cell or row reference ("B7" and "7:7" give 7); 0 when there is none.</summary>
        public static int RowOf(string reference)
        {
            string text = (reference ?? "").Trim();
            int colon = text.IndexOf(':');
            if (colon > 0) text = text.Substring(0, colon);
            var m = RowNumber.Match(text);
            return m.Success && int.TryParse(m.Groups[1].Value, out int row) ? row : 0;
        }

        /// <summary>A column number to letters: 1 = A, 26 = Z, 27 = AA.</summary>
        public static string ColumnName(int number)
        {
            string letters = "";
            while (number > 0)
            {
                letters = (char)('A' + (number - 1) % 26) + letters;
                number = (number - 1) / 26;
            }
            return letters;
        }

        /// <summary>Column letters to a number: A = 1, Z = 26, AA = 27.</summary>
        public static int ColumnNumber(string letters)
        {
            int n = 0;
            foreach (char c in letters.ToUpperInvariant()) n = n * 26 + (c - 'A' + 1);
            return n;
        }
    }

    /// <summary>Colours written as #RRGGBB.</summary>
    public static class ExcelColors
    {
        private static readonly Regex Hex = new Regex("^#?[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        public static bool IsValid(string text)
        {
            return !string.IsNullOrWhiteSpace(text) && Hex.IsMatch(text.Trim());
        }

        /// <summary>The number Excel uses for a colour: red + green x 256 + blue x 65536.</summary>
        public static int ToExcel(string text)
        {
            string hex = text.Trim().TrimStart('#');
            int r = Convert.ToInt32(hex.Substring(0, 2), 16), g = Convert.ToInt32(hex.Substring(2, 2), 16), b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return r + g * 256 + b * 65536;
        }
    }

    /// <summary>Turns what Excel returns into the types the variables use.</summary>
    public static class ExcelValues
    {
        private static readonly Dictionary<int, string> Errors = new Dictionary<int, string>
        {
            { -2146826281, "#DIV/0!" }, { -2146826246, "#N/A" }, { -2146826259, "#NAME?" }, { -2146826288, "#NULL!" },
            { -2146826252, "#NUM!" }, { -2146826265, "#REF!" }, { -2146826273, "#VALUE!" }, { -2146826240, "#GETTING_DATA" }
        };

        /// <summary>
        /// double, string, bool and DateTime stay as they are; a cell error (Excel sends it as a negative whole number) becomes its text;
        /// a currency (decimal) becomes a double; an empty cell becomes null.
        /// </summary>
        public static object Normalize(object value)
        {
            switch (value)
            {
                case null: return null;
                case int code: return Errors.TryGetValue(code, out string text) ? text : (object)(double)code;
                case decimal money: return (double)money;
                case float single: return (double)single;
                case long big: return (double)big;
                case short small: return (double)small;
                case double _:
                case string _:
                case bool _:
                case DateTime _:
                    return value;
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The variable value for a cell: an empty cell is empty text.</summary>
        public static object ForVariable(object value)
        {
            return Normalize(value) ?? "";
        }
    }

    /// <summary>Picks a sheet by name pattern and/or position.</summary>
    public static class SheetSelector
    {
        public const int RegexTimeoutMs = 1000;

        /// <returns>The position of the sheet (1 = first), or 0 when no sheet matches.</returns>
        /// <exception cref="ArgumentException">The pattern is not a valid regular expression.</exception>
        public static int Choose(IReadOnlyList<string> names, string pattern, int index)
        {
            if (index <= 0) index = 1; // empty counts as 1
            if (names.Count == 0) return 0;
            if (string.IsNullOrWhiteSpace(pattern)) return Math.Min(index, names.Count);

            var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(RegexTimeoutMs));
            var matches = new List<int>();
            for (int i = 0; i < names.Count; i++)
            {
                try { if (regex.IsMatch(names[i])) matches.Add(i + 1); }
                catch (RegexMatchTimeoutException) { }
            }
            return matches.Count == 0 ? 0 : matches[Math.Min(index, matches.Count) - 1];
        }
    }
}
