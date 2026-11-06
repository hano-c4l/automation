using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Automation.Core.Expressions;
using Automation.Core.Imaging;
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
        private const int DetailWidth = 510;
        private const int FieldWidth = 200;   // every input field
        private const int UnitWidth = 50;     // second column: unit text (px, %, Times, notches)
        private const int ButtonWidth = 68;   // third column: Pick / Check / Capture buttons, so every button starts at the same x
        private const int LabelWidth = 100;
        private const int GroupInset = 8;    // a group box insets its content by its padding and border

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
            public bool IsEnd;                 // the "End" line of a Condition (Node is the Condition itself)
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
        private readonly HintTextBox _name = new HintTextBox { Width = 200, Placeholder = "Alias Name"  };
        private readonly ComboBox _category = MakeCombo(typeof(ActionCategory));

        // Editor: mouse
        private readonly ComboBox _mouseAction = MakeCombo(typeof(MouseAction));
        private readonly ComboBox _button = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        private readonly TimeBox _time = new TimeBox(500);
        private readonly TimeBox _delay = new TimeBox(500);
        private readonly HintTextBox _processName = new HintTextBox { Width = 200, Placeholder = "Process Name" };
        private readonly HintTextBox _processTitle = new HintTextBox { Width = 200, Placeholder = "Process Title (Regex)" };
        private readonly Button _pickProcessButton = new Button { Text = "Pick", AutoSize = true };
        private readonly ComboBox _anchor = MakeCombo(typeof(WindowAnchor));
        private readonly NumberBox _x = new NumberBox { Minimum = -20000, Maximum = 20000, Placeholder = "X" };
        private readonly NumberBox _y = new NumberBox { Minimum = -20000, Maximum = 20000, Placeholder = "Y" };
        private readonly NumericUpDown _notches = MakeNumber(1, 1000, 1);

        // Editor: keyboard
        private readonly ComboBox _kbAction = MakeCombo(typeof(KeyboardAction));
        private readonly KeyCaptureBox _kbKey = new KeyCaptureBox { Width = 200 };
        private readonly HintTextBox _kbText = new HintTextBox { Width = 200, Height = 90, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical, Placeholder = "Text Content" };
        private readonly TimeBox _kbTime = new TimeBox(500);
        private readonly TimeBox _kbDelay = new TimeBox(500);

        // Editor: command
        private readonly ComboBox _cmdKind = MakeCombo(typeof(CommandKind));
        private readonly HintTextBox _cmdValue = new HintTextBox { Width = 200, Placeholder = "Process Path" };

        // Editor: control (loop, wait)
        private readonly ComboBox _controlKind = MakeCombo(typeof(ControlKind));
        private readonly ComboBox _loopType = MakeCombo(typeof(LoopType));
        private readonly NumericUpDown _loopCount = MakeNumber(0, 1000000000, 1);
        private readonly TimeBox _loopDuration = new TimeBox(10000);
        private readonly DateTimePicker _loopUntil = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = UntilFormat, Width = 200 };
        private readonly HintTextBox _waitProcessName = new HintTextBox { Width = 200, Placeholder = "Process Name" };
        private readonly HintTextBox _waitProcessTitle = new HintTextBox { Width = 200, Placeholder = "Process Title (Regex)" };
        private readonly TimeBox _waitTime = new TimeBox(10000);
        private readonly NumericUpDown _waitRetry = MakeNumber(0, 1000, 0);
        private readonly Button _waitPick = new Button { Text = "Pick", AutoSize = true };

        // Editor: image
        private readonly HintTextBox _imgProcessName = new HintTextBox { Width = 200, Placeholder = "Process Name" };
        private readonly HintTextBox _imgProcessTitle = new HintTextBox { Width = 200, Placeholder = "Process Title (Regex)" };
        private readonly ComboBox _imgAnchor = MakeCombo(typeof(WindowAnchor));
        private readonly NumberBox _imgX = new NumberBox { Minimum = -20000, Maximum = 20000, Placeholder = "X" };
        private readonly NumberBox _imgY = new NumberBox { Minimum = -20000, Maximum = 20000, Placeholder = "Y" };
        private readonly NumberBox _imgW = new NumberBox { Minimum = 0, Maximum = 20000, Placeholder = "Width" };
        private readonly NumberBox _imgH = new NumberBox { Minimum = 0, Maximum = 20000, Placeholder = "Height" };
        private readonly NumericUpDown _imgAccuracy = MakeNumber(1, 100, 90);
        private readonly PictureBox _imgPreview = new PictureBox { Width = 200, Height = 90, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        private readonly Label _imgInfo = new Label { AutoSize = true, Text = "No picture" };
        private readonly Button _imgPickProcess = new Button { Text = "Pick", AutoSize = true };
        private readonly Button _imgPickRange = new Button { Text = "Pick", AutoSize = true };
        private readonly Button _imgCapture = new Button { Text = "Capture", AutoSize = true };
        private readonly Button _imgLoad = new Button { Text = "Pick", AutoSize = true };
        private readonly Button _imgFind = new Button { Text = "Check", AutoSize = true };
        private readonly TextBox _imgId = new TextBox { Width = 200 };
        private RawImage _imageRaw;

        // Editor: variable
        private readonly TextBox _varId = new TextBox { Width = 200 };
        private readonly ComboBox _varType = MakeCombo(typeof(VariableType));
        private readonly CheckBox _varUseExpr = new CheckBox { Text = "Use an expression", AutoSize = true };
        private readonly TextBox _varExpr = new TextBox { Width = 200 };
        private readonly TextBox _varText = new TextBox { Width = 200 };
        private readonly CheckBox _varBool = new CheckBox { Text = "true", AutoSize = true };
        private readonly DateTimePicker _varDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Width = 200 };
        private readonly DateTimePicker _varDateTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = UntilFormat, Width = 200 };
        private readonly NumberBox _varX = new NumberBox { Minimum = -20000, Maximum = 20000, Placeholder = "X" };
        private readonly NumberBox _varY = new NumberBox { Minimum = -20000, Maximum = 20000, Placeholder = "Y" };
        private readonly NumberBox _varW = new NumberBox { Minimum = 0, Maximum = 20000, Placeholder = "Width" };
        private readonly NumberBox _varH = new NumberBox { Minimum = 0, Maximum = 20000, Placeholder = "Height" };
        private readonly Button _varPickPos = new Button { Text = "Pick", AutoSize = true };
        private readonly Button _varPickRange = new Button { Text = "Pick", AutoSize = true };

        // Editor: condition / case
        private readonly TextBox _caseExpr = new TextBox { Width = 200 };

        // Editor: where positions and text come from
        private readonly ComboBox _posSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Items = { "Input", "Variable" } };
        private readonly HintTextBox _posExpr = new HintTextBox { Width = 200, Placeholder = "Position Expression"  };
        private readonly Button _pickPositionButton = new Button { Text = "Pick", AutoSize = true };
        private readonly ComboBox _kbSource = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, Items = { "Input", "Variable" } };
        private readonly HintTextBox _kbTextExpr = new HintTextBox { Width = 200, Placeholder = "Text Expression" };
        private readonly ToolStripButton _saveAs = new ToolStripButton("Save As") { DisplayStyle = ToolStripItemDisplayStyle.Text };

        private readonly GroupBox _mouseBox = new GroupBox { Text = "Mouse", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _keyboardBox = new GroupBox { Text = "Keyboard", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _commandBox = new GroupBox { Text = "Command", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _controlBox = new GroupBox { Text = "Control", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private ToolStripButton _btnAdd, _btnUpdate, _btnDelete, _btnIn, _btnOut, _btnUp, _btnDown;
        private readonly Dictionary<Control, Label> _rowLabels = new Dictionary<Control, Label>();
        private readonly Dictionary<Control, Control> _rowHosts = new Dictionary<Control, Control>();
        private readonly ToolTip _tips = new ToolTip();
        private string _savedSnapshot = "";

        /// <summary>
        /// Asked before unsaved changes would be lost (New, Open, closing). Receives the message and returns true to go on.
        /// Null shows a Yes/No dialog; tests replace it.
        /// </summary>
        public Func<string, bool> ConfirmDiscard { get; set; }
        // AutoSize + Dock.Top so the area is exactly as tall as its content; the parent panel scrolls when the window is small.
        private readonly Panel _editArea = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        public MainForm()
        {
            Text = "Automation";
            ClientSize = new Size(1380, 600);
            MinimumSize = new Size(1150, 540);
            _loopUntil.Value = DateTime.Now.AddHours(1);
            _varDateTime.Value = DateTime.Now;
            _controlKind.Items.Remove(ControlKind.Else); // the else line is created with its Condition, never added by hand
            _controlKind.Items.Remove(ControlKind.Case); // Case is offered only while a line of a Condition is selected
            _posSource.SelectedIndex = 0;
            _kbSource.SelectedIndex = 0;
            _category.SelectedItem = ActionCategory.Mouse;
            BuildLayout();
            WireEvents();
            UpdateEditorVisibility();
            RefreshList();
            UpdateTitle();
            MarkSaved();
            UpdateButtonStates();
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

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Fill, RenderMode = ToolStripRenderMode.System };
            tools.Items.Add(MakeToolButton("New", (s, e) => NewScript()));
            tools.Items.Add(MakeToolButton("Open", (s, e) => OpenScript()));
            tools.Items.Add(MakeToolButton("Save", (s, e) => SaveScript(false)));
            _saveAs.Click += (s, e) => SaveScript(true);
            tools.Items.Add(_saveAs);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(_startStop);
            var statusBar = new StatusStrip { SizingGrip = false };
            statusBar.Items.Add(_status);

            // Action buttons as icons (hover for the name). They sit in the same row as the menu, over the detail panel
            // and left-aligned with it: a second strip whose width and left padding match that panel.
            var iconBar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Dock = DockStyle.Fill,
                CanOverflow = false,
                RenderMode = ToolStripRenderMode.System,
                Padding = new Padding(8, 0, 0, 0) // the detail panel's own left padding
            };
            _btnAdd = MakeToolIcon("\uE710", "+", "Add", (s, e) => AddAction());
            _btnUpdate = MakeToolIcon("\uE70F", "\u270E", "Update the selected action (select exactly one)", (s, e) => UpdateAction());
            _btnDelete = MakeToolIcon("\uE74D", "\u2715", "Delete the selected actions", (s, e) => DeleteActions());
            _btnIn = MakeToolIcon("\uE72A", "\u2192", "In: move into the Loop, Wait or Image above", (s, e) => MoveIn());
            _btnOut = MakeToolIcon("\uE72B", "\u2190", "Out: move out of the container", (s, e) => MoveOut());
            _btnUp = MakeToolIcon("\uE74A", "\u2191", "Up", (s, e) => MoveUpDown(-1));
            _btnDown = MakeToolIcon("\uE74B", "\u2193", "Down", (s, e) => MoveUpDown(1));
            foreach (var button in new[] { _btnAdd, _btnUpdate, _btnDelete, _btnIn, _btnOut, _btnUp, _btnDown })
                iconBar.Items.Add(button);

            var topBar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DetailWidth));
            topBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            topBar.Controls.Add(tools, 0, 0);
            topBar.Controls.Add(iconBar, 1, 0);

            // Name, Category and Action sit outside the group boxes; only the Action of the chosen category is shown.
            // Their fields start at the same x as the fields inside the group boxes (a group box insets its content).
            var common = NewTable();
            common.Padding = new Padding(GroupInset, 0, 0, 0);
            AddRow(common, "Name", _name);
            AddRow(common, "Category", _category);
            AddRow(common, "Action", _mouseAction);
            AddRow(common, "Action", _kbAction);
            AddRow(common, "Action", _cmdKind);
            AddRow(common, "Action", _controlKind);

            // Every field is FieldWidth wide; a unit text and a Pick button go in a second fixed column and the "?" in a
            // third, so the right edges and the "?" icons line up from row to row.
            // Mouse
            _pickProcessButton.Click += (s, e) => PickProcess(_processName);
            _pickPositionButton.Click += (s, e) => PickPosition();
            var mouseTable = NewTable();
            AddRow(mouseTable, "Type", _button);
            AddRow(mouseTable, "Scroll", _notches, null, "notches");
            AddRow(mouseTable, "Time", _time, Help.MouseTime);
            AddRow(mouseTable, "Delay", _delay, Help.MouseDelay);
            AddRow(mouseTable, "Process", _processName, null, null, _pickProcessButton);
            AddRow(mouseTable, "", _processTitle, Help.TitleRegex);
            AddRow(mouseTable, "Anchor", _anchor, Help.Anchor);
            AddRow(mouseTable, "Position", _posSource, Help.Anchor);
            AddRow(mouseTable, "", new Control[] { _x, _y }, Help.Anchor, "px", _pickPositionButton);
            AddRow(mouseTable, "Expression", _posExpr, Help.ExpressionPosition);
            _mouseBox.Controls.Add(mouseTable);

            // Keyboard
            var kbTable = NewTable();
            AddRow(kbTable, "Key", _kbKey);
            AddRow(kbTable, "Text", _kbSource);
            AddRow(kbTable, "", _kbText);
            AddRow(kbTable, "", _kbTextExpr, Help.ExpressionText);
            AddRow(kbTable, "Time", _kbTime, Help.KeyTime);
            AddRow(kbTable, "Delay", _kbDelay, Help.KeyDelay);
            _keyboardBox.Controls.Add(kbTable);

            // Command
            var cmdTable = NewTable();
            AddRow(cmdTable, "Path", _cmdValue, null, null, MakeButton("Browse", (s, e) => BrowseCommandPath()));
            _commandBox.Controls.Add(cmdTable);

            // Control
            var controlTable = NewTable();
            AddRow(controlTable, "Type", _loopType);
            AddRow(controlTable, "Times", _loopCount, Help.Count);
            AddRow(controlTable, "Duration", _loopDuration);
            AddRow(controlTable, "Until", _loopUntil);
            AddRow(controlTable, "Process", _waitProcessName, null, null, _waitPick);
            AddRow(controlTable, "", _waitProcessTitle, Help.TitleRegex);
            AddRow(controlTable, "Wait", _waitTime, Help.WaitTime);
            AddRow(controlTable, "Retry", _waitRetry, Help.Retry, "Times");
            AddRow(controlTable, "ID", _imgId, Help.Id);
            AddRow(controlTable, "Process", _imgProcessName, null, null, _imgPickProcess);
            AddRow(controlTable, "", _imgProcessTitle, Help.TitleRegex);
            AddRow(controlTable, "Anchor", _imgAnchor, Help.RangeAnchor);
            AddRow(controlTable, "Position", new Control[] { _imgX, _imgY }, Help.RangeSize, "px");
            AddRow(controlTable, "Size", new Control[] { _imgW, _imgH }, Help.RangeSize, "px", _imgPickRange);
            AddRow(controlTable, "Picture", Stack(_imgPreview, _imgInfo), null, null, Stack(_imgCapture, _imgLoad), _imgPreview);
            AddRow(controlTable, "Accuracy", _imgAccuracy, Help.Accuracy, "%", _imgFind);
            AddRow(controlTable, "ID", _varId, Help.Id);
            AddRow(controlTable, "Value type", _varType, Help.ValueType);
            AddRow(controlTable, "", _varUseExpr);
            AddRow(controlTable, "Expression", _varExpr, Help.ExpressionVariable);
            AddRow(controlTable, "Value", _varText);
            AddRow(controlTable, "Value", _varBool);
            AddRow(controlTable, "Date", _varDate);
            AddRow(controlTable, "Date and time", _varDateTime);
            AddRow(controlTable, "Position", new Control[] { _varX, _varY }, Help.PositionPick, "px", _varPickPos);
            AddRow(controlTable, "Size", new Control[] { _varW, _varH }, null, "px", _varPickRange);
            AddRow(controlTable, "Condition", _caseExpr, Help.ExpressionCase);
            _controlBox.Controls.Add(controlTable);

            // Extra space above the first row of a new group, so related rows read together.
            const int Group = 12, Small = 6;
            Space(_category, Small); Space(_mouseAction, Small); Space(_kbAction, Small); Space(_cmdKind, Small); Space(_controlKind, Small);
            common.Padding = new Padding(GroupInset, 0, 0, 10); // Padding (not Margin): a docked table ignores its margin
            Space(_time, Group); Space(_processName, Group);                     // Mouse: after Type, after Delay
            Space(_kbTime, Group);                                               // Keyboard: before Time
            Space(_waitTime, Group);                                             // Wait: after the Process rows
            Space(_imgProcessName, Group); Space(_imgPreview, Group);            // Image: after ID, after Size
            Space(_varUseExpr, Group);                                           // Variable: after Value type

            _tips.SetToolTip(_pickProcessButton, "Pick a running program: click any window of it");
            _tips.SetToolTip(_waitPick, "Pick a running program: click any window of it");
            _tips.SetToolTip(_imgPickProcess, "Pick a running program: click any window of it");
            _tips.SetToolTip(_pickPositionButton, "Pick a point on the screen (or in the program's window when a Process is set)");
            _tips.SetToolTip(_varPickPos, "Pick a point on the screen");
            _tips.SetToolTip(_imgPickRange, "Pick an area: drag a rectangle on the screen");
            _tips.SetToolTip(_varPickRange, "Pick an area: drag a rectangle on the screen");
            _tips.SetToolTip(_imgCapture, "Capture: drag a rectangle on the screen to use it as the picture");
            _tips.SetToolTip(_imgLoad, "Pick: choose an image file to use as the picture");
            _tips.SetToolTip(_imgFind, "Check: look for the picture on the screen now and show the match");

            // Docked top-to-bottom: the control added last is docked first.
            _editArea.Controls.Add(_controlBox);
            _editArea.Controls.Add(_commandBox);
            _editArea.Controls.Add(_keyboardBox);
            _editArea.Controls.Add(_mouseBox);
            _editArea.Controls.Add(common);

            var right = new Panel { Dock = DockStyle.Right, Width = DetailWidth, Padding = new Padding(8), AutoScroll = true };
            right.Controls.Add(_editArea);

            // Fill control first, edge-docked panels afterwards.
            Controls.Add(_list);
            Controls.Add(right);
            Controls.Add(statusBar);
            Controls.Add(topBar);
        }

        private void WireEvents()
        {
            _list.SelectedIndexChanged += (s, e) => { RefreshControlKinds(); LoadSelectedIntoEditor(); UpdateEditorVisibility(); UpdateButtonStates(); };
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
            _imgPickProcess.Click += (s, e) => PickProcess(_imgProcessName);
            _imgPickRange.Click += (s, e) => PickRange();
            _imgCapture.Click += (s, e) => CaptureImage();
            _imgLoad.Click += (s, e) => LoadImageFile();
            _imgFind.Click += (s, e) => FindNow();
            _varType.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _varUseExpr.CheckedChanged += (s, e) => UpdateEditorVisibility();
            _varPickPos.Click += (s, e) => PickVariablePosition();
            _varPickRange.Click += (s, e) => PickVariableRange();
            _posSource.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _kbSource.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
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
            SetRowVisible(_mouseAction, category == ActionCategory.Mouse);
            SetRowVisible(_kbAction, category == ActionCategory.Keyboard);
            SetRowVisible(_cmdKind, category == ActionCategory.Command);
            SetRowVisible(_controlKind, category == ActionCategory.Control);

            // Mouse
            var action = (MouseAction)_mouseAction.SelectedItem;
            bool scroll = action == MouseAction.ScrollUp || action == MouseAction.ScrollDown;
            RefreshButtonChoices(scroll);
            SetRowVisible(_notches, scroll);
            SetRowVisible(_button, action != MouseAction.Move); // moving the cursor does not involve a button
            // Without a process the position is absolute on the desktop, so an anchor means nothing.
            _anchor.Enabled = _processName.Text.Trim().Length > 0 || _processTitle.Text.Trim().Length > 0;
            bool posFromVariable = _posSource.SelectedIndex == 1;
            SetRowVisible(_x, !posFromVariable);
            SetRowVisible(_y, !posFromVariable);
            SetRowVisible(_pickPositionButton, !posFromVariable);
            SetRowVisible(_posExpr, posFromVariable);

            // Keyboard
            var kb = (KeyboardAction)_kbAction.SelectedItem;
            bool isText = kb == KeyboardAction.Text;
            bool textFromVariable = _kbSource.SelectedIndex == 1;
            _kbKey.Combo = kb == KeyboardAction.ComboKey;
            SetRowVisible(_kbKey, !isText);
            SetRowVisible(_kbSource, isText);
            SetRowVisible(_kbText, isText && !textFromVariable);
            SetRowVisible(_kbTextExpr, isText && textFromVariable);
            SetRowVisible(_kbTime, kb == KeyboardAction.KeyPress || kb == KeyboardAction.ComboKey || isText);

            // Control
            var controlKind = (ControlKind)_controlKind.SelectedItem;
            bool loop = controlKind == ControlKind.Loop;
            bool wait = controlKind == ControlKind.Wait;
            bool image = controlKind == ControlKind.Image;
            bool variable = controlKind == ControlKind.Variable;
            bool caseKind = controlKind == ControlKind.Case;
            bool condition = controlKind == ControlKind.Condition;
            var loopType = (LoopType)_loopType.SelectedItem;
            SetRowVisible(_loopType, loop);
            SetRowVisible(_loopCount, loop && loopType == LoopType.Times);
            SetRowVisible(_loopDuration, loop && loopType == LoopType.Duration);
            SetRowVisible(_loopUntil, loop && loopType == LoopType.Until);
            SetRowVisible(_waitProcessName, wait);
            SetRowVisible(_waitProcessTitle, wait);
            SetRowVisible(_waitTime, wait);
            SetRowVisible(_waitRetry, wait);
            SetRowVisible(_waitPick, wait);
            foreach (var c in new Control[] { _imgId, _imgProcessName, _imgProcessTitle, _imgPickProcess, _imgAnchor, _imgX, _imgY, _imgW, _imgH,
                                              _imgPickRange, _imgPreview, _imgInfo, _imgCapture, _imgLoad, _imgAccuracy, _imgFind })
                SetRowVisible(c, image);
            _imgAnchor.Enabled = _imgProcessName.Text.Trim().Length > 0 || _imgProcessTitle.Text.Trim().Length > 0;

            // Variable
            var varType = (VariableType)_varType.SelectedItem;
            bool expr = _varUseExpr.Checked;
            bool literal = variable && !expr;
            bool geometry = literal && (varType == VariableType.Position || varType == VariableType.Range);
            SetRowVisible(_varId, variable);
            SetRowVisible(_varType, variable);
            SetRowVisible(_varUseExpr, variable);
            SetRowVisible(_varExpr, variable && expr);
            SetRowVisible(_varText, literal && (varType == VariableType.String || varType == VariableType.Number));
            SetRowVisible(_varBool, literal && varType == VariableType.Boolean);
            SetRowVisible(_varDate, literal && varType == VariableType.Date);
            SetRowVisible(_varDateTime, literal && varType == VariableType.DateTime);
            SetRowVisible(_varX, geometry);
            SetRowVisible(_varY, geometry);
            SetRowVisible(_varW, literal && varType == VariableType.Range);
            SetRowVisible(_varH, literal && varType == VariableType.Range);
            SetRowVisible(_varPickPos, literal && varType == VariableType.Position);
            SetRowVisible(_varPickRange, literal && varType == VariableType.Range);

            // Condition and case
            SetRowVisible(_caseExpr, caseKind || condition);
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
                    bool fromVariable = _posSource.SelectedIndex == 1;
                    return ActionNode.NewMouse(new MouseSettings
                    {
                        Action = (MouseAction)_mouseAction.SelectedItem,
                        Button = (MouseAction)_mouseAction.SelectedItem == MouseAction.Move ? MouseButton.Left : (MouseButton)_button.SelectedItem,
                        TimeMs = _time.Milliseconds,
                        DelayMs = _delay.Milliseconds,
                        Process = Target(_processName, _processTitle),
                        Anchor = (WindowAnchor)_anchor.SelectedItem,
                        X = (int)_x.Value,
                        Y = (int)_y.Value,
                        ScrollNotches = (int)_notches.Value,
                        UseVariable = fromVariable,
                        PositionExpression = fromVariable ? _posExpr.Text.Trim() : null
                    });

                case ActionCategory.Keyboard:
                    var action = (KeyboardAction)_kbAction.SelectedItem;
                    bool expression = action == KeyboardAction.Text && _kbSource.SelectedIndex == 1;
                    return ActionNode.NewKeyboard(new KeyboardSettings
                    {
                        Action = action,
                        Value = action != KeyboardAction.Text ? _kbKey.Value : expression ? _kbTextExpr.Text.Trim() : _kbText.Text,
                        ValueIsExpression = expression,
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
                    switch ((ControlKind)_controlKind.SelectedItem)
                    {
                        case ControlKind.Image:
                            return ActionNode.NewImage(BuildImageSettings());
                        case ControlKind.Variable:
                            return ActionNode.NewVariable(BuildVariableSettings());
                        case ControlKind.Condition:
                            var created = ActionNode.NewCondition();
                            created.Children[0].Case.Expression = _caseExpr.Text.Trim();
                            return created;
                        case ControlKind.Case:
                            return ActionNode.NewCase(new CaseSettings { Expression = _caseExpr.Text.Trim() });
                        case ControlKind.Wait:
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

        private VariableSettings BuildVariableSettings()
        {
            var type = (VariableType)_varType.SelectedItem;
            var v = new VariableSettings
            {
                Id = _varId.Text.Trim(),
                Type = type,
                UseExpression = _varUseExpr.Checked,
                Expression = _varExpr.Text.Trim(),
                X = (int)_varX.Value,
                Y = (int)_varY.Value,
                Width = (int)_varW.Value,
                Height = (int)_varH.Value
            };
            switch (type)
            {
                case VariableType.String:
                    v.Text = _varText.Text;
                    break;
                case VariableType.Number:
                    // Accept "1.5" and, on a machine that uses a decimal comma, "1,5"; always stored with a dot.
                    string typed = _varText.Text.Trim();
                    v.Text = double.TryParse(typed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n)
                        || double.TryParse(typed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out n)
                        ? n.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : typed;
                    break;
                case VariableType.Boolean:
                    v.Text = _varBool.Checked ? "true" : "false";
                    break;
                case VariableType.Date:
                    v.Text = _varDate.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case VariableType.DateTime:
                    v.Text = _varDateTime.Value.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
            return v;
        }

        private ImageSettings BuildImageSettings()
        {
            var settings = new ImageSettings
            {
                Id = _imgId.Text.Trim().Length == 0 ? null : _imgId.Text.Trim(),
                Process = Target(_imgProcessName, _imgProcessTitle),
                Anchor = (WindowAnchor)_imgAnchor.SelectedItem,
                RangeX = (int)_imgX.Value,
                RangeY = (int)_imgY.Value,
                RangeWidth = (int)_imgW.Value,
                RangeHeight = (int)_imgH.Value,
                Accuracy = (int)_imgAccuracy.Value
            };
            if (_imageRaw != null) settings.SetImage(_imageRaw);
            return settings;
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
            if (node.IsKind(ControlKind.Else))
            {
                SetStatus("else has no settings: select it and use Add to put actions inside it.");
                return;
            }
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
                    _posSource.SelectedIndex = m.UseVariable ? 1 : 0;
                    _posExpr.Text = m.PositionExpression ?? "";
                    break;

                case ActionCategory.Keyboard:
                    var k = node.Keyboard;
                    _kbAction.SelectedItem = k.Action;
                    UpdateEditorVisibility();
                    if (k.Action == KeyboardAction.Text)
                    {
                        _kbSource.SelectedIndex = k.ValueIsExpression ? 1 : 0;
                        if (k.ValueIsExpression) _kbTextExpr.Text = k.Value; else _kbText.Text = k.Value;
                    }
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
                    switch (node.Control)
                    {
                        case ControlKind.Image:
                            LoadImageSettings(node.Image);
                            break;
                        case ControlKind.Variable:
                            LoadVariableSettings(node.Variable);
                            break;
                        case ControlKind.Condition:
                            _caseExpr.Text = node.Children[0].Case.Expression; // the first case; Update writes it back
                            break;
                        case ControlKind.Case:
                            _caseExpr.Text = node.Case.Expression;
                            break;
                        case ControlKind.Wait:
                            var w = node.Wait;
                            _waitProcessName.Text = w.Process.ProcessName ?? "";
                            _waitProcessTitle.Text = w.Process.TitlePattern ?? "";
                            _waitTime.Milliseconds = w.TimeMs;
                            _waitRetry.Value = Clamp(_waitRetry, w.Retry);
                            break;
                        case ControlKind.Loop:
                            _loopType.SelectedItem = node.Loop.Mode;
                            _loopCount.Value = (decimal)Math.Min(node.Loop.Count, (long)_loopCount.Maximum);
                            _loopDuration.Milliseconds = node.Loop.DurationMs;
                            var until = node.Loop.GetUntil();
                            if (until.HasValue && until.Value >= _loopUntil.MinDate && until.Value <= _loopUntil.MaxDate) _loopUntil.Value = until.Value;
                            break;
                    }
                    break;
            }
        }

        private void LoadImageSettings(ImageSettings im)
        {
            _imgId.Text = im.Id ?? "";
            _imgProcessName.Text = im.Process.ProcessName ?? "";
            _imgProcessTitle.Text = im.Process.TitlePattern ?? "";
            _imgAnchor.SelectedItem = im.Anchor;
            _imgX.Value = Clamp(_imgX, im.RangeX);
            _imgY.Value = Clamp(_imgY, im.RangeY);
            _imgW.Value = Clamp(_imgW, im.RangeWidth);
            _imgH.Value = Clamp(_imgH, im.RangeHeight);
            _imgAccuracy.Value = Clamp(_imgAccuracy, im.Accuracy);
            RawImage raw = null;
            if (im.HasImage)
            {
                try { raw = im.GetImage(); }
                catch (InvalidDataException) { SetStatus("The picture stored in this action is damaged; capture it again."); }
            }
            SetImage(raw);
        }

        private void LoadVariableSettings(VariableSettings v)
        {
            _varId.Text = v.Id;
            _varType.SelectedItem = v.Type;
            _varUseExpr.Checked = v.UseExpression;
            _varExpr.Text = v.Expression;
            _varX.Value = Clamp(_varX, v.X);
            _varY.Value = Clamp(_varY, v.Y);
            _varW.Value = Clamp(_varW, v.Width);
            _varH.Value = Clamp(_varH, v.Height);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (v.Type)
            {
                case VariableType.String:
                case VariableType.Number:
                    _varText.Text = v.Text;
                    break;
                case VariableType.Boolean:
                    _varBool.Checked = string.Equals(v.Text, "true", StringComparison.OrdinalIgnoreCase);
                    break;
                case VariableType.Date:
                    if (DateTime.TryParseExact(v.Text, "yyyy-MM-dd", inv, System.Globalization.DateTimeStyles.None, out DateTime d)) _varDate.Value = d;
                    break;
                case VariableType.DateTime:
                    if (DateTime.TryParseExact(v.Text, "yyyy-MM-ddTHH:mm:ss", inv, System.Globalization.DateTimeStyles.None, out DateTime dt)) _varDateTime.Value = dt;
                    break;
            }
        }

        // ---------------------------------------------------------------- tree editing

        /// <summary>The Condition that the single selected line belongs to (its Begin/End line, a case, the else, or anything inside), if any.</summary>
        private ActionNode ConditionScopeNode()
        {
            if (_list.SelectedIndices.Count != 1) return null;
            for (var n = _rows[_list.SelectedIndices[0]].Node; n != null; n = ParentOf(n))
                if (n.IsKind(ControlKind.Condition)) return n;
            return null;
        }

        /// <summary>Offers Action = Case only while a line inside a Condition is selected.</summary>
        private void RefreshControlKinds()
        {
            bool allow = ConditionScopeNode() != null;
            bool has = _controlKind.Items.Contains(ControlKind.Case);
            if (allow == has) return;
            if (allow)
            {
                _controlKind.Items.Add(ControlKind.Case);
                return;
            }
            bool wasCase = _controlKind.SelectedItem is ControlKind k && k == ControlKind.Case;
            _controlKind.Items.Remove(ControlKind.Case);
            if (wasCase) _controlKind.SelectedIndex = 0;
        }

        /// <summary>The expression without spacing, except inside quoted text where spaces are part of the value.</summary>
        private static string Canonical(string expression)
        {
            var sb = new System.Text.StringBuilder();
            char quote = '\0';
            foreach (char c in expression ?? "")
            {
                if (quote != '\0')
                {
                    sb.Append(c);
                    if (c == quote) quote = '\0';
                }
                else if (c == '"' || c == '\'')
                {
                    quote = c;
                    sb.Append(c);
                }
                else if (!char.IsWhiteSpace(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>A message if another case of the Condition already has the same condition (spacing ignored); such a case could never run.</summary>
        private static string DuplicateCase(ActionNode condition, ActionNode candidate, ActionNode ignore)
        {
            string key = Canonical(candidate.Case.Expression);
            foreach (var other in condition.Children)
            {
                if (!other.IsKind(ControlKind.Case) || ReferenceEquals(other, ignore)) continue;
                if (Canonical(other.Case.Expression) == key)
                    return "This Condition already has a case \"" + other.Case.Expression.Trim() + "\"; a second case with the same condition would never run.";
            }
            return null;
        }

        private static bool IsBranch(ActionNode node)
        {
            return node.IsKind(ControlKind.Case) || node.IsKind(ControlKind.Else);
        }

        /// <summary>
        /// Add: inside the selected container (Loop, Wait, Image, a Condition's case or else), otherwise right after the
        /// selected action, otherwise at the end. A Case goes into the selected Condition, just before its else.
        /// </summary>
        private void AddAction()
        {
            var node = ReadEditor();
            string problem = Validate(node);
            if (problem != null) { SetStatus(problem); return; }
            var selected = SelectedRows();

            if (node.IsKind(ControlKind.Case))
            {
                ActionNode condition = ConditionScopeNode();
                if (condition == null) { SetStatus("Select a line of a Condition to add a Case to it."); return; }
                string duplicate = DuplicateCase(condition, node, null);
                if (duplicate != null) { SetStatus(duplicate); return; }
                condition.Children.Insert(condition.Children.Count - 1, node); // the else stays last
            }
            else if (selected.Count == 1 && selected[0].Node.IsKind(ControlKind.Condition))
            {
                SetStatus("A Condition holds cases: set Action = Case, or select a case or the else to add an action inside it.");
                return;
            }
            else if (selected.Count == 1 && selected[0].Node.IsContainer)
                selected[0].Node.Children.Add(node);
            else if (selected.Count > 0)
            {
                var last = selected[selected.Count - 1];
                if (IsBranch(last.Node)) { SetStatus("Select a single case or else to add an action inside it."); return; }
                last.Siblings.Insert(last.Siblings.IndexOf(last.Node) + 1, node);
            }
            else
                _script.Actions.Add(node);
            RefreshList(new[] { node });
            _name.Clear(); // the next action should not inherit this alias
        }

        /// <summary>Returns a message if the action could never run (for example a key that is not set), otherwise null.</summary>
        private static string Validate(ActionNode node)
        {
            try
            {
                switch (node.Category)
                {
                    case ActionCategory.Mouse:
                        if (node.Mouse.UseVariable)
                        {
                            if (string.IsNullOrWhiteSpace(node.Mouse.PositionExpression)) return "Enter the position expression, for example {img.Position}.";
                            Expression.CheckPositionSyntax(node.Mouse.PositionExpression);
                        }
                        break;

                    case ActionCategory.Keyboard:
                        var k = node.Keyboard;
                        if (k.Action == KeyboardAction.ComboKey) KeyCodes.ParseCombo(k.Value);
                        else if (k.Action != KeyboardAction.Text) KeyCodes.ParseKey(k.Value);
                        else if (k.ValueIsExpression) Expression.CheckSyntax(k.Value);
                        else if (k.Value.Length == 0) return "Type the text to send first.";
                        break;

                    case ActionCategory.Control:
                        if (node.IsKind(ControlKind.Image))
                        {
                            if (!node.Image.HasImage) return "Capture or load a picture first.";
                            if ((node.Image.RangeWidth > 0) != (node.Image.RangeHeight > 0))
                                return "Set both Width and Height of the range, or leave both at 0 to search the whole area.";
                            if (!string.IsNullOrWhiteSpace(node.Image.Id) && !Expression.IsValidId(node.Image.Id))
                                return "\"" + node.Image.Id + "\" is not a valid ID: use letters, digits and _, not starting with a digit (and not true, false, now, today).";
                        }
                        else if (node.IsKind(ControlKind.Variable))
                        {
                            var v = node.Variable;
                            if (!Expression.IsValidId(v.Id))
                                return "Enter an ID with letters, digits and _, not starting with a digit (and not true, false, now, today).";
                            if (v.UseExpression)
                            {
                                if (string.IsNullOrWhiteSpace(v.Expression)) return "Enter the expression, for example {abc} + 1.";
                                Expression.CheckSyntax(v.Expression);
                            }
                            else v.GetLiteral();
                        }
                        else if (node.IsKind(ControlKind.Condition))
                        {
                            string first = node.Children[0].Case.Expression;
                            if (string.IsNullOrWhiteSpace(first)) return "Enter the condition, for example {abc} > 3.";
                            Expression.CheckSyntax(first);
                        }
                        else if (node.IsKind(ControlKind.Case))
                        {
                            if (string.IsNullOrWhiteSpace(node.Case.Expression)) return "Enter the condition, for example {abc} > 3.";
                            Expression.CheckSyntax(node.Case.Expression);
                        }
                        break;
                }
            }
            catch (ExpressionException ex)
            {
                return ex.Message;
            }
            catch (ArgumentException ex)
            {
                return ex.Message + " Click the Key box and press the key(s).";
            }
            return null;
        }

        private void UpdateAction()
        {
            var row = SingleSelectedRow();
            if (row == null) { SetStatus("Select exactly one action to update."); return; }
            if (row.Node.IsKind(ControlKind.Else)) { SetStatus("else has no settings to update."); return; }
            var edited = ReadEditor();
            string problem = Validate(edited);
            if (problem != null) { SetStatus(problem); return; }
            // A Condition and its Cases keep their structure: they cannot turn into something else or be created by Update.
            if (row.Node.IsKind(ControlKind.Condition) != edited.IsKind(ControlKind.Condition)
                || row.Node.IsKind(ControlKind.Case) != edited.IsKind(ControlKind.Case))
            {
                SetStatus("A Condition or a Case cannot be changed into another type (and nothing can be changed into one); add a new action instead.");
                return;
            }
            if (!edited.IsContainer && row.Node.Children.Count > 0)
            {
                SetStatus("This action contains other actions; move or delete them before changing it to a different type.");
                return;
            }
            if (edited.IsKind(ControlKind.Case))
            {
                string duplicate = DuplicateCase(row.Parent, edited, row.Node);
                if (duplicate != null) { SetStatus(duplicate); return; }
            }
            string firstCase = null;
            if (edited.IsKind(ControlKind.Condition))
            {
                // Update on a Condition changes its name and its first case.
                firstCase = edited.Children[0].Case.Expression;
                string duplicate = DuplicateCase(row.Node, edited.Children[0], row.Node.Children[0]);
                if (duplicate != null) { SetStatus(duplicate); return; }
            }
            edited.Children = row.Node.Children;
            if (firstCase != null) edited.Children[0].Case.Expression = firstCase;
            row.Siblings[row.Siblings.IndexOf(row.Node)] = edited;
            RefreshList(new[] { edited });
        }

        private ActionNode ParentOf(ActionNode node)
        {
            return _rows.FirstOrDefault(r => !r.IsEnd && ReferenceEquals(r.Node, node))?.Parent;
        }

        private void DeleteActions()
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            var nodes = new HashSet<ActionNode>(selected.Select(r => r.Node));

            bool AncestorSelected(Row r)
            {
                for (var p = r.Parent; p != null; p = ParentOf(p))
                    if (nodes.Contains(p)) return true;
                return false;
            }

            foreach (var row in selected)
            {
                if (AncestorSelected(row)) continue; // goes away with its Condition
                if (row.Node.IsKind(ControlKind.Else)) { SetStatus("The else line cannot be deleted."); return; }
                if (row.Node.IsKind(ControlKind.Case) && !row.Siblings.Any(c => c.IsKind(ControlKind.Case) && !nodes.Contains(c)))
                {
                    SetStatus("A Condition needs at least one case; delete the whole Condition instead.");
                    return;
                }
            }

            int firstIndex = _list.SelectedIndices.Cast<int>().Min();
            foreach (var row in selected) row.Siblings.Remove(row.Node); // children go with their container
            RefreshList(null);
            int next = Math.Min(firstIndex, _list.Items.Count - 1);
            if (next >= 0) _list.Items[next].Selected = true;
            SetStatus("Deleted " + selected.Count + " action(s).");
        }

        /// <summary>Moves the selected actions one place up (-1) or down (1) within their list; they must share one parent.</summary>
        private void MoveUpDown(int delta)
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            if (!SameList(selected)) { SetStatus("Select actions that are in the same list to move them."); return; }
            if (selected.Any(r => r.Node.IsKind(ControlKind.Else))) { SetStatus("The else line always stays last."); return; }

            var list = selected[0].Siblings;
            var picked = new HashSet<ActionNode>(selected.Select(r => r.Node));
            var indices = selected.Select(r => list.IndexOf(r.Node)).OrderBy(i => i).ToList();
            var order = delta < 0 ? indices : Enumerable.Reverse(indices).ToList();
            foreach (int i in order)
            {
                int target = i + delta;
                // Blocked at the edge, by a neighbour that is selected too, or by an else (which stays last).
                if (target < 0 || target >= list.Count || picked.Contains(list[target]) || list[target].IsKind(ControlKind.Else)) continue;
                var node = list[i];
                list[i] = list[target];
                list[target] = node;
            }
            RefreshList(picked);
        }

        /// <summary>In: moves the selected actions to the end of the Loop, Wait or Image right before them.</summary>
        private void MoveIn()
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            if (!SameList(selected)) { SetStatus("Select actions that are in the same list to move them."); return; }
            if (selected.Any(r => IsBranch(r.Node))) { SetStatus("Cases and else stay inside their Condition."); return; }

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
            if (container != null && container.IsKind(ControlKind.Condition))
            {
                SetStatus("In: a Condition holds cases; add an action inside a case or the else instead.");
                return;
            }
            if (container == null || !container.IsContainer)
            {
                SetStatus("In: the action right before the selection must be a Loop, Wait or Image.");
                return;
            }
            var moving = list.Where(picked.Contains).ToList();
            foreach (var node in moving) list.Remove(node);
            container.Children.AddRange(moving);
            RefreshList(picked);
        }

        /// <summary>Out: moves the selected actions out of their container, right after it (after the Condition when they are in a case or else).</summary>
        private void MoveOut()
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            if (!SameList(selected)) { SetStatus("Select actions that are in the same list to move them."); return; }
            if (selected.Any(r => IsBranch(r.Node))) { SetStatus("Cases and else stay inside their Condition."); return; }
            var parent = selected[0].Parent;
            if (parent == null) { SetStatus("Out: the selected actions are already at the top level."); return; }

            // From inside a case or else the destination is after the whole Condition.
            var anchor = IsBranch(parent) ? ParentOf(parent) : parent;
            var grandList = FindList(anchor);
            var picked = new HashSet<ActionNode>(selected.Select(r => r.Node));
            var moving = selected[0].Siblings.Where(picked.Contains).ToList();
            foreach (var node in moving) selected[0].Siblings.Remove(node);
            grandList.InsertRange(grandList.IndexOf(anchor) + 1, moving);
            RefreshList(picked);
        }

        private static bool SameList(List<Row> rows)
        {
            return rows.All(r => ReferenceEquals(r.Siblings, rows[0].Siblings));
        }

        /// <summary>The list that contains the given node.</summary>
        private List<ActionNode> FindList(ActionNode node)
        {
            return _rows.First(r => !r.IsEnd && ReferenceEquals(r.Node, node)).Siblings;
        }

        /// <summary>The selected row when exactly one real row is selected (the virtual End line does not count).</summary>
        private Row SingleSelectedRow()
        {
            if (_list.SelectedIndices.Count != 1) return null;
            var row = _rows[_list.SelectedIndices[0]];
            return row.IsEnd ? null : row;
        }

        /// <summary>Selected rows in list order, without the virtual End lines.</summary>
        private List<Row> SelectedRows()
        {
            return _list.SelectedIndices.Cast<int>().OrderBy(i => i).Select(i => _rows[i]).Where(r => !r.IsEnd).ToList();
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
                if (!_rows[i].IsEnd && wanted.Contains(_rows[i].Node))
                {
                    item.Selected = true;
                    last = i;
                }
            }
            _list.EndUpdate();
            if (last >= 0) _list.EnsureVisible(last);
            UpdateButtonStates();
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
                if (node.IsKind(ControlKind.Condition))
                    _rows.Add(new Row { Node = node, Parent = parent, Siblings = nodes, Depth = depth, IsEnd = true });
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
            string name = null, action = "", process = "", details = "", time = "", delay = "";
            switch (n.Category)
            {
                case ActionCategory.Mouse:
                    var m = n.Mouse;
                    action = m.Action.ToString();
                    process = DescribeTarget(m.Process);
                    bool hasButton = m.Action == MouseAction.Click || m.Action == MouseAction.MouseDown || m.Action == MouseAction.MouseUp;
                    details = (hasButton ? m.Button + " " : "")
                        + (m.UseVariable ? Shorten(m.PositionExpression) : "(" + m.X + ", " + m.Y + ")");
                    if (!m.Process.IsEmpty) details += " " + m.Anchor;
                    if (m.Action == MouseAction.ScrollUp || m.Action == MouseAction.ScrollDown) details += " x" + m.ScrollNotches;
                    time = TimeBox.Format(m.TimeMs);
                    delay = TimeBox.Format(m.DelayMs);
                    break;

                case ActionCategory.Keyboard:
                    var k = n.Keyboard;
                    action = k.Action.ToString();
                    details = (k.ValueIsExpression ? "= " : "") + Shorten(k.Value);
                    if (k.Action == KeyboardAction.KeyPress || k.Action == KeyboardAction.ComboKey || k.Action == KeyboardAction.Text)
                        time = TimeBox.Format(k.TimeMs);
                    delay = TimeBox.Format(k.DelayMs);
                    break;

                case ActionCategory.Command:
                    action = n.Command.Kind.ToString();
                    details = n.Command.Value;
                    break;

                default:
                    switch (n.Control)
                    {
                        case ControlKind.Image:
                            var im = n.Image;
                            action = "Image";
                            process = DescribeTarget(im.Process);
                            string range = im.RangeWidth > 0 && im.RangeHeight > 0
                                ? "range " + im.RangeWidth + "x" + im.RangeHeight + " at (" + im.RangeX + ", " + im.RangeY + ")" + (im.Process.IsEmpty ? "" : " " + im.Anchor)
                                : "whole area";
                            details = (string.IsNullOrWhiteSpace(im.Id) ? "" : "{" + im.Id + "} ") + im.ImageWidth + "x" + im.ImageHeight + " picture, " + im.Accuracy + "%, " + range;
                            break;
                        case ControlKind.Wait:
                            action = "Wait";
                            process = DescribeTarget(n.Wait.Process);
                            details = "up to " + TimeBox.Format(n.Wait.TimeMs) + (n.Wait.Retry > 0 ? ", retry " + n.Wait.Retry : "");
                            break;
                        case ControlKind.Variable:
                            var v = n.Variable;
                            action = "Variable";
                            details = "{" + v.Id + "} (" + v.Type + ") = " + (v.UseExpression ? Shorten(v.Expression) : DescribeLiteral(v));
                            break;
                        case ControlKind.Condition:
                            action = "Condition";
                            name = (string.IsNullOrWhiteSpace(n.Name) ? "Condition" : n.Name) + (row.IsEnd ? " - End" : " - Begin");
                            if (row.IsEnd) action = "";
                            break;
                        case ControlKind.Case:
                            action = "Case";
                            details = n.Case.Expression;
                            name = string.IsNullOrWhiteSpace(n.Name) ? n.Case.Expression : n.Name;
                            break;
                        case ControlKind.Else:
                            action = "Else";
                            name = "else";
                            break;
                        default:
                            action = "Loop";
                            switch (n.Loop.Mode)
                            {
                                case LoopType.Duration: details = "for " + TimeBox.Format(n.Loop.DurationMs); break;
                                case LoopType.Until: details = "until " + (n.Loop.GetUntil()?.ToString(UntilFormat) ?? "?"); break;
                                default: details = n.Loop.Count == 0 ? "forever" : n.Loop.Count + " time(s)"; break;
                            }
                            break;
                    }
                    break;
            }
            // Name column: the user's alias, or the action type when no alias is set; indented by tree depth.
            if (name == null) name = string.IsNullOrWhiteSpace(n.Name) ? action : n.Name;
            var item = new ListViewItem(indent + name);
            item.SubItems.Add(action);
            item.SubItems.Add(process);
            item.SubItems.Add(details);
            item.SubItems.Add(time);
            item.SubItems.Add(delay);
            return item;
        }

        private static string DescribeLiteral(VariableSettings v)
        {
            switch (v.Type)
            {
                case VariableType.Position: return v.X + ", " + v.Y;
                case VariableType.Range: return v.X + ", " + v.Y + ", " + v.Width + " x " + v.Height;
                case VariableType.String: return "\"" + Shorten(v.Text) + "\"";
                default: return v.Text;
            }
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
            if (!PickPoint(Target(_processName, _processTitle), (WindowAnchor)_anchor.SelectedItem, out int x, out int y)) return;
            _x.Value = Clamp(_x, x);
            _y.Value = Clamp(_y, y);
        }

        private void PickVariablePosition()
        {
            if (!PickPoint(new WindowTarget(), WindowAnchor.TopLeft, out int x, out int y)) return;
            _varX.Value = Clamp(_varX, x);
            _varY.Value = Clamp(_varY, y);
        }

        /// <summary>
        /// Lets the user click a point. Without a process it is a desktop position; with one, the program is brought to the
        /// front first and the point is stored as an offset from the anchor of its client area.
        /// </summary>
        private bool PickPoint(WindowTarget process, WindowAnchor anchor, out int x, out int y)
        {
            x = y = 0;
            if (!process.IsEmpty && !ActivateProcess(process)) return false;
            if (!RunOverlay(out Point p, out WindowInfo? clicked)) return false;

            x = p.X;
            y = p.Y;
            if (!process.IsEmpty)
            {
                // Refuse a point that is not inside the chosen program's client area.
                bool matches;
                try { matches = clicked.HasValue && WindowLocator.Matches(clicked.Value.Handle, process); }
                catch (ArgumentException ex) { SetStatus(ex.Message); return false; }
                if (!matches)
                {
                    SetStatus("That point is not inside a window matching Process / Title.");
                    return false;
                }
                var area = WindowLocator.GetClientArea(clicked.Value.Handle);
                if (x < area.Left || y < area.Top || x >= area.Left + area.Width || y >= area.Top + area.Height)
                {
                    SetStatus("That point is outside the program's client area (title bar or border).");
                    return false;
                }
                WindowLocator.FromScreen(area, anchor, x, y, out x, out y);
            }
            return true;
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

        // ---------------------------------------------------------------- image

        private void Minimize()
        {
            WindowState = FormWindowState.Minimized;
            Application.DoEvents();
            Thread.Sleep(250);
        }

        private void Restore()
        {
            WindowState = FormWindowState.Normal;
            Activate();
        }

        /// <summary>Brings the program's window to the front while this window is still in front; false (with a status message) if it is missing.</summary>
        private bool ActivateProcess(WindowTarget process)
        {
            IntPtr hwnd;
            try { hwnd = WindowLocator.Find(process); }
            catch (ArgumentException ex) { SetStatus(ex.Message); return false; }
            if (hwnd == IntPtr.Zero)
            {
                SetStatus("The program is not running; start it first, or clear Process.");
                return false;
            }
            WindowLocator.BringToFront(hwnd);
            Thread.Sleep(200);
            return true;
        }

        /// <summary>Lets the user drag a rectangle on screen; Process (if set) is activated first.</summary>
        private bool PickRegion(WindowTarget process, bool capture, out Rectangle region, out RawImage image)
        {
            region = Rectangle.Empty;
            image = null;
            if (!process.IsEmpty && !ActivateProcess(process)) return false;

            Minimize();
            ScreenCapture.GetVirtualScreen(out int vl, out int vt, out int vw, out int vh);
            RawImage snapshot = null;
            Bitmap frozen = null;
            if (capture)
            {
                // Take one picture of the screen now and let the user select on that picture, so what is selected is
                // exactly what is captured even when the screen changes meanwhile (animations, pop-ups closing, hover).
                // The cursor is moved to a corner first so no control under it shows its hover look.
                var cursor = Cursor.Position;
                Cursor.Position = new Point(vl + vw - 1, vt + vh - 1);
                Thread.Sleep(250);
                try { snapshot = ScreenCapture.Capture(vl, vt, vw, vh); }
                catch (InvalidOperationException ex)
                {
                    Cursor.Position = cursor;
                    Restore();
                    SetStatus(ex.Message);
                    return false;
                }
                Cursor.Position = cursor;
                frozen = ToBitmap(snapshot);
            }

            bool ok;
            using (frozen)
            using (var picker = new RegionPickerForm(frozen))
            {
                ok = picker.ShowDialog() == DialogResult.OK;
                if (ok) region = picker.SelectedRegion;
            }
            Thread.Sleep(150); // let the overlay disappear
            if (ok && capture) image = Crop(snapshot, region.X - vl, region.Y - vt, region.Width, region.Height);
            Restore();
            return ok;
        }

        /// <summary>A copy of part of an image; the rectangle is clipped to the image.</summary>
        private static RawImage Crop(RawImage source, int x, int y, int width, int height)
        {
            x = Math.Max(0, x);
            y = Math.Max(0, y);
            width = Math.Min(width, source.Width - x);
            height = Math.Min(height, source.Height - y);
            var bytes = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(source.Bgra, ((y + row) * source.Width + x) * 4, bytes, row * width * 4, width * 4);
            return new RawImage(width, height, bytes);
        }

        private void PickRange()
        {
            if (!PickRangeFor(Target(_imgProcessName, _imgProcessTitle), (WindowAnchor)_imgAnchor.SelectedItem, out int x, out int y, out int w, out int h)) return;
            _imgX.Value = Clamp(_imgX, x);
            _imgY.Value = Clamp(_imgY, y);
            _imgW.Value = Clamp(_imgW, w);
            _imgH.Value = Clamp(_imgH, h);
        }

        private void PickVariableRange()
        {
            if (!PickRangeFor(new WindowTarget(), WindowAnchor.TopLeft, out int x, out int y, out int w, out int h)) return;
            _varX.Value = Clamp(_varX, x);
            _varY.Value = Clamp(_varY, y);
            _varW.Value = Clamp(_varW, w);
            _varH.Value = Clamp(_varH, h);
        }

        /// <summary>Lets the user drag a rectangle; with a process the top-left is stored as an offset from the anchor of its client area.</summary>
        private bool PickRangeFor(WindowTarget process, WindowAnchor anchor, out int x, out int y, out int w, out int h)
        {
            x = y = w = h = 0;
            if (!PickRegion(process, false, out Rectangle region, out _)) return false;

            x = region.X;
            y = region.Y;
            w = region.Width;
            h = region.Height;
            if (!process.IsEmpty)
            {
                IntPtr hwnd;
                try { hwnd = WindowLocator.Find(process); }
                catch (ArgumentException ex) { SetStatus(ex.Message); return false; }
                if (hwnd == IntPtr.Zero) { SetStatus("The program is no longer available."); return false; }
                // The range's anchor corner (centre for Center) measured from the same anchor point of the window.
                var regionArea = new ClientArea { Left = region.X, Top = region.Y, Width = region.Width, Height = region.Height };
                WindowLocator.ToScreen(regionArea, anchor, 0, 0, out int cornerX, out int cornerY);
                WindowLocator.FromScreen(WindowLocator.GetClientArea(hwnd), anchor, cornerX, cornerY, out x, out y);
            }
            return true;
        }

        private void CaptureImage()
        {
            if (!PickRegion(Target(_imgProcessName, _imgProcessTitle), true, out Rectangle region, out RawImage image)) return;
            AcceptImage(image);
        }

        private void LoadImageFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "Images (*.png;*.bmp;*.jpg;*.jpeg;*.gif)|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var loaded = new Bitmap(dialog.FileName))
                        AcceptImage(FromBitmap(loaded));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is OutOfMemoryException)
                {
                    SetStatus("Cannot read the picture: " + ex.Message);
                }
            }
        }

        /// <summary>Takes a new template picture, refusing one that has no pattern to look for.</summary>
        private void AcceptImage(RawImage image)
        {
            if (!ImageMatcher.HasEnoughDetail(image))
            {
                SetStatus("That area is almost a single colour; pick an area with some detail (text, an icon, an edge).");
                return;
            }
            SetImage(image);
            SetStatus(image.Width > 300 || image.Height > 300
                ? "Picture set. It is large (" + image.Width + "x" + image.Height + "): searching will be slower and the script file bigger; a smaller distinctive part works better."
                : "Picture set (" + image.Width + "x" + image.Height + "). Press Add or Update to keep it.");
        }

        private void SetImage(RawImage image)
        {
            _imageRaw = image;
            var old = _imgPreview.Image;
            _imgPreview.Image = image == null ? null : ToBitmap(image);
            old?.Dispose();
            _imgInfo.Text = image == null ? "No picture" : image.Width + " x " + image.Height + " px";
        }

        /// <summary>Shows where the picture is on screen right now and how well it matches, to help choose the accuracy.</summary>
        private void FindNow()
        {
            if (_imageRaw == null) { SetStatus("Capture or load a picture first."); return; }
            var settings = BuildImageSettings();
            if (!settings.Process.IsEmpty && !ActivateProcess(settings.Process)) return;

            Minimize(); // this window must not cover what is searched
            string message;
            try
            {
                var found = ImageLocator.Locate(settings, settings.Process);
                int percent = found.Score >= 0 ? (int)Math.Round(found.Score * 100) : 0;
                message = found.Found
                    ? "Found at screen (" + found.Rect.Left + ", " + found.Rect.Top + "), match " + percent + "%."
                    : "Not found" + (found.Score >= 0 ? " (best match " + percent + "%, needs " + settings.Accuracy + "%)." : ".");
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                message = ex.Message;
            }
            Restore();
            SetStatus(message);
        }

        private static Bitmap ToBitmap(RawImage image)
        {
            var bitmap = new Bitmap(image.Width, image.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bytes = (byte[])image.Bgra.Clone();
                for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255; // screen captures carry no alpha
                for (int y = 0; y < image.Height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(bytes, y * image.Width * 4, data.Scan0 + y * data.Stride, image.Width * 4);
            }
            finally { bitmap.UnlockBits(data); }
            return bitmap;
        }

        private static RawImage FromBitmap(Bitmap source)
        {
            using (var bitmap = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap)) g.DrawImage(source, 0, 0, source.Width, source.Height);
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var bytes = new byte[bitmap.Width * bitmap.Height * 4];
                    for (int y = 0; y < bitmap.Height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bitmap.Width * 4, bitmap.Width * 4);
                    return new RawImage(bitmap.Width, bitmap.Height, bytes);
                }
                finally { bitmap.UnlockBits(data); }
            }
        }

        // ---------------------------------------------------------------- files

        private void MarkSaved()
        {
            _savedSnapshot = ScriptSerializer.ToJson(_script);
        }

        private bool IsDirty()
        {
            return ScriptSerializer.ToJson(_script) != _savedSnapshot;
        }

        /// <summary>True when there is nothing to lose or the user agrees to lose it.</summary>
        private bool ConfirmLoseChanges(string question)
        {
            if (!IsDirty()) return true;
            string what = _currentPath == null
                ? "The actions in the list have not been saved."
                : "\"" + Path.GetFileName(_currentPath) + "\" has changes that have not been saved.";
            string message = what + " Unsaved data will be lost.\n\n" + question;
            if (ConfirmDiscard != null) return ConfirmDiscard(message);
            return MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void NewScript()
        {
            if (!ConfirmLoseChanges("Do you want to start a new script anyway?")) return;
            _script = new AutomationScript();
            _currentPath = null;
            RefreshList();
            UpdateTitle();
            MarkSaved();
        }

        private void OpenScript()
        {
            using (var dialog = new OpenFileDialog { Filter = "Automation script (*.json)|*.json|All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!ConfirmLoseChanges("Do you want to open \"" + Path.GetFileName(dialog.FileName) + "\" anyway?")) return;
                try
                {
                    _script = ScriptSerializer.Load(dialog.FileName);
                    _currentPath = dialog.FileName;
                    RefreshList(_script.Actions.Take(1));
                    UpdateTitle();
                    MarkSaved();
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
                MarkSaved();
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
            _saveAs.Visible = _currentPath != null; // Save As only makes sense once there is a file to copy from
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
            UpdateButtonStates();
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitDetailsColumn();
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
            // Ask first: answering No must not stop a running script.
            if (!ConfirmLoseChanges("Do you want to close the program anyway?"))
            {
                e.Cancel = true;
                return;
            }
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

        private static decimal Clamp(NumberBox box, int value)
        {
            return Math.Max(box.Minimum, Math.Min(box.Maximum, value));
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

        /// <summary>An icon button for the toolbar (Segoe MDL2 Assets when installed, else a plain symbol).</summary>
        private ToolStripButton MakeToolIcon(string mdl2Glyph, string fallbackGlyph, string tip, EventHandler onClick)
        {
            bool mdl2 = FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets");
            var button = new ToolStripButton(mdl2 ? mdl2Glyph : fallbackGlyph)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = new Font(mdl2 ? "Segoe MDL2 Assets" : "Segoe UI Symbol", 13f, FontStyle.Bold),
                ForeColor = Color.Black,
                ToolTipText = tip,
                AccessibleName = tip,
                Padding = new Padding(4, 2, 4, 2) // the button grows with its glyph, so it fits at any display scaling
            };
            // A ToolStrip button does not take focus, so the field being edited would keep it: move focus away first.
            // That commits typed numbers (Validating) and ends key recording (Leave re-registers the F6 hot key).
            button.Click += (s, e) =>
            {
                if (!_running) _list.Focus();
                onClick(s, e);
            };
            return button;
        }

        /// <summary>Add needs nothing selected; every other action button needs at least one selected action, and none work while a script runs.</summary>
        private void UpdateButtonStates()
        {
            bool any = SelectedRows().Count > 0;
            _btnAdd.Enabled = !_running;
            foreach (var button in new[] { _btnDelete, _btnIn, _btnOut, _btnUp, _btnDown })
                button.Enabled = any && !_running;
            _btnUpdate.Enabled = SingleSelectedRow() != null && !_running; // Update changes one action, so exactly one must be selected
            // Strong black when usable, pale grey when not, so the two states are easy to tell apart.
            foreach (var button in new[] { _btnAdd, _btnUpdate, _btnDelete, _btnIn, _btnOut, _btnUp, _btnDown })
                button.ForeColor = button.Enabled ? Color.Black : Color.FromArgb(185, 185, 185);
        }

        private static TableLayoutPanel NewTable()
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LabelWidth));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return table;
        }

        /// <summary>Adds vertical space above a row (identified by its first control); the space moves with the row when it is hidden or shown.</summary>
        private void Space(Control key, int pixels)
        {
            if (_rowLabels.TryGetValue(key, out var label))
                label.Padding = new Padding(label.Padding.Left, label.Padding.Top + pixels, label.Padding.Right, label.Padding.Bottom);
            Control cell = _rowHosts.TryGetValue(key, out var host) ? host : key;
            cell.Margin = new Padding(cell.Margin.Left, cell.Margin.Top + pixels, cell.Margin.Right, cell.Margin.Bottom);
        }

        /// <summary>A unit text (px, %) placed after a field.</summary>
        private static Label Unit(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 0, 0, 0) };
        }

        /// <summary>Controls stacked top to bottom in one cell (for example a preview with its caption, or two buttons).</summary>
        private static FlowLayoutPanel Stack(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 6, 0) };
            panel.Controls.AddRange(controls);
            return panel;
        }

        /// <summary>Gives a single input field the common width.</summary>
        private static void FitField(Control c)
        {
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is DateTimePicker) c.Width = FieldWidth;
        }

        /// <summary>
        /// Adds a "label | field" row. Fields are FieldWidth wide (two fields, such as X and Y, share that width). When the
        /// row has a unit text, a button or a "?" they go in fixed columns to the right of the field, so everything lines up:
        /// field(s) | unit and button | "?". The first field stands for the row in SetRowVisible (or key, when given).
        /// </summary>
        private void AddRow(TableLayoutPanel table, string label, Control field, HelpTopic help = null, string unit = null, Control button = null, Control key = null)
        {
            AddRow(table, label, new[] { field }, help, unit, button, key);
        }

        private void AddRow(TableLayoutPanel table, string label, Control[] fields, HelpTopic help = null, string unit = null, Control button = null, Control key = null)
        {
            var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) };
            Control cell;
            if (fields.Length == 1 && help == null && unit == null && button == null)
            {
                FitField(fields[0]);
                cell = fields[0];
            }
            else
            {
                // field | unit | button | ?   (fixed columns, so units, buttons and the ? icons each line up from row to row)
                var host = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 1, Margin = new Padding(0) };
                host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldWidth + 6));
                host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UnitWidth));
                host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ButtonWidth));
                host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                if (fields.Length == 1)
                {
                    FitField(fields[0]);
                    host.Controls.Add(fields[0], 0, 0);
                }
                else
                {
                    // Several fields share the one field width (X and Y side by side).
                    var pair = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(3, 3, 3, 3), Padding = new Padding(0) };
                    int each = (FieldWidth - 4 * (fields.Length - 1)) / fields.Length;
                    for (int i = 0; i < fields.Length; i++)
                    {
                        fields[i].Width = each;
                        fields[i].Margin = new Padding(0, 0, i < fields.Length - 1 ? 4 : 0, 0);
                        pair.Controls.Add(fields[i]);
                    }
                    host.Controls.Add(pair, 0, 0);
                }

                if (unit != null) host.Controls.Add(Unit(unit), 1, 0);
                if (button != null) host.Controls.Add(button, 2, 0);
                if (help != null) host.Controls.Add(new HelpIcon(help), 3, 0);
                cell = host;
                _rowHosts[key ?? fields[0]] = host;
            }
            // Explicit cells: with auto placement a hidden control is skipped and the later controls shift into the wrong columns.
            int row = table.RowCount++;
            table.Controls.Add(caption, 0, row);
            table.Controls.Add(cell, 1, row);
            _rowLabels[key ?? fields[0]] = caption;
        }

        private void SetRowVisible(Control control, bool visible)
        {
            (_rowHosts.TryGetValue(control, out var host) ? host : control).Visible = visible;
            if (_rowLabels.TryGetValue(control, out var label)) label.Visible = visible;
        }
    }
}
