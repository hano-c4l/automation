using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Automation.Core.Input;

namespace Automation.App
{
    /// <summary>Milestone 1 smoke-test form: move the cursor to a point over a duration, then click.</summary>
    public sealed class MainForm : Form
    {
        private readonly NumericUpDown _x = MakeNumber(0, 20000, 0);
        private readonly NumericUpDown _y = MakeNumber(0, 20000, 0);
        private readonly NumericUpDown _duration = MakeNumber(0, 60000, 1000);
        private readonly NumericUpDown _delay = MakeNumber(0, 60000, 3000);
        private readonly ComboBox _button = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        private readonly Button _run = new Button { Text = "Run", AutoSize = true };
        private readonly Button _cancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
        private readonly Label _status = new Label { AutoSize = true, Text = "Ready" };
        private readonly MouseController _mouse = new MouseController();
        private CancellationTokenSource _cts;

        public MainForm()
        {
            Text = "Automation - Milestone 1";
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            _button.Items.AddRange(new object[] { MouseButton.Left, MouseButton.Right });
            _button.SelectedIndex = 0;

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(12) };
            AddRow(layout, "X (px)", _x);
            AddRow(layout, "Y (px)", _y);
            AddRow(layout, "Move duration (ms)", _duration);
            AddRow(layout, "Start delay (ms)", _delay);
            AddRow(layout, "Button", _button);
            var buttons = new FlowLayoutPanel { AutoSize = true };
            buttons.Controls.Add(_run);
            buttons.Controls.Add(_cancel);
            AddRow(layout, "", buttons);
            AddRow(layout, "Status", _status);
            Controls.Add(layout);

            _run.Click += async (s, e) => await RunAsync();
            _cancel.Click += (s, e) => _cts?.Cancel();
        }

        private async Task RunAsync()
        {
            int x = (int)_x.Value, y = (int)_y.Value, duration = (int)_duration.Value, delay = (int)_delay.Value;
            var button = (MouseButton)_button.SelectedItem;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _run.Enabled = false;
            _cancel.Enabled = true;
            try
            {
                _status.Text = "Starting in " + delay + " ms...";
                await Task.Delay(delay, token);
                _status.Text = "Running...";
                await Task.Run(() =>
                {
                    _mouse.MoveTo(x, y, duration, token);
                    _mouse.Click(button);
                }, token);
                _status.Text = "Done";
            }
            catch (OperationCanceledException)
            {
                _status.Text = "Cancelled";
            }
            catch (Exception ex)
            {
                _status.Text = "Error: " + ex.Message;
            }
            finally
            {
                _run.Enabled = true;
                _cancel.Enabled = false;
            }
        }

        private static NumericUpDown MakeNumber(int min, int max, int value)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 80 };
        }

        private static void AddRow(TableLayoutPanel panel, string label, Control control)
        {
            panel.RowCount++;
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) });
            panel.Controls.Add(control);
        }
    }
}
