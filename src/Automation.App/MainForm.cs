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
        private readonly KeyboardController _keyboard = new KeyboardController();
        private readonly StopKeyHook _stopHook = new StopKeyHook();
        private bool _hotKeyFallback;
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
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        // Editor: common
        private readonly TextBox _name = new TextBox { Width = 200 };
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

        // Editor: keyboard
        private readonly ComboBox _kbAction = MakeCombo(typeof(KeyboardAction));
        private readonly KeyCaptureBox _kbKey = new KeyCaptureBox { Width = 200 };
        private readonly TextBox _kbText = new TextBox { Width = 200, Height = 90, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical };
        private readonly TimeBox _kbTime = new TimeBox(500);
        private readonly TimeBox _kbDelay = new TimeBox(500);

        // Editor: command
        private readonly ComboBox _cmdKind = MakeCombo(typeof(CommandKind));
        private readonly TextBox _cmdValue = new TextBox { Width = 200 };

        // Editor: control (loop, wait)
        private readonly ComboBox _controlKind = MakeCombo(typeof(ControlKind));
        private readonly ComboBox _loopType = MakeCombo(typeof(LoopType));
        private readonly NumericUpDown _loopCount = MakeNumber(0, 1000000000, 1);
        private readonly TimeBox _loopDuration = new TimeBox(10000);
        private readonly DateTimePicker _loopUntil = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = UntilFormat, Width = 200 };
        private readonly TextBox _waitProcessName = new TextBox { Width = 200 };
        private readonly TextBox _waitProcessTitle = new TextBox { Width = 200 };
        private readonly TimeBox _waitTime = new TimeBox(10000);
        private readonly NumericUpDown _waitRetry = MakeNumber(0, 1000, 0);
        private readonly Button _waitPick = new Button { Text = "Pick program...", AutoSize = true };

        private readonly GroupBox _mouseBox = new GroupBox { Text = "Mouse", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _keyboardBox = new GroupBox { Text = "Keyboard", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _commandBox = new GroupBox { Text = "Command", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _controlBox = new GroupBox { Text = "Control", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly Dictionary<Control, Label> _rowLabels = new Dictionary<Control, Label>();
        // AutoSize + Dock.Top so the area is exactly as tall as its content; the parent panel scrolls when the window is small.
        private readonly Panel _editArea = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        public MainForm()
        {
            Text = "Automation";
            ClientSize = new Size(1240, 580);
            MinimumSize = new Size(1000, 520);
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
            _list.Columns.Add("Name", 170);
            _list.Columns.Add("Action", 90);
            _list.Columns.Add("Process", 110);
            _list.Columns.Add("Details", 300);
            _list.Columns.Add("Time", 65);
            _list.Columns.Add("Delay", 65);

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(MakeToolButton("New", (s, e) => NewScript()));
            tools.Items.Add(MakeToolButton("Open", (s, e) => OpenScript()));
            tools.Items.Add(MakeToolButton("Save", (s, e) => SaveScript(false)));
            tools.Items.Add(MakeToolButton("Save As", (s, e) => SaveScript(true)));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(_startStop);
            var statusBar = new StatusStrip { SizingGrip = false };
            statusBar.Items.Add(_status);

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
            AddRow(common, "Name", _name);
            AddRow(common, "Category", _category);

            // Mouse
            var pickProcess = MakeButton("Pick program...", (s, e) => PickProcess(_processName));
            var pickPosition = MakeButton("Pick position...", (s, e) => PickPosition());
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

            // Keyboard
            var kbTable = NewTable();
            AddRow(kbTable, "Action", _kbAction);
            AddRow(kbTable, "Key (press it)", _kbKey);
            AddRow(kbTable, "Text", _kbText);
            AddRow(kbTable, "Time", _kbTime);
            AddRow(kbTable, "Delay", _kbDelay);
            _keyboardBox.Controls.Add(kbTable);

            // Command
            var cmdTable = NewTable();
            AddRow(cmdTable, "Type", _cmdKind);
            AddRow(cmdTable, "Path", _cmdValue);
            AddRow(cmdTable, "", MakeButton("Browse...", (s, e) => BrowseCommandPath()));
            _commandBox.Controls.Add(cmdTable);

            // Control
            var controlTable = NewTable();
            AddRow(controlTable, "Type", _controlKind);
            AddRow(controlTable, "Loop by", _loopType);
            AddRow(controlTable, "Count (0 = forever)", _loopCount);
            AddRow(controlTable, "Duration", _loopDuration);
            AddRow(controlTable, "Until", _loopUntil);
            AddRow(controlTable, "Process", _waitProcessName);
            AddRow(controlTable, "Title (regex)", _waitProcessTitle);
            AddRow(controlTable, "", _waitPick);
            AddRow(controlTable, "Wait up to", _waitTime);
            AddRow(controlTable, "Retry", _waitRetry);
            _controlBox.Controls.Add(controlTable);

            // Docked top-to-bottom: the control added last is docked first.
            _editArea.Controls.Add(_controlBox);
            _editArea.Controls.Add(_commandBox);
            _editArea.Controls.Add(_keyboardBox);
            _editArea.Controls.Add(_mouseBox);
            _editArea.Controls.Add(common);
            _editArea.Controls.Add(buttons);

            var right = new Panel { Dock = DockStyle.Right, Width = 400, Padding = new Padding(8), AutoScroll = true };
            right.Controls.Add(_editArea);

            // Fill control first, edge-docked panels afterwards.
            Controls.Add(_list);
            Controls.Add(right);
            Controls.Add(statusBar);
            Controls.Add(tools);
        }

        private void WireEvents()
        {
            _list.SelectedIndexChanged += (s, e) => LoadSelectedIntoEditor();
            _list.SizeChanged += (s, e) => FitDetailsColumn();
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
            _kbAction.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _controlKind.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _loopType.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _processName.TextChanged += (s, e) => UpdateEditorVisibility();
            _processTitle.TextChanged += (s, e) => UpdateEditorVisibility();

            // F6 is a global hot key while idle; it must not be swallowed while the user records F6 as a key.
            _waitPick.Click += (s, e) => PickProcess(_waitProcessName);
            _kbKey.Enter += (s, e) => SetToggleHotKey(false);
            _kbKey.Leave += (s, e) => SetToggleHotKey(true);
        }

        /// <summary>Shows only the fields that apply to the chosen category, action and loop type.</summary>
        private void UpdateEditorVisibility()
        {
            var category = (ActionCategory)_category.SelectedItem;
            _mouseBox.Visible = category == ActionCategory.Mouse;
            _keyboardBox.Visible = category == ActionCategory.Keyboard;
            _commandBox.Visible = category == ActionCategory.Command;
            _controlBox.Visible = category == ActionCategory.Control;

            // Mouse
            var action = (MouseAction)_mouseAction.SelectedItem;
            bool scroll = action == MouseAction.ScrollUp || action == MouseAction.ScrollDown;
            RefreshButtonChoices(scroll);
            SetRowVisible(_notches, scroll);
            // Without a process the position is absolute on the desktop, so an anchor means nothing.
            _anchor.Enabled = _processName.Text.Trim().Length > 0 || _processTitle.Text.Trim().Length > 0;

            // Keyboard
            var kb = (KeyboardAction)_kbAction.SelectedItem;
            bool isText = kb == KeyboardAction.Text;
            _kbKey.Combo = kb == KeyboardAction.ComboKey;
            SetRowVisible(_kbKey, !isText);
            SetRowVisible(_kbText, isText);
            SetRowVisible(_kbTime, kb == KeyboardAction.KeyPress || kb == KeyboardAction.ComboKey || isText);

            // Control
            bool loop = (ControlKind)_controlKind.SelectedItem == ControlKind.Loop;
            var loopType = (LoopType)_loopType.SelectedItem;
            SetRowVisible(_loopType, loop);
            SetRowVisible(_loopCount, loop && loopType == LoopType.Count);
            SetRowVisible(_loopDuration, loop && loopType == LoopType.Duration);
            SetRowVisible(_loopUntil, loop && loopType == LoopType.Until);
            SetRowVisible(_waitProcessName, !loop);
            SetRowVisible(_waitProcessTitle, !loop);
            SetRowVisible(_waitTime, !loop);
            SetRowVisible(_waitRetry, !loop);
            SetRowVisible(_waitPick, !loop);
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
            var node = BuildNode();
            string name = _name.Text.Trim();
            node.Name = name.Length == 0 ? null : name;
            return node;
        }

        private ActionNode BuildNode()
        {
            switch ((ActionCategory)_category.SelectedItem)
            {
                case ActionCategory.Mouse:
                    return ActionNode.NewMouse(new MouseSettings
                    {
                        Action = (MouseAction)_mouseAction.SelectedItem,
                        Button = (MouseButton)_button.SelectedItem,
                        TimeMs = _time.Milliseconds,
                        DelayMs = _delay.Milliseconds,
                        Process = Target(_processName, _processTitle),
                        Anchor = (WindowAnchor)_anchor.SelectedItem,
                        X = (int)_x.Value,
                        Y = (int)_y.Value,
                        ScrollNotches = (int)_notches.Value
                    });

                case ActionCategory.Keyboard:
                    var action = (KeyboardAction)_kbAction.SelectedItem;
                    return ActionNode.NewKeyboard(new KeyboardSettings
                    {
                        Action = action,
                        Value = action == KeyboardAction.Text ? _kbText.Text : _kbKey.Value,
                        TimeMs = _kbTime.Milliseconds,
                        DelayMs = _kbDelay.Milliseconds
                    });

                case ActionCategory.Command:
                    return ActionNode.NewCommand(new CommandSettings
                    {
                        Kind = (CommandKind)_cmdKind.SelectedItem,
                        Value = _cmdValue.Text.Trim()
                    });

                default:
                    if ((ControlKind)_controlKind.SelectedItem == ControlKind.Wait)
                    {
                        return ActionNode.NewWait(new WaitSettings
                        {
                            Process = Target(_waitProcessName, _waitProcessTitle),
                            TimeMs = _waitTime.Milliseconds,
                            Retry = (int)_waitRetry.Value
                        });
                    }
                    var loop = new LoopSettings
                    {
                        Mode = (LoopType)_loopType.SelectedItem,
                        Count = (long)_loopCount.Value,
                        DurationMs = _loopDuration.Milliseconds
                    };
                    loop.SetUntil(_loopUntil.Value);
                    return ActionNode.NewLoop(loop);
            }
        }

        private static WindowTarget Target(TextBox name, TextBox title)
        {
            return new WindowTarget { ProcessName = name.Text.Trim(), TitlePattern = title.Text.Trim() };
        }

        private void LoadSelectedIntoEditor()
        {
            var row = SingleSelectedRow();
            if (row == null) return; // nothing or several rows selected: leave the editor as it is
            var node = row.Node;
            _name.Text = node.Name ?? "";
            _category.SelectedItem = node.Category;
            switch (node.Category)
            {
                case ActionCategory.Mouse:
                    var m = node.Mouse;
                    _mouseAction.SelectedItem = m.Action;
                    UpdateEditorVisibility(); // so the button list matches the action before selecting the button
                    _button.SelectedItem = m.Button;
                    _time.Milliseconds = m.TimeMs;
                    _delay.Milliseconds = m.DelayMs;
                    _processName.Text = m.Process.ProcessName ?? "";
                    _processTitle.Text = m.Process.TitlePattern ?? "";
                    _anchor.SelectedItem = m.Anchor;
                    _x.Value = Clamp(_x, m.X);
                    _y.Value = Clamp(_y, m.Y);
                    _notches.Value = Clamp(_notches, m.ScrollNotches);
                    break;

                case ActionCategory.Keyboard:
                    var k = node.Keyboard;
                    _kbAction.SelectedItem = k.Action;
                    UpdateEditorVisibility();
                    if (k.Action == KeyboardAction.Text) _kbText.Text = k.Value;
                    else _kbKey.Value = k.Value;
                    _kbTime.Milliseconds = k.TimeMs;
                    _kbDelay.Milliseconds = k.DelayMs;
                    break;

                case ActionCategory.Command:
                    _cmdKind.SelectedItem = node.Command.Kind;
                    _cmdValue.Text = node.Command.Value;
                    break;

                default:
                    _controlKind.SelectedItem = node.Control;
                    if (node.Control == ControlKind.Wait)
                    {
                        var w = node.Wait;
                        _waitProcessName.Text = w.Process.ProcessName ?? "";
                        _waitProcessTitle.Text = w.Process.TitlePattern ?? "";
                        _waitTime.Milliseconds = w.TimeMs;
                        _waitRetry.Value = Clamp(_waitRetry, w.Retry);
                    }
                    else
                    {
                        _loopType.SelectedItem = node.Loop.Mode;
                        _loopCount.Value = (decimal)Math.Min(node.Loop.Count, (long)_loopCount.Maximum);
                        _loopDuration.Milliseconds = node.Loop.DurationMs;
                        var until = node.Loop.GetUntil();
                        if (until.HasValue && until.Value >= _loopUntil.MinDate && until.Value <= _loopUntil.MaxDate) _loopUntil.Value = until.Value;
                    }
                    break;
            }
        }

        // ---------------------------------------------------------------- tree editing

        /// <summary>
        /// Add: inside the selected container (Loop, Wait), otherwise right after the selected action, otherwise at the end.
        /// </summary>
        private void AddAction()
        {
            var node = ReadEditor();
            string problem = Validate(node);
            if (problem != null) { SetStatus(problem); return; }
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

        /// <summary>Returns a message if the action could never run (for example a key that is not set), otherwise null.</summary>
        private static string Validate(ActionNode node)
        {
            if (node.Category == ActionCategory.Keyboard)
            {
                var k = node.Keyboard;
                try
                {
                    if (k.Action == KeyboardAction.ComboKey) KeyCodes.ParseCombo(k.Value);
                    else if (k.Action != KeyboardAction.Text) KeyCodes.ParseKey(k.Value);
                    else if (k.Value.Length == 0) return "Type the text to send first.";
                }
                catch (ArgumentException ex)
                {
                    return ex.Message + " Click the Key box and press the key(s).";
                }
            }
            return null;
        }

        private void UpdateAction()
        {
            var row = SingleSelectedRow();
            if (row == null) { SetStatus("Select exactly one action to update."); return; }
            var edited = ReadEditor();
            string problem = Validate(edited);
            if (problem != null) { SetStatus(problem); return; }
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

        /// <summary>In: moves the selected actions to the end of the Loop or Wait right before them.</summary>
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
                SetStatus("In: the action right before the selection must be a Loop or a Wait.");
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

        /// <summary>Gives the Details column all the width the other (short) columns leave free.</summary>
        private void FitDetailsColumn()
        {
            const int Details = 3;
            int others = 0;
            for (int i = 0; i < _list.Columns.Count; i++)
                if (i != Details) others += _list.Columns[i].Width;
            int free = _list.ClientSize.Width - others;
            _list.Columns[Details].Width = Math.Max(300, free);
        }

        private static string DescribeTarget(WindowTarget target)
        {
            if (target == null || target.IsEmpty) return "";
            string name = string.IsNullOrWhiteSpace(target.ProcessName) ? "" : target.ProcessName.Trim();
            string title = string.IsNullOrWhiteSpace(target.TitlePattern) ? "" : "[" + target.TitlePattern.Trim() + "]";
            return (name + " " + title).Trim();
        }

        private static string Shorten(string text)
        {
            text = (text ?? "").Replace("\r", "").Replace("\n", " / ");
            return text.Length > 40 ? text.Substring(0, 40) + "..." : text;
        }

        private static ListViewItem Describe(Row row)
        {
            var n = row.Node;
            string indent = new string(' ', row.Depth * 4);
            string action = "", process = "", details = "", time = "", delay = "";
            switch (n.Category)
            {
                case ActionCategory.Mouse:
                    var m = n.Mouse;
                    action = m.Action.ToString();
                    process = DescribeTarget(m.Process);
                    bool hasButton = m.Action == MouseAction.Click || m.Action == MouseAction.MouseDown || m.Action == MouseAction.MouseUp;
                    details = (hasButton ? m.Button + " " : "") + "(" + m.X + ", " + m.Y + ")";
                    if (!m.Process.IsEmpty) details += " " + m.Anchor;
                    if (m.Action == MouseAction.ScrollUp || m.Action == MouseAction.ScrollDown) details += " x" + m.ScrollNotches;
                    time = TimeBox.Format(m.TimeMs);
                    delay = TimeBox.Format(m.DelayMs);
                    break;

                case ActionCategory.Keyboard:
                    var k = n.Keyboard;
                    action = k.Action.ToString();
                    details = Shorten(k.Value);
                    if (k.Action == KeyboardAction.KeyPress || k.Action == KeyboardAction.ComboKey || k.Action == KeyboardAction.Text)
                        time = TimeBox.Format(k.TimeMs);
                    delay = TimeBox.Format(k.DelayMs);
                    break;

                case ActionCategory.Command:
                    action = n.Command.Kind.ToString();
                    details = n.Command.Value;
                    break;

                default:
                    if (n.Control == ControlKind.Wait)
                    {
                        action = "Wait";
                        process = DescribeTarget(n.Wait.Process);
                        details = "up to " + TimeBox.Format(n.Wait.TimeMs) + (n.Wait.Retry > 0 ? ", retry " + n.Wait.Retry : "");
                    }
                    else
                    {
                        action = "Loop";
                        switch (n.Loop.Mode)
                        {
                            case LoopType.Duration: details = "for " + TimeBox.Format(n.Loop.DurationMs); break;
                            case LoopType.Until: details = "until " + (n.Loop.GetUntil()?.ToString(UntilFormat) ?? "?"); break;
                            default: details = n.Loop.Count == 0 ? "forever" : n.Loop.Count + " time(s)"; break;
                        }
                    }
                    break;
            }
            // Name column: the user's alias, or the action type when no alias is set; indented by tree depth.
            var item = new ListViewItem(indent + (string.IsNullOrWhiteSpace(n.Name) ? action : n.Name));
            item.SubItems.Add(action);
            item.SubItems.Add(process);
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
            var process = Target(_processName, _processTitle);
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

        /// <summary>Lets the user click any window to fill in a process name box.</summary>
        private void PickProcess(TextBox nameBox)
        {
            if (!RunOverlay(out Point p, out WindowInfo? info) || !info.HasValue) return;
            if (string.Equals(info.Value.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("That is this program's own window; pick another window.");
                return;
            }
            nameBox.Text = info.Value.ProcessName;
            SetStatus("Process: " + info.Value.ProcessName + "  title: \"" + info.Value.Title + "\" (use Title to tell windows of the same program apart)");
        }

        private void BrowseCommandPath()
        {
            using (var dialog = new OpenFileDialog { Filter = "All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) _cmdValue.Text = dialog.FileName;
            }
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

        /// <summary>Save writes to the current file when there is one; Save As (or a new script) asks for a path.</summary>
        private void SaveScript(bool saveAs)
        {
            string path = _currentPath;
            if (saveAs || path == null)
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
                    path = dialog.FileName;
                }
            }
            try
            {
                ScriptSerializer.Save(_script, path);
                _currentPath = path;
                UpdateTitle();
                SetStatus("Saved " + Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot save the file:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            var runner = new ScriptRunner(_mouse, _keyboard);
            runner.NodeStarted += node =>
            {
                var line = Describe(new Row { Node = node, Depth = 0 });
                PostStatus("Running: " + line.Text + "  " + line.SubItems[3].Text);
            };
            var notes = new List<string>();
            runner.Message += text => { lock (notes) notes.Add(text); PostStatus(text); };

            SetRunning(true);
            try
            {
                await Task.Run(() => runner.Run(snapshot, token));
                string extra;
                lock (notes) extra = notes.Count == 0 ? "" : "  " + string.Join(" ", notes);
                SetStatus("Finished." + extra);
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
                // While a script runs, F6 and Esc stop it through a keyboard hook that ignores keys the script sends
                // itself (and does not swallow them), so a script can still send F6 or Esc to another program.
                HotKeyNative.UnregisterHotKey(Handle, HotKeyToggleId);
                if (!_stopHook.Start(() => _cts?.Cancel()))
                {
                    // Fallback if the hook cannot be installed: plain global hot keys (they also swallow the keys).
                    _hotKeyFallback = true;
                    HotKeyNative.RegisterHotKey(Handle, HotKeyToggleId, HotKeyNative.MOD_NOREPEAT, (uint)Keys.F6);
                    HotKeyNative.RegisterHotKey(Handle, HotKeyEscapeId, HotKeyNative.MOD_NOREPEAT, (uint)Keys.Escape);
                }
                WindowState = FormWindowState.Minimized;
            }
            else
            {
                _stopHook.Stop();
                if (_hotKeyFallback)
                {
                    HotKeyNative.UnregisterHotKey(Handle, HotKeyEscapeId);
                    HotKeyNative.UnregisterHotKey(Handle, HotKeyToggleId);
                    _hotKeyFallback = false;
                }
                SetToggleHotKey(true);
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

        /// <summary>Registers or releases the global F6 start/stop key (it is released while the user records keys).</summary>
        private void SetToggleHotKey(bool enabled)
        {
            if (!IsHandleCreated) return;
            HotKeyNative.UnregisterHotKey(Handle, HotKeyToggleId);
            if (enabled && !_running && !HotKeyNative.RegisterHotKey(Handle, HotKeyToggleId, HotKeyNative.MOD_NOREPEAT, (uint)Keys.F6))
                SetStatus("F6 is used by another program; use the Start button.");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SetToggleHotKey(true);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _stopHook.Stop();
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
            // Explicit cells: with auto placement a hidden control is skipped and the later controls shift into the wrong columns.
            int row = table.RowCount++;
            table.Controls.Add(caption, 0, row);
            table.Controls.Add(control, 1, row);
            _rowLabels[control] = caption;
        }

        private void SetRowVisible(Control control, bool visible)
        {
            control.Visible = visible;
            if (_rowLabels.TryGetValue(control, out var label)) label.Visible = visible;
        }
    }
}
