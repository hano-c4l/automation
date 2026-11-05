using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Automation.Core.Input;
using Automation.Core.Model;
using Automation.Core.Runner;

namespace Automation.App
{
    public sealed class MainForm : Form
    {
        private const int HotKeyToggleId = 1;
        private const int HotKeyEscapeId = 2;

        private readonly MouseController _mouse = new MouseController();
        private AutomationScript _script = new AutomationScript();
        private CancellationTokenSource _cts;
        private bool _running;
        private string _currentPath;

        // Step list
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            GridLines = true
        };

        // Step editor
        private readonly ComboBox _action = MakeCombo(typeof(StepAction));
        private readonly ComboBox _button = MakeCombo(typeof(MouseButton));
        private readonly NumericUpDown _x = MakeNumber(-20000, 20000, 0);
        private readonly NumericUpDown _y = MakeNumber(-20000, 20000, 0);
        private readonly NumericUpDown _moveMs = MakeNumber(0, 600000, 500);
        private readonly NumericUpDown _delayMs = MakeNumber(0, 600000, 500);
        private readonly NumericUpDown _repeat = MakeNumber(1, 1000000, 1);

        // Script settings
        private readonly NumericUpDown _startDelay = MakeNumber(0, 600000, 2000);
        private readonly NumericUpDown _loops = MakeNumber(0, 1000000, 1);
        private readonly NumericUpDown _loopDelay = MakeNumber(0, 600000, 0);

        private readonly Button _startStop = new Button { Text = "Start (F6)", AutoSize = true };
        private readonly Label _status = new Label { AutoSize = true, Text = "Ready", Padding = new Padding(8, 6, 0, 0) };
        private readonly Panel _editArea = new Panel { Dock = DockStyle.Fill };

        public MainForm()
        {
            Text = "Automation";
            ClientSize = new Size(980, 520);
            MinimumSize = new Size(860, 480);
            BuildLayout();
            WireEvents();
            RefreshList();
            UpdateTitle();
        }

        // ---------------------------------------------------------------- layout

        private void BuildLayout()
        {
            _list.Columns.Add("#", 36);
            _list.Columns.Add("Action", 90);
            _list.Columns.Add("Button", 70);
            _list.Columns.Add("X", 60);
            _list.Columns.Add("Y", 60);
            _list.Columns.Add("Move (ms)", 80);
            _list.Columns.Add("Delay (ms)", 80);
            _list.Columns.Add("Repeat", 60);

            var pickButton = new Button { Text = "Pick on screen...", AutoSize = true };
            pickButton.Click += (s, e) => PickPoint();

            var editor = new GroupBox { Text = "Step", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            var editorTable = NewTable();
            AddRow(editorTable, "Action", _action);
            AddRow(editorTable, "Button", _button);
            AddRow(editorTable, "X (px)", _x);
            AddRow(editorTable, "Y (px)", _y);
            AddRow(editorTable, "", pickButton);
            AddRow(editorTable, "Move time (ms)", _moveMs);
            AddRow(editorTable, "Delay after (ms)", _delayMs);
            AddRow(editorTable, "Repeat step", _repeat);

            // Two rows of three equal columns so every button stays visible at any display scale.
            var stepButtons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 2 };
            for (int c = 0; c < 3; c++) stepButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            AddGridButton(stepButtons, "Add", 0, 0, (s, e) => AddStep());
            AddGridButton(stepButtons, "Update", 1, 0, (s, e) => UpdateStep());
            AddGridButton(stepButtons, "Remove", 2, 0, (s, e) => RemoveStep());
            AddGridButton(stepButtons, "Up", 0, 1, (s, e) => MoveStep(-1));
            AddGridButton(stepButtons, "Down", 1, 1, (s, e) => MoveStep(1));
            editor.Controls.Add(stepButtons);
            editor.Controls.Add(editorTable);

            var settings = new GroupBox { Text = "Script", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            var settingsTable = NewTable();
            AddRow(settingsTable, "Start delay (ms)", _startDelay);
            AddRow(settingsTable, "Loops (0 = forever)", _loops);
            AddRow(settingsTable, "Delay between loops (ms)", _loopDelay);
            settings.Controls.Add(settingsTable);

            // Docked top-to-bottom: the control added last is docked first.
            _editArea.Controls.Add(settings);
            _editArea.Controls.Add(editor);

            var right = new Panel { Dock = DockStyle.Right, Width = 360, Padding = new Padding(8) };
            right.Controls.Add(_editArea);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8) };
            bottom.Controls.Add(MakeButton("New", (s, e) => NewScript()));
            bottom.Controls.Add(MakeButton("Open...", (s, e) => OpenScript()));
            bottom.Controls.Add(MakeButton("Save...", (s, e) => SaveScript()));
            bottom.Controls.Add(_startStop);
            bottom.Controls.Add(_status);

            // Fill control first, edge-docked panels afterwards.
            Controls.Add(_list);
            Controls.Add(right);
            Controls.Add(bottom);
        }

        private void WireEvents()
        {
            _list.SelectedIndexChanged += (s, e) => LoadSelectedIntoEditor();
            _startStop.Click += (s, e) => ToggleRun();
            _action.SelectedIndexChanged += (s, e) => _button.Enabled = (StepAction)_action.SelectedItem != StepAction.Move;
        }

        // ---------------------------------------------------------------- step editing

        private Step ReadEditor()
        {
            return new Step
            {
                Action = (StepAction)_action.SelectedItem,
                Button = (MouseButton)_button.SelectedItem,
                X = (int)_x.Value,
                Y = (int)_y.Value,
                MoveDurationMs = (int)_moveMs.Value,
                DelayAfterMs = (int)_delayMs.Value,
                Repeat = (int)_repeat.Value
            };
        }

        private void LoadSelectedIntoEditor()
        {
            int index = SelectedIndex();
            if (index < 0) return;
            var step = _script.Steps[index];
            _action.SelectedItem = step.Action;
            _button.SelectedItem = step.Button;
            _x.Value = Clamp(_x, step.X);
            _y.Value = Clamp(_y, step.Y);
            _moveMs.Value = Clamp(_moveMs, step.MoveDurationMs);
            _delayMs.Value = Clamp(_delayMs, step.DelayAfterMs);
            _repeat.Value = Clamp(_repeat, step.Repeat);
        }

        private void AddStep()
        {
            int index = SelectedIndex();
            int insertAt = index < 0 ? _script.Steps.Count : index + 1;
            _script.Steps.Insert(insertAt, ReadEditor());
            RefreshList(insertAt);
        }

        private void UpdateStep()
        {
            int index = SelectedIndex();
            if (index < 0) { SetStatus("Select a step to update."); return; }
            _script.Steps[index] = ReadEditor();
            RefreshList(index);
        }

        private void RemoveStep()
        {
            int index = SelectedIndex();
            if (index < 0) return;
            _script.Steps.RemoveAt(index);
            RefreshList(Math.Min(index, _script.Steps.Count - 1));
        }

        private void MoveStep(int delta)
        {
            int index = SelectedIndex();
            int target = index + delta;
            if (index < 0 || target < 0 || target >= _script.Steps.Count) return;
            var step = _script.Steps[index];
            _script.Steps.RemoveAt(index);
            _script.Steps.Insert(target, step);
            RefreshList(target);
        }

        private void PickPoint()
        {
            var previous = WindowState;
            WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            Thread.Sleep(250); // let the minimize animation finish so the overlay is not captured behind it
            Point? picked = null;
            using (var picker = new PointPickerForm())
            {
                if (picker.ShowDialog() == DialogResult.OK) picked = picker.SelectedPoint;
            }
            WindowState = previous;
            Activate();
            if (picked.HasValue)
            {
                _x.Value = Clamp(_x, picked.Value.X);
                _y.Value = Clamp(_y, picked.Value.Y);
            }
        }

        private int SelectedIndex()
        {
            return _list.SelectedIndices.Count == 0 ? -1 : _list.SelectedIndices[0];
        }

        private void RefreshList(int select = -1)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            for (int i = 0; i < _script.Steps.Count; i++)
            {
                var s = _script.Steps[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(s.Action.ToString());
                item.SubItems.Add(s.Action == StepAction.Move ? "-" : s.Button.ToString());
                item.SubItems.Add(s.X.ToString());
                item.SubItems.Add(s.Y.ToString());
                item.SubItems.Add(s.MoveDurationMs.ToString());
                item.SubItems.Add(s.DelayAfterMs.ToString());
                item.SubItems.Add(s.Repeat.ToString());
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            if (select >= 0 && select < _list.Items.Count)
            {
                _list.Items[select].Selected = true;
                _list.EnsureVisible(select);
            }
        }

        // ---------------------------------------------------------------- files

        private void ApplySettingsToScript()
        {
            _script.StartDelayMs = (int)_startDelay.Value;
            _script.RepeatCount = (int)_loops.Value;
            _script.RepeatDelayMs = (int)_loopDelay.Value;
        }

        private void LoadSettingsFromScript()
        {
            _startDelay.Value = Clamp(_startDelay, _script.StartDelayMs);
            _loops.Value = Clamp(_loops, _script.RepeatCount);
            _loopDelay.Value = Clamp(_loopDelay, _script.RepeatDelayMs);
        }

        private void NewScript()
        {
            _script = new AutomationScript();
            _currentPath = null;
            LoadSettingsFromScript();
            RefreshList();
            UpdateTitle();
        }

        private void OpenScript()
        {
            using (var dialog = new OpenFileDialog { Filter = "Automation script (*.json)|*.json|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    _script = ScriptSerializer.Load(dialog.FileName);
                    _currentPath = dialog.FileName;
                    LoadSettingsFromScript();
                    RefreshList(_script.Steps.Count > 0 ? 0 : -1);
                    UpdateTitle();
                    SetStatus("Opened " + Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Cannot open the file:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void SaveScript()
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = "Automation script (*.json)|*.json",
                DefaultExt = "json",
                FileName = _currentPath == null ? "script.json" : Path.GetFileName(_currentPath),
                InitialDirectory = _currentPath == null ? null : Path.GetDirectoryName(_currentPath)
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ApplySettingsToScript();
                    ScriptSerializer.Save(_script, dialog.FileName);
                    _currentPath = dialog.FileName;
                    UpdateTitle();
                    SetStatus("Saved " + Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Cannot save the file:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void UpdateTitle()
        {
            Text = "Automation - " + (_currentPath == null ? "(unsaved)" : Path.GetFileName(_currentPath));
        }

        // ---------------------------------------------------------------- running

        private void ToggleRun()
        {
            if (_running) _cts.Cancel();
            else _ = StartAsync();
        }

        private async Task StartAsync()
        {
            if (_running) return;
            if (_script.Steps.Count == 0) { SetStatus("Add at least one step first."); return; }

            ApplySettingsToScript();
            var snapshot = _script.Clone();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var runner = new ScriptRunner(_mouse);
            runner.RunStarted += n => PostStatus("Loop " + n + (snapshot.RepeatCount == 0 ? "" : " / " + snapshot.RepeatCount));
            runner.StepStarted += i => PostStatus("Step " + (i + 1) + " / " + snapshot.Steps.Count);

            SetRunning(true);
            try
            {
                SetStatus("Starting in " + snapshot.StartDelayMs + " ms...");
                await Task.Run(() => runner.Run(snapshot, token));
                SetStatus("Finished");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Stopped");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message);
            }
            finally
            {
                SetRunning(false);
            }
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _editArea.Enabled = !running;
            _list.Enabled = !running;
            _startStop.Text = running ? "Stop (F6)" : "Start (F6)";
            if (running)
            {
                // Esc is only grabbed globally while a script runs, so it never steals Esc from other programs otherwise.
                HotKeyNative.RegisterHotKey(Handle, HotKeyEscapeId, HotKeyNative.MOD_NOREPEAT, (uint)Keys.Escape);
                WindowState = FormWindowState.Minimized;
            }
            else
            {
                HotKeyNative.UnregisterHotKey(Handle, HotKeyEscapeId);
                WindowState = FormWindowState.Normal;
                Activate();
            }
        }

        private void PostStatus(string text)
        {
            if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() => SetStatus(text)));
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }

        // ---------------------------------------------------------------- global hot keys

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!HotKeyNative.RegisterHotKey(Handle, HotKeyToggleId, HotKeyNative.MOD_NOREPEAT, (uint)Keys.F6))
                SetStatus("F6 is used by another program; use the Start button.");
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            HotKeyNative.UnregisterHotKey(Handle, HotKeyToggleId);
            HotKeyNative.UnregisterHotKey(Handle, HotKeyEscapeId);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == HotKeyNative.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HotKeyToggleId) ToggleRun();
                else if (id == HotKeyEscapeId && _running) _cts.Cancel();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_running) _cts.Cancel();
            base.OnFormClosing(e);
        }

        // ---------------------------------------------------------------- helpers

        private static ComboBox MakeCombo(Type enumType)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            combo.Items.AddRange(Enum.GetValues(enumType).Cast<object>().ToArray());
            combo.SelectedIndex = 0;
            return combo;
        }

        private static NumericUpDown MakeNumber(int min, int max, int value)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, Width = 120 };
        }

        private static decimal Clamp(NumericUpDown box, int value)
        {
            return Math.Max(box.Minimum, Math.Min(box.Maximum, value));
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += onClick;
            return button;
        }

        private static void AddGridButton(TableLayoutPanel grid, string text, int column, int row, EventHandler onClick)
        {
            var button = new Button { Text = text, Dock = DockStyle.Fill, AutoSize = true };
            button.Click += onClick;
            grid.Controls.Add(button, column, row);
        }

        private static TableLayoutPanel NewTable()
        {
            return new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        }

        private static void AddRow(TableLayoutPanel table, string label, Control control)
        {
            table.RowCount++;
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) });
            table.Controls.Add(control);
        }
    }
}
