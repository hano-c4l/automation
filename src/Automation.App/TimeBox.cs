using System;
using System.Windows.Forms;

namespace Automation.App
{
    /// <summary>A number plus a unit (milliseconds, seconds, minutes, hours) that reads and writes milliseconds.</summary>
    internal sealed class TimeBox : FlowLayoutPanel
    {
        private static readonly string[] UnitNames = { "milliseconds", "seconds", "minutes", "hours" };
        private static readonly string[] UnitShort = { "ms", "s", "min", "h" };
        private static readonly long[] UnitFactors = { 1, 1000, 60000, 3600000 };

        // 94 + 6 + 100 = 200: the same width as every other input field.
        private readonly NumericUpDown _value = new NumericUpDown { Minimum = 0, Maximum = 1000000000, Width = 94, Margin = new Padding(0, 0, 6, 0) };
        private readonly ComboBox _unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(0) };

        public TimeBox(long defaultMs)
        {
            AutoSize = true;
            WrapContents = false;
            Margin = new Padding(3);
            _unit.Items.AddRange(UnitNames);
            _unit.SelectedIndex = 0;
            Controls.Add(_value);
            Controls.Add(_unit);
            Milliseconds = defaultMs;
        }

        public long Milliseconds
        {
            get { return (long)_value.Value * UnitFactors[_unit.SelectedIndex]; }
            set
            {
                value = Math.Max(0, value);
                // Largest unit that divides the value exactly, so 1500 ms stays "1500 ms" instead of a rounded 2 s.
                int index = 0;
                if (value > 0)
                {
                    for (int i = UnitFactors.Length - 1; i >= 0; i--)
                    {
                        if (value % UnitFactors[i] == 0) { index = i; break; }
                    }
                }
                _unit.SelectedIndex = index;
                _value.Value = Math.Min(_value.Maximum, value / UnitFactors[index]);
            }
        }

        /// <summary>Text such as "500 ms" or "2 s" using the largest unit that divides the value exactly.</summary>
        public static string Format(long ms)
        {
            int index = 0;
            if (ms > 0)
            {
                for (int i = UnitFactors.Length - 1; i >= 0; i--)
                {
                    if (ms % UnitFactors[i] == 0) { index = i; break; }
                }
            }
            return (ms / UnitFactors[index]) + " " + UnitShort[index];
        }
    }
}
