using System;
using System.Globalization;
using System.Windows.Forms;

namespace Automation.App
{
    /// <summary>
    /// A whole-number text box with a faint placeholder shown while it is empty (an empty box counts as 0 or the minimum).
    /// Unlike NumericUpDown it can be left visibly empty, so several of them in one row can say what each one is.
    /// </summary>
    public sealed class NumberBox : HintTextBox
    {
        public NumberBox()
        {
            Width = 70;
        }

        public decimal Minimum { get; set; } = int.MinValue;
        public decimal Maximum { get; set; } = int.MaxValue;

        /// <summary>The number in the box, kept between Minimum and Maximum; an empty or invalid box gives 0 (or Minimum when 0 is not allowed).</summary>
        public decimal Value
        {
            get
            {
                return decimal.TryParse(Text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal parsed)
                    ? Clamp(parsed) : Clamp(0);
            }
            set { Text = Clamp(value).ToString(CultureInfo.InvariantCulture); }
        }

        private decimal Clamp(decimal value)
        {
            return Math.Max(Minimum, Math.Min(Maximum, value));
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            // Digits, and a minus sign at the start when negative numbers are allowed.
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && !(e.KeyChar == '-' && Minimum < 0 && SelectionStart == 0 && !Text.StartsWith("-")))
                e.Handled = true;
            base.OnKeyPress(e);
        }

        protected override void OnLeave(EventArgs e)
        {
            // Leaving the box tidies what was typed or pasted: "007" becomes 7 and out-of-range numbers are clamped.
            if (Text.Trim().Length > 0) Value = Value;
            base.OnLeave(e);
        }
    }
}
