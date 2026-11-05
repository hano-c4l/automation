using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Automation.Core.Input;
using Automation.Core.Model;
using Automation.Core.Runner;
using Automation.Core.Windows;

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
            MultiSelect = true,
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
        private readonly ComboBox _mode = MakeCombo(typeof(CoordinateMode));
        private readonly ComboBox _anchor = MakeCombo(typeof(WindowAnchor));

        // Target window (script level)
        private readonly TextBox _targetProcess = new TextBox { Width = 160 };
        private readonly TextBox _targetTitle = new TextBox { Width = 160 };
        private readonly CheckBox _bringToFront = new CheckBox { Text = "Bring to front before running", AutoSize = true };

        // Script settings
        private readonly NumericUpDown _startDelay = MakeNumber(0, 600000, 2000);
        private readonly NumericUpDown _loops = MakeNumber(0, 1000000, 1);
        private readonly NumericUpDown _loopDelay = MakeNumber(0, 600000, 0);

        private readonly Button _startStop = new Button { Text = "Start (F6)", AutoSize = true };
        private readonly Label _status = new Label { AutoSize = true, Text = "Ready", Padding = new Padding(8, 6, 0, 0) };
        // AutoSize + Dock.Top so the area is exactly as tall as its content; the parent panel scrolls when the window is small.
        private readonly Panel _editArea = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

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
            _list.Columns.Add("Position", 90);
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
            AddRow(editorTable, "Position", _mode);
            AddRow(editorTable, "Anchor (window)", _anchor);
            AddRow(editorTable, "X (px)", _x);
            AddRow(editorTable, "Y (px)", _y);
            AddRow(editorTable, "", pickButton);
            AddRow(editorTable, "Move time (ms)", _moveMs);
            AddRow(editorTable, "Delay after (ms)", _delayMs);
            AddRow(editorTable, "Repeat step", _repeat);

            // Two rows of three equal columns so every button stays visible at any display scale.
            var stepButtons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 2 };
            for (int c = 0; c < 3; c++) stepButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            for (int r = 0; r < 2; r++) stepButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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

            var targetBox = new GroupBox { Text = "Target window (for Window positions)", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            var targetTable = NewTable();
            AddRow(targetTable, "Process name", _targetProcess);
            AddRow(targetTable, "Title (regex)", _targetTitle);
            AddRow(targetTable, "", _bringToFront);
            var targetButtons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
            targetButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            targetButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            targetButtons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddGridButton(targetButtons, "Pick window...", 0, 0, (s, e) => PickTargetWindow());
            AddGridButton(targetButtons, "Check", 1, 0, (s, e) => CheckTargetWindow());
            targetBox.Controls.Add(targetButtons);
            targetBox.Controls.Add(targetTable);

            // Docked top-to-bottom: the control added last is docked first.
            _editArea.Controls.Add(targetBox);
            _editArea.Controls.Add(settings);
            _editArea.Controls.Add(editor);

            var right = new Panel { Dock = DockStyle.Right, Width = 380, Padding = new Padding(8), AutoScroll = true };
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
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) RemoveStep();
                else if (e.Control && e.KeyCode == Keys.A)
                {
                    foreach (ListViewItem item in _list.Items) item.Selected = true;
                    e.SuppressKeyPress = true;
                }
            };
            _startStop.Click += (s, e) => ToggleRun();
            _mode.SelectedIndexChanged += (s, e) => _anchor.Enabled = (CoordinateMode)_mode.SelectedItem == CoordinateMode.Window;
            _anchor.Enabled = false;
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
                Repeat = (int)_repeat.Value,
                Mode = (CoordinateMode)_mode.SelectedItem,
                Anchor = (WindowAnchor)_anchor.SelectedItem
            };
        }

        private void LoadSelectedIntoEditor()
        {
            int index = SelectedIndex();
            if (index < 0) return; // nothing selected, or several steps selected: leave the editor as it is
            var step = _script.Steps[index];
            _action.SelectedItem = step.Action;
            _button.SelectedItem = step.Button;
            _x.Value = Clamp(_x, step.X);
            _y.Value = Clamp(_y, step.Y);
            _moveMs.Value = Clamp(_moveMs, step.MoveDurationMs);
            _delayMs.Value = Clamp(_delayMs, step.DelayAfterMs);
            _repeat.Value = Clamp(_repeat, step.Repeat);
            _mode.SelectedItem = step.Mode;
            _anchor.SelectedItem = step.Anchor;
        }

        private void AddStep()
        {
            var selected = SelectedIndices();
            int insertAt = selected.Count == 0 ? _script.Steps.Count : selected[selected.Count - 1] + 1;
            _script.Steps.Insert(insertAt, ReadEditor());
            RefreshList(new[] { insertAt });
        }

        private void UpdateStep()
        {
            int index = SelectedIndex();
            if (index < 0) { SetStatus("Select exactly one step to update."); return; }
            _script.Steps[index] = ReadEditor();
            RefreshList(new[] { index });
        }

        private void RemoveStep()
        {
            var selected = SelectedIndices();
            if (selected.Count == 0) return;
            for (int i = selected.Count - 1; i >= 0; i--) _script.Steps.RemoveAt(selected[i]);
            int next = Math.Min(selected[0], _script.Steps.Count - 1);
            RefreshList(next >= 0 ? new[] { next } : new int[0]);
            SetStatus("Removed " + selected.Count + " step(s).");
        }

        /// <summary>Moves every selected step one place up (-1) or down (1), keeping their relative order.</summary>
        private void MoveStep(int delta)
        {
            var selected = SelectedIndices();
            if (selected.Count == 0) return;
            var picked = new HashSet<int>(selected);
            var steps = _script.Steps;
            bool up = delta < 0;
            // Process in the direction of travel so a step never swaps with a neighbour that is also moving.
            var order = up ? selected : Enumerable.Reverse(selected).ToList();
            var newSelection = new List<int>();
            foreach (int i in order)
            {
                int target = i + delta;
                if (target < 0 || target >= steps.Count || picked.Contains(target))
                {
                    newSelection.Add(i); // blocked at the edge or by another selected step: stays in place
                    continue;
                }
                var step = steps[i];
                steps[i] = steps[target];
                steps[target] = step;
                picked.Remove(i);
                picked.Add(target);
                newSelection.Add(target);
            }
            RefreshList(newSelection);
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
            if (!picked.HasValue) return;

            int x = picked.Value.X, y = picked.Value.Y;
            if ((CoordinateMode)_mode.SelectedItem == CoordinateMode.Window)
            {
                // Store the point as an offset from the chosen anchor of the target window's client area.
                IntPtr hwnd;
                try { hwnd = WindowLocator.Find(ReadTarget()); }
                catch (ArgumentException ex) { SetStatus(ex.Message); return; }
                if (hwnd == IntPtr.Zero)
                {
                    SetStatus("Target window not found: set the target window first (Pick window...).");
                    return;
                }
                var area = WindowLocator.GetClientArea(hwnd);
                WindowLocator.FromScreen(area, (WindowAnchor)_anchor.SelectedItem, x, y, out x, out y);
            }
            _x.Value = Clamp(_x, x);
            _y.Value = Clamp(_y, y);
        }

        private WindowTarget ReadTarget()
        {
            return new WindowTarget
            {
                ProcessName = _targetProcess.Text.Trim(),
                TitlePattern = _targetTitle.Text.Trim(),
                BringToFront = _bringToFront.Checked
            };
        }

        /// <summary>Lets the user click any window to fill in the target process name.</summary>
        private void PickTargetWindow()
        {
            var previous = WindowState;
            WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            Thread.Sleep(250);
            Point? picked = null;
            using (var picker = new PointPickerForm())
            {
                if (picker.ShowDialog() == DialogResult.OK) picked = picker.SelectedPoint;
            }
            Thread.Sleep(150); // let the overlay disappear so WindowFromPoint sees the window underneath
            WindowInfo? info = picked.HasValue ? WindowLocator.WindowAt(picked.Value.X, picked.Value.Y) : null;
            WindowState = previous;
            Activate();
            if (!info.HasValue) return;

            if (string.Equals(info.Value.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("That is this program's own window; pick another window.");
                return;
            }
            _targetProcess.Text = info.Value.ProcessName;
            SetStatus("Target process: " + info.Value.ProcessName + "  title: \"" + info.Value.Title + "\" (use the title box to tell windows of the same program apart)");
        }

        private void CheckTargetWindow()
        {
            try
            {
                var target = ReadTarget();
                if (target.IsEmpty) { SetStatus("Enter a process name or a title pattern first."); return; }
                IntPtr hwnd = WindowLocator.Find(target);
                if (hwnd == IntPtr.Zero) { SetStatus("Target window not found."); return; }
                var area = WindowLocator.GetClientArea(hwnd);
                SetStatus("Found \"" + WindowLocator.GetTitle(hwnd) + "\", client area " + area.Width + "x" + area.Height + " at (" + area.Left + ", " + area.Top + ")");
            }
            catch (ArgumentException ex)
            {
                SetStatus(ex.Message);
            }
        }

        /// <summary>Index of the selected step, or -1 unless exactly one step is selected.</summary>
        private int SelectedIndex()
        {
            return _list.SelectedIndices.Count == 1 ? _list.SelectedIndices[0] : -1;
        }

        /// <summary>Selected step indices in ascending order.</summary>
        private List<int> SelectedIndices()
        {
            return _list.SelectedIndices.Cast<int>().OrderBy(i => i).ToList();
        }

        private void RefreshList(IEnumerable<int> select = null)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            for (int i = 0; i < _script.Steps.Count; i++)
            {
                var s = _script.Steps[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(s.Action.ToString());
                item.SubItems.Add(s.Action == StepAction.Move ? "-" : s.Button.ToString());
                item.SubItems.Add(s.Mode == CoordinateMode.Window ? "Win/" + s.Anchor : "Screen");
                item.SubItems.Add(s.X.ToString());
                item.SubItems.Add(s.Y.ToString());
                item.SubItems.Add(s.MoveDurationMs.ToString());
                item.SubItems.Add(s.DelayAfterMs.ToString());
                item.SubItems.Add(s.Repeat.ToString());
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            if (select == null) return;
            int last = -1;
            foreach (int index in select)
            {
                if (index < 0 || index >= _list.Items.Count) continue;
                _list.Items[index].Selected = true;
                last = index;
            }
            if (last >= 0) _list.EnsureVisible(last);
        }

        // ---------------------------------------------------------------- files

        private void ApplySettingsToScript()
        {
            _script.StartDelayMs = (int)_startDelay.Value;
            _script.RepeatCount = (int)_loops.Value;
            _script.RepeatDelayMs = (int)_loopDelay.Value;
            _script.Target = ReadTarget();
        }

        private void LoadSettingsFromScript()
        {
            var target = _script.Target ?? new WindowTarget();
            _targetProcess.Text = target.ProcessName ?? "";
            _targetTitle.Text = target.TitlePattern ?? "";
            _bringToFront.Checked = target.BringToFront;
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
                    RefreshList(_script.Steps.Count > 0 ? new[] { 0 } : null);
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
            // Anchor left/right (not Dock.Fill): Dock.Fill with AutoSize made the rows far taller than the buttons need.
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(2)
            };
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
