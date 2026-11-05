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
        private const string UntilFormat = "yyyy-MM-dd HH:mm:ss";

        private readonly MouseController _mouse = new MouseController();
        private AutomationScript _script = new AutomationScript();
        private CancellationTokenSource _cts;
        private bool _running;
        private string _currentPath;

        /// <summary>One visible line of the action list: a node plus where it lives in the tree.</summary>
        private sealed class Row
        {
            public ActionNode Node;
            public ActionNode Parent;          // null for top-level actions
            public List<ActionNode> Siblings;  // the list that contains Node
            public int Depth;
        }

        private readonly List<Row> _rows = new List<Row>();

        // Left panel: action list (indented tree, multi-select)
        private readonly ListView _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            GridLines = true
        };

        // Toolbar
        private readonly ToolStripButton _startStop = new ToolStripButton("Start (F6)") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        private readonly ToolStripLabel _status = new ToolStripLabel("Ready") { Alignment = ToolStripItemAlignment.Right };

        // Editor: common
        private readonly ComboBox _category = MakeCombo(typeof(ActionCategory));

        // Editor: mouse
        private readonly ComboBox _mouseAction = MakeCombo(typeof(MouseAction));
        private readonly ComboBox _button = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        private readonly TimeBox _time = new TimeBox(500);
        private readonly TimeBox _delay = new TimeBox(500);
        private readonly TextBox _processName = new TextBox { Width = 200 };
        private readonly TextBox _processTitle = new TextBox { Width = 200 };
        private readonly ComboBox _anchor = MakeCombo(typeof(WindowAnchor));
        private readonly NumericUpDown _x = MakeNumber(-20000, 20000, 0);
        private readonly NumericUpDown _y = MakeNumber(-20000, 20000, 0);
        private readonly NumericUpDown _notches = MakeNumber(1, 1000, 1);

        // Editor: control (loop)
        private readonly ComboBox _controlKind = MakeCombo(typeof(ControlKind));
        private readonly ComboBox _loopType = MakeCombo(typeof(LoopType));
        private readonly NumericUpDown _loopCount = MakeNumber(0, 1000000000, 1);
        private readonly TimeBox _loopDuration = new TimeBox(10000);
        private readonly DateTimePicker _loopUntil = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = UntilFormat, Width = 200 };

        private readonly GroupBox _mouseBox = new GroupBox { Text = "Mouse", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _controlBox = new GroupBox { Text = "Control", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly Dictionary<Control, Label> _rowLabels = new Dictionary<Control, Label>();
        // AutoSize + Dock.Top so the area is exactly as tall as its content; the parent panel scrolls when the window is small.
        private readonly Panel _editArea = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        public MainForm()
        {
            Text = "Automation";
            ClientSize = new Size(1040, 560);
            MinimumSize = new Size(900, 520);
            _loopUntil.Value = DateTime.Now.AddHours(1);
            _category.SelectedItem = ActionCategory.Mouse;
            BuildLayout();
            WireEvents();
            UpdateEditorVisibility();
            RefreshList();
            UpdateTitle();
        }

        // ---------------------------------------------------------------- layout

        private void BuildLayout()
        {
            _list.Columns.Add("Action", 220);
            _list.Columns.Add("Details", 230);
            _list.Columns.Add("Time", 70);
            _list.Columns.Add("Delay", 70);

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(MakeToolButton("New", (s, e) => NewScript()));
            tools.Items.Add(MakeToolButton("Open...", (s, e) => OpenScript()));
            tools.Items.Add(MakeToolButton("Save...", (s, e) => SaveScript()));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(_startStop);
            tools.Items.Add(_status);

            // Action buttons: Add / Update / Delete, then In / Out / Up / Down.
            var buttons = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, RowCount = 2 };
            for (int c = 0; c < 4; c++) buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            for (int r = 0; r < 2; r++) buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddGridButton(buttons, "Add", 0, 0, (s, e) => AddAction());
            AddGridButton(buttons, "Update", 1, 0, (s, e) => UpdateAction());
            AddGridButton(buttons, "Delete", 2, 0, (s, e) => DeleteActions());
            AddGridButton(buttons, "In", 0, 1, (s, e) => MoveIn());
            AddGridButton(buttons, "Out", 1, 1, (s, e) => MoveOut());
            AddGridButton(buttons, "Up", 2, 1, (s, e) => MoveUpDown(-1));
            AddGridButton(buttons, "Down", 3, 1, (s, e) => MoveUpDown(1));

            var common = NewTable();
            AddRow(common, "Category", _category);

            var pickProcess = new Button { Text = "Pick program...", AutoSize = true };
            pickProcess.Click += (s, e) => PickProcess();
            var pickPosition = new Button { Text = "Pick position...", AutoSize = true };
            pickPosition.Click += (s, e) => PickPosition();

            var mouseTable = NewTable();
            AddRow(mouseTable, "Action", _mouseAction);
            AddRow(mouseTable, "Type", _button);
            AddRow(mouseTable, "Scroll notches", _notches);
            AddRow(mouseTable, "Time", _time);
            AddRow(mouseTable, "Delay", _delay);
            AddRow(mouseTable, "Process", _processName);
            AddRow(mouseTable, "Title (regex)", _processTitle);
            AddRow(mouseTable, "", pickProcess);
            AddRow(mouseTable, "Anchor", _anchor);
            AddRow(mouseTable, "X (px)", _x);
            AddRow(mouseTable, "Y (px)", _y);
            AddRow(mouseTable, "", pickPosition);
            _mouseBox.Controls.Add(mouseTable);

            var controlTable = NewTable();
            AddRow(controlTable, "Type", _controlKind);
            AddRow(controlTable, "Loop by", _loopType);
            AddRow(controlTable, "Count (0 = forever)", _loopCount);
            AddRow(controlTable, "Duration", _loopDuration);
            AddRow(controlTable, "Until", _loopUntil);
            _controlBox.Controls.Add(controlTable);

            // Docked top-to-bottom: the control added last is docked first.
            _editArea.Controls.Add(_controlBox);
            _editArea.Controls.Add(_mouseBox);
            _editArea.Controls.Add(common);
            _editArea.Controls.Add(buttons);

            var right = new Panel { Dock = DockStyle.Right, Width = 400, Padding = new Padding(8), AutoScroll = true };
            right.Controls.Add(_editArea);

            // Fill control first, edge-docked panels afterwards.
            Controls.Add(_list);
            Controls.Add(right);
            Controls.Add(tools);
        }

        private void WireEvents()
        {
            _list.SelectedIndexChanged += (s, e) => LoadSelectedIntoEditor();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) DeleteActions();
                else if (e.Control && e.KeyCode == Keys.A)
                {
                    foreach (ListViewItem item in _list.Items) item.Selected = true;
                    e.SuppressKeyPress = true;
                }
            };
            _startStop.Click += (s, e) => ToggleRun();
            _category.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _mouseAction.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _loopType.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _processName.TextChanged += (s, e) => UpdateEditorVisibility();
            _processTitle.TextChanged += (s, e) => UpdateEditorVisibility();
        }

        /// <summary>Shows only the fields that apply to the chosen category, action and loop type.</summary>
        private void UpdateEditorVisibility()
        {
            bool mouse = (ActionCategory)_category.SelectedItem == ActionCategory.Mouse;
            _mouseBox.Visible = mouse;
            _controlBox.Visible = !mouse;

            var action = (MouseAction)_mouseAction.SelectedItem;
            bool scroll = action == MouseAction.ScrollUp || action == MouseAction.ScrollDown;
            RefreshButtonChoices(scroll);
            SetRowVisible(_notches, scroll);

            // Without a process the position is absolute on the desktop, so an anchor means nothing.
            bool hasProcess = _processName.Text.Trim().Length > 0 || _processTitle.Text.Trim().Length > 0;
            _anchor.Enabled = hasProcess;

            var loop = (LoopType)_loopType.SelectedItem;
            SetRowVisible(_loopCount, loop == LoopType.Count);
            SetRowVisible(_loopDuration, loop == LoopType.Duration);
            SetRowVisible(_loopUntil, loop == LoopType.Until);
        }

        /// <summary>Scroll actions always use the middle button; the other actions allow all three.</summary>
        private void RefreshButtonChoices(bool scroll)
        {
            var wanted = scroll ? new[] { MouseButton.Middle } : new[] { MouseButton.Left, MouseButton.Right, MouseButton.Middle };
            var current = _button.SelectedItem is MouseButton b ? b : MouseButton.Left;
            if (_button.Items.Count == wanted.Length) return;
            _button.Items.Clear();
            foreach (var item in wanted) _button.Items.Add(item);
            _button.SelectedItem = wanted.Contains(current) ? current : wanted[0];
        }

        // ---------------------------------------------------------------- editor <-> node

        private ActionNode ReadEditor()
        {
            var node = new ActionNode { Category = (ActionCategory)_category.SelectedItem };
            if (node.Category == ActionCategory.Mouse)
            {
                node.Action = (MouseAction)_mouseAction.SelectedItem;
                node.Button = (MouseButton)_button.SelectedItem;
                node.TimeMs = _time.Milliseconds;
                node.DelayMs = _delay.Milliseconds;
                node.Process = ReadProcess();
                node.Anchor = (WindowAnchor)_anchor.SelectedItem;
                node.X = (int)_x.Value;
                node.Y = (int)_y.Value;
                node.ScrollNotches = (int)_notches.Value;
            }
            else
            {
                node.Control = (ControlKind)_controlKind.SelectedItem;
                node.LoopMode = (LoopType)_loopType.SelectedItem;
                node.LoopCount = (long)_loopCount.Value;
                node.LoopDurationMs = _loopDuration.Milliseconds;
                node.SetLoopUntil(_loopUntil.Value);
            }
            return node;
        }

        private WindowTarget ReadProcess()
        {
            return new WindowTarget { ProcessName = _processName.Text.Trim(), TitlePattern = _processTitle.Text.Trim() };
        }

        private void LoadSelectedIntoEditor()
        {
            var row = SingleSelectedRow();
            if (row == null) return; // nothing or several rows selected: leave the editor as it is
            var node = row.Node;
            _category.SelectedItem = node.Category;
            if (node.Category == ActionCategory.Mouse)
            {
                _mouseAction.SelectedItem = node.Action;
                UpdateEditorVisibility(); // so the button list matches the action before selecting the button
                _button.SelectedItem = node.Button;
                _time.Milliseconds = node.TimeMs;
                _delay.Milliseconds = node.DelayMs;
                _processName.Text = node.Process.ProcessName ?? "";
                _processTitle.Text = node.Process.TitlePattern ?? "";
                _anchor.SelectedItem = node.Anchor;
                _x.Value = Clamp(_x, node.X);
                _y.Value = Clamp(_y, node.Y);
                _notches.Value = Clamp(_notches, node.ScrollNotches);
            }
            else
            {
                _controlKind.SelectedItem = node.Control;
                _loopType.SelectedItem = node.LoopMode;
                _loopCount.Value = (decimal)Math.Min(node.LoopCount, (long)_loopCount.Maximum);
                _loopDuration.Milliseconds = node.LoopDurationMs;
                var until = node.GetLoopUntil();
                if (until.HasValue && until.Value >= _loopUntil.MinDate && until.Value <= _loopUntil.MaxDate) _loopUntil.Value = until.Value;
            }
        }

        // ---------------------------------------------------------------- tree editing

        /// <summary>
        /// Add: inside the selected container (Loop), otherwise right after the selected action, otherwise at the end.
        /// </summary>
        private void AddAction()
        {
            var node = ReadEditor();
            var selected = SelectedRows();
            if (selected.Count == 1 && selected[0].Node.IsContainer)
                selected[0].Node.Children.Add(node);
            else if (selected.Count > 0)
            {
                var last = selected[selected.Count - 1];
                last.Siblings.Insert(last.Siblings.IndexOf(last.Node) + 1, node);
            }
            else
                _script.Actions.Add(node);
            RefreshList(new[] { node });
        }

        private void UpdateAction()
        {
            var row = SingleSelectedRow();
            if (row == null) { SetStatus("Select exactly one action to update."); return; }
            var edited = ReadEditor();
            if (!edited.IsContainer && row.Node.Children.Count > 0)
            {
                SetStatus("This action contains other actions; move or delete them before changing it to a different type.");
                return;
            }
            edited.Children = row.Node.Children;
            row.Siblings[row.Siblings.IndexOf(row.Node)] = edited;
            RefreshList(new[] { edited });
        }

        private void DeleteActions()
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            int firstIndex = _list.SelectedIndices.Cast<int>().Min();
            foreach (var row in selected) row.Siblings.Remove(row.Node); // children go with their container
            int next = Math.Min(firstIndex, CountVisible() - 1);
            RefreshList(null);
            if (next >= 0 && next < _list.Items.Count) _list.Items[next].Selected = true;
            SetStatus("Deleted " + selected.Count + " action(s).");
        }

        private int CountVisible()
        {
            int Count(List<ActionNode> nodes) => nodes.Sum(n => 1 + Count(n.Children));
            return Count(_script.Actions);
        }

        /// <summary>Moves the selected actions one place up (-1) or down (1) within their list; they must share one parent.</summary>
        private void MoveUpDown(int delta)
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            if (!SameList(selected)) { SetStatus("Select actions that are in the same list to move them."); return; }

            var list = selected[0].Siblings;
            var picked = new HashSet<ActionNode>(selected.Select(r => r.Node));
            var indices = selected.Select(r => list.IndexOf(r.Node)).OrderBy(i => i).ToList();
            var order = delta < 0 ? indices : Enumerable.Reverse(indices).ToList();
            foreach (int i in order)
            {
                int target = i + delta;
                // Blocked at the edge or by a neighbour that is selected too: stays in place.
                if (target < 0 || target >= list.Count || picked.Contains(list[target])) continue;
                var node = list[i];
                list[i] = list[target];
                list[target] = node;
            }
            RefreshList(picked);
        }

        /// <summary>In: moves the selected actions to the end of the Loop right before them.</summary>
        private void MoveIn()
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            if (!SameList(selected)) { SetStatus("Select actions that are in the same list to move them."); return; }

            var list = selected[0].Siblings;
            var picked = new HashSet<ActionNode>(selected.Select(r => r.Node));
            int first = selected.Select(r => list.IndexOf(r.Node)).Min();
            ActionNode container = null;
            for (int i = first - 1; i >= 0; i--)
            {
                if (picked.Contains(list[i])) continue;
                container = list[i];
                break;
            }
            if (container == null || !container.IsContainer)
            {
                SetStatus("In: the action right before the selection must be a Loop.");
                return;
            }
            var moving = list.Where(picked.Contains).ToList();
            foreach (var node in moving) list.Remove(node);
            container.Children.AddRange(moving);
            RefreshList(picked);
        }

        /// <summary>Out: moves the selected actions out of their container, right after it.</summary>
        private void MoveOut()
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            if (!SameList(selected)) { SetStatus("Select actions that are in the same list to move them."); return; }
            var parent = selected[0].Parent;
            if (parent == null) { SetStatus("Out: the selected actions are already at the top level."); return; }

            var grandList = FindList(parent);
            var picked = new HashSet<ActionNode>(selected.Select(r => r.Node));
            var moving = selected[0].Siblings.Where(picked.Contains).ToList();
            foreach (var node in moving) selected[0].Siblings.Remove(node);
            grandList.InsertRange(grandList.IndexOf(parent) + 1, moving);
            RefreshList(picked);
        }

        private static bool SameList(List<Row> rows)
        {
            return rows.All(r => ReferenceEquals(r.Siblings, rows[0].Siblings));
        }

        /// <summary>The list that contains the given node.</summary>
        private List<ActionNode> FindList(ActionNode node)
        {
            return _rows.First(r => ReferenceEquals(r.Node, node)).Siblings;
        }

        private Row SingleSelectedRow()
        {
            return _list.SelectedIndices.Count == 1 ? _rows[_list.SelectedIndices[0]] : null;
        }

        /// <summary>Selected rows in list order.</summary>
        private List<Row> SelectedRows()
        {
            return _list.SelectedIndices.Cast<int>().OrderBy(i => i).Select(i => _rows[i]).ToList();
        }

        private void RefreshList(IEnumerable<ActionNode> select)
        {
            var wanted = select == null ? new HashSet<ActionNode>() : new HashSet<ActionNode>(select);
            _rows.Clear();
            Flatten(_script.Actions, null, 0);

            _list.BeginUpdate();
            _list.Items.Clear();
            int last = -1;
            for (int i = 0; i < _rows.Count; i++)
            {
                var item = Describe(_rows[i]);
                _list.Items.Add(item);
                if (wanted.Contains(_rows[i].Node))
                {
                    item.Selected = true;
                    last = i;
                }
            }
            _list.EndUpdate();
            if (last >= 0) _list.EnsureVisible(last);
        }

        private void RefreshList()
        {
            RefreshList(null);
        }

        private void Flatten(List<ActionNode> nodes, ActionNode parent, int depth)
        {
            foreach (var node in nodes)
            {
                _rows.Add(new Row { Node = node, Parent = parent, Siblings = nodes, Depth = depth });
                Flatten(node.Children, node, depth + 1);
            }
        }

        private static ListViewItem Describe(Row row)
        {
            var n = row.Node;
            string indent = new string(' ', row.Depth * 4);
            string name, details, time = "", delay = "";
            if (n.Category == ActionCategory.Mouse)
            {
                name = n.Action.ToString();
                string where = n.Process.IsEmpty
                    ? "Screen"
                    : (string.IsNullOrWhiteSpace(n.Process.ProcessName) ? "Title" : n.Process.ProcessName.Trim()) + "/" + n.Anchor;
                bool hasButton = n.Action == MouseAction.Click || n.Action == MouseAction.MouseDown || n.Action == MouseAction.MouseUp;
                details = (hasButton ? n.Button + " " : "") + where + " (" + n.X + ", " + n.Y + ")";
                if (n.Action == MouseAction.ScrollUp || n.Action == MouseAction.ScrollDown) details += " x" + n.ScrollNotches;
                time = TimeBox.Format(n.TimeMs);
                delay = TimeBox.Format(n.DelayMs);
            }
            else
            {
                name = "Loop";
                switch (n.LoopMode)
                {
                    case LoopType.Duration: details = "for " + TimeBox.Format(n.LoopDurationMs); break;
                    case LoopType.Until: details = "until " + (n.GetLoopUntil()?.ToString(UntilFormat) ?? "?"); break;
                    default: details = n.LoopCount == 0 ? "forever" : n.LoopCount + " time(s)"; break;
                }
            }
            var item = new ListViewItem(indent + name);
            item.SubItems.Add(details);
            item.SubItems.Add(time);
            item.SubItems.Add(delay);
            return item;
        }

        // ---------------------------------------------------------------- picking

        /// <summary>Minimizes this window, lets the user click one point on screen and returns it with the window under it.</summary>
        private bool RunOverlay(out Point point, out WindowInfo? window)
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
            Thread.Sleep(150); // let the overlay disappear so WindowFromPoint sees the window underneath
            // Resolve the clicked window now, before this form is restored on top of it.
            window = picked.HasValue ? WindowLocator.WindowAt(picked.Value.X, picked.Value.Y) : null;
            WindowState = previous;
            Activate();
            point = picked ?? Point.Empty;
            return picked.HasValue;
        }

        private void PickPosition()
        {
            var process = ReadProcess();
            if (!process.IsEmpty)
            {
                IntPtr hwnd;
                try { hwnd = WindowLocator.Find(process); }
                catch (ArgumentException ex) { SetStatus(ex.Message); return; }
                if (hwnd == IntPtr.Zero)
                {
                    SetStatus("The program is not running; start it first, or clear Process to pick a desktop position.");
                    return;
                }
                // Activate the program while this window is still in front: Windows only allows that reliably from the foreground app.
                WindowLocator.BringToFront(hwnd);
                Thread.Sleep(200);
            }

            if (!RunOverlay(out Point p, out WindowInfo? clicked)) return;

            int x = p.X, y = p.Y;
            if (!process.IsEmpty)
            {
                // Store the point as an offset from the anchor of the client area of the window that was actually
                // clicked, and refuse if that window is not the chosen program.
                bool matches;
                try { matches = clicked.HasValue && WindowLocator.Matches(clicked.Value.Handle, process); }
                catch (ArgumentException ex) { SetStatus(ex.Message); return; }
                if (!matches)
                {
                    SetStatus("That point is not inside a window matching Process / Title.");
                    return;
                }
                var area = WindowLocator.GetClientArea(clicked.Value.Handle);
                if (x < area.Left || y < area.Top || x >= area.Left + area.Width || y >= area.Top + area.Height)
                {
                    SetStatus("That point is outside the program's client area (title bar or border).");
                    return;
                }
                WindowLocator.FromScreen(area, (WindowAnchor)_anchor.SelectedItem, x, y, out x, out y);
            }
            _x.Value = Clamp(_x, x);
            _y.Value = Clamp(_y, y);
        }

        /// <summary>Lets the user click any window to fill in the process name.</summary>
        private void PickProcess()
        {
            if (!RunOverlay(out Point p, out WindowInfo? info) || !info.HasValue) return;
            if (string.Equals(info.Value.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("That is this program's own window; pick another window.");
                return;
            }
            _processName.Text = info.Value.ProcessName;
            SetStatus("Process: " + info.Value.ProcessName + "  title: \"" + info.Value.Title + "\" (use Title to tell windows of the same program apart)");
        }

        // ---------------------------------------------------------------- files

        private void NewScript()
        {
            _script = new AutomationScript();
            _currentPath = null;
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
                    RefreshList(_script.Actions.Take(1));
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
            if (_script.Actions.Count == 0) { SetStatus("Add at least one action first."); return; }

            var snapshot = _script.Clone();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var runner = new ScriptRunner(_mouse);
            runner.NodeStarted += node =>
            {
                var line = Describe(new Row { Node = node, Depth = 0 });
                PostStatus("Running: " + line.Text + "  " + line.SubItems[1].Text);
            };
            runner.Message += PostStatus;

            SetRunning(true);
            try
            {
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

        private static ToolStripButton MakeToolButton(string text, EventHandler onClick)
        {
            var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text };
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

        private void AddRow(TableLayoutPanel table, string label, Control control)
        {
            var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) };
            table.RowCount++;
            table.Controls.Add(caption);
            table.Controls.Add(control);
            _rowLabels[control] = caption;
        }

        private void SetRowVisible(Control control, bool visible)
        {
            control.Visible = visible;
            if (_rowLabels.TryGetValue(control, out var label)) label.Visible = visible;
        }
    }
}
