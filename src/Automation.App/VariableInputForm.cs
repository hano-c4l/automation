using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Automation.Core.Expressions;
using Automation.Core.Model;
using Automation.Core.Runner;

namespace Automation.App
{
    /// <summary>
    /// Asks for the values of the variables that the selected actions read but that no selected action sets, so the
    /// selection can be tried on its own. The type of each variable is taken from the Variable (or Image) action that
    /// defines it elsewhere in the script, and can be changed.
    /// </summary>
    internal sealed class VariableInputForm : Form
    {
        private const string ImageType = "Image";

        private sealed class Row
        {
            public string Name;
            public ComboBox Type;
            public TextBox Text;
            public CheckBox Bool;
            public DateTimePicker Date;
            public NumericUpDown[] Numbers; // X, Y, Width, Height
            public Label[] NumberLabels;
            public CheckBox Found;
            public FlowLayoutPanel Editor;
        }

        private readonly List<Row> _rows = new List<Row>();

        /// <summary>The values to start the run with, by variable name (valid after the dialog returns OK).</summary>
        public Dictionary<string, object> Values { get; private set; }

        public VariableInputForm(IList<VariableNeed> needs)
        {
            Text = "Variables for the selected actions";
            Icon = AppIcon.Get() ?? Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(14);

            int unit = Font.Height;
            var note = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(unit * 38, 0),
                Text = "The selected actions use these variables, but no selected action gives them a value. Enter the values to use for this run."
            };

            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Margin = new Padding(0, 10, 0, 10) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach (var need in needs) AddRow(table, need);

            var ok = new Button { Text = "Run", AutoSize = true, DialogResult = DialogResult.None };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Accept();
            AcceptButton = ok;
            CancelButton = cancel;
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
            layout.Controls.Add(note);
            layout.Controls.Add(table);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
        }

        private void AddRow(TableLayoutPanel table, VariableNeed need)
        {
            var row = new Row { Name = need.Name };
            var typeNames = Enum.GetValues(typeof(VariableType)).Cast<object>().Select(t => t.ToString()).Concat(new[] { ImageType }).ToArray();
            row.Type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, Margin = new Padding(6, 3, 6, 3) };
            row.Type.Items.AddRange(typeNames);
            string selected = need.IsImage ? ImageType : (need.Type ?? VariableType.Number).ToString();
            row.Type.SelectedItem = selected;

            row.Text = new TextBox { Width = 180 };
            row.Bool = new CheckBox { Text = "true", AutoSize = true };
            row.Date = new DateTimePicker { Width = 180, Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd HH:mm:ss" };
            row.Found = new CheckBox { Text = "found", AutoSize = true, Margin = new Padding(3, 5, 8, 3) };
            row.Numbers = new NumericUpDown[4];
            row.NumberLabels = new Label[4];
            string[] captions = { "X", "Y", "W", "H" };
            row.Editor = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Editor.Controls.Add(row.Text);
            row.Editor.Controls.Add(row.Bool);
            row.Editor.Controls.Add(row.Date);
            row.Editor.Controls.Add(row.Found);
            for (int i = 0; i < 4; i++)
            {
                row.NumberLabels[i] = new Label { Text = captions[i], AutoSize = true, Margin = new Padding(6, 6, 2, 0) };
                row.Numbers[i] = new NumericUpDown { Minimum = -1000000, Maximum = 1000000, Width = 64 };
                row.Editor.Controls.Add(row.NumberLabels[i]);
                row.Editor.Controls.Add(row.Numbers[i]);
            }

            Prefill(row, need.Default);
            row.Type.SelectedIndexChanged += (s, e) => ShowEditor(row);
            ShowEditor(row);

            table.Controls.Add(new Label { Text = "{" + need.Name + "}", AutoSize = true, Margin = new Padding(0, 7, 0, 0) });
            table.Controls.Add(row.Type);
            table.Controls.Add(row.Editor);
            _rows.Add(row);
        }

        private static void Prefill(Row row, object value)
        {
            switch (value)
            {
                case double d: row.Text.Text = Convert.ToString(d, CultureInfo.InvariantCulture); break;
                case string s: row.Text.Text = s; break;
                case bool b: row.Bool.Checked = b; break;
                case DateTime dt: row.Date.Value = dt < row.Date.MinDate ? row.Date.MinDate : dt > row.Date.MaxDate ? row.Date.MaxDate : dt; break;
                case PointValue p: row.Numbers[0].Value = Clamp(row.Numbers[0], p.X); row.Numbers[1].Value = Clamp(row.Numbers[1], p.Y); break;
                case RangeValue r:
                    row.Numbers[0].Value = Clamp(row.Numbers[0], r.X); row.Numbers[1].Value = Clamp(row.Numbers[1], r.Y);
                    row.Numbers[2].Value = Clamp(row.Numbers[2], r.Width); row.Numbers[3].Value = Clamp(row.Numbers[3], r.Height);
                    break;
            }
        }

        private static decimal Clamp(NumericUpDown box, double value)
        {
            return Math.Max(box.Minimum, Math.Min(box.Maximum, (decimal)Math.Round(value)));
        }

        private static void ShowEditor(Row row)
        {
            string type = (string)row.Type.SelectedItem;
            bool number = type == VariableType.Number.ToString(), text = type == VariableType.String.ToString();
            bool boolean = type == VariableType.Boolean.ToString();
            bool date = type == VariableType.Date.ToString(), dateTime = type == VariableType.DateTime.ToString();
            bool position = type == VariableType.Position.ToString(), range = type == VariableType.Range.ToString(), image = type == ImageType;
            row.Text.Visible = number || text;
            row.Bool.Visible = boolean;
            row.Date.Visible = date || dateTime;
            row.Date.CustomFormat = date ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss";
            row.Found.Visible = image;
            int count = position ? 2 : (range || image) ? 4 : 0;
            for (int i = 0; i < 4; i++)
            {
                row.Numbers[i].Visible = i < count;
                row.NumberLabels[i].Visible = i < count;
            }
            if (image) { row.NumberLabels[0].Text = "X"; row.NumberLabels[1].Text = "Y"; row.NumberLabels[2].Text = "W"; row.NumberLabels[3].Text = "H"; }
        }

        private void Accept()
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
            {
                string type = (string)row.Type.SelectedItem;
                object value;
                if (type == VariableType.Number.ToString())
                {
                    if (!double.TryParse(row.Text.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    {
                        Complain(row, "is not a number (use a dot for decimals, like 1.5)");
                        return;
                    }
                    value = number;
                }
                else if (type == VariableType.String.ToString()) value = row.Text.Text;
                else if (type == VariableType.Boolean.ToString()) value = row.Bool.Checked;
                else if (type == VariableType.Date.ToString()) value = row.Date.Value.Date;
                else if (type == VariableType.DateTime.ToString()) value = new DateTime(row.Date.Value.Year, row.Date.Value.Month, row.Date.Value.Day, row.Date.Value.Hour, row.Date.Value.Minute, row.Date.Value.Second);
                else if (type == VariableType.Position.ToString()) value = new PointValue((double)row.Numbers[0].Value, (double)row.Numbers[1].Value);
                else if (type == VariableType.Range.ToString()) value = new RangeValue((double)row.Numbers[0].Value, (double)row.Numbers[1].Value, (double)row.Numbers[2].Value, (double)row.Numbers[3].Value);
                else
                    value = new ImageValue(row.Found.Checked, new PointValue((double)row.Numbers[0].Value, (double)row.Numbers[1].Value), new SizeValue((double)row.Numbers[2].Value, (double)row.Numbers[3].Value));
                values[row.Name] = value;
            }
            Values = values;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Complain(Row row, string problem)
        {
            MessageBox.Show(this, "{" + row.Name + "} " + problem + ".", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            row.Text.Focus();
        }
    }
}
