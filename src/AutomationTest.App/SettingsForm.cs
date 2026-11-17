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
        private readonly ToggleSwitch _log = new ToggleSwitch();
        private readonly NumberBox _clearLog = new NumberBox { Minimum = AppSettings.MinClearLogDays, Maximum = AppSettings.MaxClearLogDays };
        private readonly ToggleSwitch _message = new ToggleSwitch();
        private readonly ToggleSwitch _randomTime = new ToggleSwitch();
        private readonly NumberBox _apiLimit = new NumberBox { Minimum = AppSettings.MinApiAnswerLimitMb, Maximum = AppSettings.MaxApiAnswerLimitMb };

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
            _log.Checked = current.LogRuns;
            _clearLog.Value = current.ClearLogDays;
            _message.Checked = current.NotifyEnd;
            _randomTime.Checked = current.RandomTime;
            _apiLimit.Value = current.ApiAnswerLimitMb;

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
            Row(table, "Log", _log);
            // Clear Log belongs to Log: it is only there while Log is on.
            Label clearLogCaption = Row(table, "Clear Log (days)", _clearLog);
            Action showClearLog = () => clearLogCaption.Visible = _clearLog.Visible = _log.Checked;
            _log.CheckedChanged += (s, e) => showClearLog();
            showClearLog();
            Tip(_clearLog, "While Log is ON: each time the program starts, the log files older than this many days are removed from the Logs folder.");
            Row(table, "Message", _message);
            Row(table, "Random Time", _randomTime);
            Row(table, "API answer limit (MB)", _apiLimit);
            Tip(_apiLimit, "The biggest answer an API action keeps in its variable (" + AppSettings.MinApiAnswerLimitMb + " to " + AppSettings.MaxApiAnswerLimitMb + " MB). A bigger answer is not kept: the run goes on with the status only ({r.TooBig} is true). Turn on Download in the action to save a big answer in a file (no limit).");
            Tip(_log, "ON: every run writes a log file in the Logs folder next to the program (which action started when, errors, how the run ended). Values are not written.");
            Tip(_message, "ON: a notice appears in the corner of the screen when a run finishes or fails.");
            Tip(_randomTime, "ON: while running, the Time and Delay of each action are made 10 to 20 percent longer at random (never shorter).");

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

        private readonly ToolTip _tips = new ToolTip { AutoPopDelay = 20000 };

        private void Tip(Control control, string text)
        {
            _tips.SetToolTip(control, text);
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

        private static Label Row(TableLayoutPanel table, string caption, Control field)
        {
            var label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 24, 6) };
            table.Controls.Add(label);
            field.Margin = new Padding(0, 3, 0, 3);
            table.Controls.Add(field);
            return label;
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
                AdvancedPositionPicker = _advancedPicker.Checked,
                LogRuns = _log.Checked,
                // An empty box is the default, not the smallest number: 1 day would remove almost every log at the next start.
                ClearLogDays = _clearLog.Text.Trim().Length == 0 ? AppSettings.DefaultClearLogDays : (int)_clearLog.Value,
                NotifyEnd = _message.Checked,
                RandomTime = _randomTime.Checked,
                ApiAnswerLimitMb = _apiLimit.Text.Trim().Length == 0 ? AppSettings.DefaultApiAnswerLimitMb : (int)_apiLimit.Value
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
