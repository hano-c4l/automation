using System;
using System.Drawing;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>
    /// The settings dialog: the shortcut keys for Start, Start Selected, Record and Pick, and the two ON/OFF options. Save checks
    /// that the keys can be used together; Cancel changes nothing.
    /// </summary>
    internal sealed class SettingsForm : Form
    {
        private readonly KeyCaptureBox _start = NewKeyBox();
        private readonly KeyCaptureBox _startSelected = NewKeyBox();
        private readonly KeyCaptureBox _record = NewKeyBox();
        private readonly KeyCaptureBox _pick = NewKeyBox();
        private readonly ToggleSwitch _recordAll = new ToggleSwitch();
        private readonly ToggleSwitch _advancedPicker = new ToggleSwitch();

        /// <summary>The settings as edited (valid after the dialog returns OK).</summary>
        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings current)
        {
            Text = "Settings";
            Icon = AppIcon.Get() ?? Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

            _start.Value = current.Start;
            _startSelected.Value = current.StartSelected;
            _record.Value = current.Record;
            _pick.Value = current.Pick;
            _recordAll.Checked = current.RecordAllEvent;
            _advancedPicker.Checked = current.AdvancedPositionPicker;

            var table = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Header(table, "Shortcut keys");
            Row(table, "Start / Stop", _start);
            Row(table, "Start Selected / Stop", _startSelected);
            Row(table, "Record / Stop", _record);
            Row(table, "Position picker", _pick);
            Header(table, "Options");
            Row(table, "Record all event", _recordAll);
            Row(table, "Advance position picker", _advancedPicker);

            var save = new Button { Text = "Save", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += (s, e) => Save();
            Shown += (s, e) => ActiveControl = save; // not a key box: those take every key press, Esc included
            _advancedPicker.CheckedChanged += (s, e) => _pick.Enabled = _advancedPicker.Checked; // the Pick key is used only by the advanced picker
            _pick.Enabled = _advancedPicker.Checked;
            AcceptButton = save;
            CancelButton = cancel;
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(save);

            var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
            layout.Controls.Add(table);
            layout.Controls.Add(buttons);
            Controls.Add(layout);
        }

        private static KeyCaptureBox NewKeyBox()
        {
            return new KeyCaptureBox { Combo = false, Width = 140 };
        }

        private static void Header(TableLayoutPanel table, string text)
        {
            var label = new Label { Text = text, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };
            table.Controls.Add(label);
            table.SetColumnSpan(label, 2);
        }

        private static void Row(TableLayoutPanel table, string caption, Control field)
        {
            table.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 24, 6) });
            field.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(field);
        }

        private void Save()
        {
            var edited = new AppSettings
            {
                Start = _start.Value,
                StartSelected = _startSelected.Value,
                Record = _record.Value,
                Pick = _pick.Value,
                RecordAllEvent = _recordAll.Checked,
                AdvancedPositionPicker = _advancedPicker.Checked
            };
            string problem = edited.Validate();
            if (problem != null)
            {
                MessageBox.Show(this, problem, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = edited;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
