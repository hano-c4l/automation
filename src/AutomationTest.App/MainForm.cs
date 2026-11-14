using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutomationTest.Core.Excel;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Imaging;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Recording;
using AutomationTest.Core.Runner;
using AutomationTest.Core.Windows;

namespace AutomationTest.App
{
    public sealed class MainForm : Form
    {
        private const int HotKeyToggleId = 1;
        private const int HotKeyRecordId = 3;
        private const int HotKeyStartSelectedId = 6;
        private const int HotKeyFreezeId = 4;      // the Pick key while a Pick waits for the picture
        private const int HotKeyFreezeCancelId = 5; // Esc while a Pick waits
        private const string UntilFormat = "yyyy-MM-dd HH:mm:ss";
        private const int DetailWidth = 475;
        private const int FieldWidth = 200;   // every input field
        private const int LabelWidth = 100;
        private const int GroupInset = 8;
        private const string RowHostTag = "row";    // a group box insets its content by its padding and border

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
            public bool HasChildren;           // can be collapsed / expanded
            public bool Collapsed;             // its children are hidden
            public bool Disabled;              // this action or one of its parents is disabled (shown grey, skipped when running)
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly HashSet<ActionNode> _collapsed = new HashSet<ActionNode>(); // nodes whose children are hidden in the list
        private const string ExpandedMark = "▼ ", CollapsedMark = "▶ ", LeafMark = "    ";

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
        private IconButton _startStop, _startSelected, _record;
        private IconButton _btnNew, _btnOpen, _btnSave, _collapseAll, _expandAll;
        private IconButton _settingsButton;
        private AppSettings _settings = AppSettings.Defaults();

        private enum RunKind { None, All, Selected }
        private RunKind _runKind;
        private readonly InputRecorder _recorder = new InputRecorder();
        private bool _recording;
        private List<Row> _recordTarget = new List<Row>();
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel("Ready") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        // Editor: common
        private readonly HintTextBox _name = new HintTextBox { Width = 200, Placeholder = "Alias Name"  };
        private readonly ToggleSwitch _enabled = new ToggleSwitch { Checked = true, Size = new Size(40, 21) }; // as tall as a text field, so the rows are evenly spaced
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
        private readonly TimeBox _cmdDelay = new TimeBox(0);
        private readonly TimeBox _cmdTime = new TimeBox(0);
        private readonly ToggleSwitch _cmdForce = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };

        // Editor: command
        private readonly ComboBox _cmdKind = MakeCombo(typeof(CommandKind));
        private readonly HintTextBox _cmdValue = new HintTextBox { Width = 200, Placeholder = "Process Path" };

        // Editor: web
        private readonly ComboBox _webKind = MakeCombo(typeof(WebKind));
        private readonly ComboBox _webBrowser = MakeCombo(typeof(BrowserKind));
        private readonly HintTextBox _webUrl = new HintTextBox { Width = 200, Placeholder = "https://..." };
        private readonly HintTextBox _webTitle = new HintTextBox { Width = 200, Placeholder = "Title (regular expression)" };
        private readonly HintTextBox _webUrlPattern = new HintTextBox { Width = 200, Placeholder = "URL (regular expression)" };
        private readonly NumberBox _webIndex = new NumberBox { Width = 200, Minimum = 0, Maximum = 100000, Placeholder = "Index (1 = first)", Text = "1" };
        private readonly ComboBox _webGet = MakeCombo(typeof(WebGetKind));
        private readonly HintTextBox _webAttribute = new HintTextBox { Width = 200, Placeholder = "Attribute name (href, src...)" };
        private readonly HintTextBox _webScript = new HintTextBox { Width = 200, Height = 120, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical, Placeholder = "JavaScript" };
        private readonly HintTextBox _webSelector = new HintTextBox { Width = 200, Placeholder = "CSS selector or text=Label" };
        private readonly HintTextBox _webFrame = new HintTextBox { Width = 200, Placeholder = "Frame (iframe selector)" };
        private Button _webPick;
        private readonly ToggleSwitch _webRequired = new ToggleSwitch { Checked = true, Size = new Size(40, 21) };
        private readonly HintTextBox _webId = new HintTextBox { Width = 200, Placeholder = "Variable ID" };
        private readonly HintTextBox _webText = new HintTextBox { Width = 200, Placeholder = "Text" };
        private readonly ToggleSwitch _webTextExpr = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _webAccept = new ToggleSwitch { Checked = true, Size = new Size(40, 21) };
        private readonly ComboBox _webClickType = MakeCombo(typeof(WebClickKind));
        private readonly ComboBox _webUntil = MakeCombo(typeof(WebWaitKind));
        private readonly HintTextBox _webTarget = new HintTextBox { Width = 200, Placeholder = "Drop on (CSS selector or text=Label)" };
        private readonly ToggleSwitch _webFullPage = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly TimeBox _webTime = new TimeBox(10000);
        private readonly TimeBox _webDelay = new TimeBox(0);
        // Editor: excel
        private readonly ComboBox _xlKind = MakeCombo(typeof(ExcelKind));
        private readonly HintTextBox _xlId = new HintTextBox { Width = 200, Placeholder = "Variable ID" };
        private readonly HintTextBox _xlPath = new HintTextBox { Width = 200, Placeholder = "File path (empty = new workbook)" };
        private readonly HintTextBox _xlName = new HintTextBox { Width = 200, Placeholder = "Open workbook name (regular expr.)" };
        private readonly ToggleSwitch _xlReadOnly = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _xlVisible = new ToggleSwitch { Checked = true, Size = new Size(40, 21) };
        private readonly HintTextBox _xlSheetName = new HintTextBox { Width = 200, Placeholder = "Sheet name (regular expression)" };
        private readonly NumberBox _xlIndex = new NumberBox { Width = 200, Minimum = 0, Maximum = 100000, Placeholder = "Index (1 = first)" };
        private readonly ComboBox _xlPosition = MakeCombo(typeof(ExcelSheetPosition));
        private readonly ToggleSwitch _xlSelectNew = new ToggleSwitch { Checked = true, Size = new Size(40, 21) };
        private readonly HintTextBox _xlCell = new HintTextBox { Width = 200, Placeholder = "Cell (B2)" };
        private readonly ToggleSwitch _xlCellExpr = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ComboBox _xlGet = MakeCombo(typeof(ExcelGetKind));
        private readonly ComboBox _xlInfo = MakeCombo(typeof(ExcelInfoKind));
        private readonly ComboBox _xlAs = MakeCombo(typeof(ExcelSetKind));
        private readonly HintTextBox _xlValue = new HintTextBox { Width = 200, Placeholder = "Value" };
        private readonly ToggleSwitch _xlValueExpr = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _xlMatchCase = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _xlWholeCell = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _xlSaveAs = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly HintTextBox _xlSavePath = new HintTextBox { Width = 200, Placeholder = "New file (.xlsx .xlsm .xlsb .xls .csv .pdf)" };
        private readonly ToggleSwitch _xlSaveChanges = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _xlAllowMacros = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ComboBox _xlTarget = MakeCombo(typeof(ExcelTarget));
        private readonly NumberBox _xlCount = new NumberBox { Width = 200, Minimum = 1, Maximum = 100000, Placeholder = "How many", Text = "1" };
        private readonly ComboBox _xlClearWhat = MakeCombo(typeof(ExcelClearKind));
        private readonly HintTextBox _xlNumberFormat = new HintTextBox { Width = 200, Placeholder = "Number format (0.00, dd/mm/yyyy, @)" };
        private readonly ComboBox _xlBold = MakeCombo(typeof(ExcelFlag));
        private readonly ComboBox _xlItalic = MakeCombo(typeof(ExcelFlag));
        private readonly HintTextBox _xlFontColor = new HintTextBox { Width = 200, Placeholder = "Font colour (#RRGGBB)" };
        private readonly HintTextBox _xlFillColor = new HintTextBox { Width = 200, Placeholder = "Fill colour (#RRGGBB)" };
        private readonly HintTextBox _xlColumnWidth = new HintTextBox { Width = 200, Placeholder = "Column width (a number, or auto)" };
        private readonly HintTextBox _xlSortBy = new HintTextBox { Width = 200, Placeholder = "Column to sort by (letters, C)" };
        private readonly ToggleSwitch _xlDescending = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _xlHasHeader = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly NumberBox _xlFilterField = new NumberBox { Width = 200, Minimum = 1, Maximum = 16384, Placeholder = "Column of the area (1 = first)", Text = "1" };
        private readonly HintTextBox _xlCriteria = new HintTextBox { Width = 200, Placeholder = "Criterion (>5, =Hanoi, <>, *text*)" };
        private readonly ToggleSwitch _xlClearFilter = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly ToggleSwitch _xlValuesOnly = new ToggleSwitch { Checked = false, Size = new Size(40, 21) };
        private readonly HintTextBox _xlMacro = new HintTextBox { Width = 200, Placeholder = "Macro (Module1.Report)" };
        private readonly TimeBox _xlTime = new TimeBox(30000);
        private readonly TimeBox _xlDelay = new TimeBox(0);
        private Button _xlPick;
        private readonly GroupBox _excelBox = new GroupBox { Text = "Excel", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };

        // Editor: loop For
        private readonly HintTextBox _forId = new HintTextBox { Width = 200, Placeholder = "Counter variable ID" };
        private readonly HintTextBox _forFrom = new HintTextBox { Width = 200, Placeholder = "From (number or expression)", Text = "1" };
        private readonly HintTextBox _forTo = new HintTextBox { Width = 200, Placeholder = "To (number or expression)", Text = "10" };
        private readonly HintTextBox _forStep = new HintTextBox { Width = 200, Placeholder = "Step (1, -1, 0.5...)", Text = "1" };

        private readonly GroupBox _webBox = new GroupBox { Text = "Web", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };

        // Editor: control (loop, wait)
        private readonly ComboBox _controlKind = MakeCombo(typeof(ControlKind));
        private readonly ComboBox _loopType = MakeCombo(typeof(LoopType));
        private readonly NumericUpDown _loopCount = MakeNumber(0, 1000000000, 1);
        private readonly TimeBox _loopDuration = new TimeBox(10000);
        private readonly DateTimePicker _loopUntil = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = UntilFormat, Width = 200 };
        private readonly HintTextBox _waitProcessName = new HintTextBox { Width = 200, Placeholder = "Process Name" };
        private readonly HintTextBox _waitProcessTitle = new HintTextBox { Width = 200, Placeholder = "Process Title (Regex)" };
        private readonly TimeBox _waitTime = new TimeBox(10000);
        private readonly TimeBox _waitSettle = new TimeBox(500);
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
        private IconButton _saveAs, _license;

        private readonly GroupBox _mouseBox = new GroupBox { Text = "Mouse", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _keyboardBox = new GroupBox { Text = "Keyboard", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _commandBox = new GroupBox { Text = "Utilities", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private readonly GroupBox _controlBox = new GroupBox { Text = "Control", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        private IconButton _btnAdd, _btnUpdate, _btnDelete, _btnIn, _btnOut, _btnUp, _btnTop, _btnDown, _btnEnd;
        private readonly Dictionary<Control, Label> _rowLabels = new Dictionary<Control, Label>();
        private readonly Dictionary<Control, Control> _rowHosts = new Dictionary<Control, Control>();
        private readonly ToolTip _tips = new ToolTip();
        private string _savedSnapshot = "";

        /// <summary>
        /// Asked before unsaved changes would be lost (New, Open, closing). Receives the message and returns Yes (save first),
        /// No (go on without saving) or Cancel. Null shows a Yes/No/Cancel dialog; tests replace it.
        /// </summary>
        public Func<string, DialogResult> ConfirmSave { get; set; }
        // AutoSize + Dock.Top so the area is exactly as tall as its content; the parent panel scrolls when the window is small.
        private readonly Panel _editArea = new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

        public MainForm()
        {
            Text = "Automation Test";
            Icon = AppIcon.Get() ?? Icon;
            ClientSize = new Size(1380, 600);
            MinimumSize = new Size(1150, 600);
            _loopUntil.Value = DateTime.Now.AddHours(1);
            _varDateTime.Value = DateTime.Now;
            _controlKind.Items.Remove(ControlKind.Else); // the else line is created with its Condition, never added by hand
            _controlKind.Items.Remove(ControlKind.Case); // Case is offered only while a line of a Condition is selected
            _posSource.SelectedIndex = 0;
            _category.SelectedItem = ActionCategory.Mouse; // the editor starts on Mouse
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
            _list.Columns.Add("Name", 100);
            _list.Columns.Add("Action", 90);
            _list.Columns.Add("Process", 80);
            _list.Columns.Add("Details", 300);
            _list.Columns.Add("Time", 65);
            _list.Columns.Add("Delay", 65);

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Fill, RenderMode = ToolStripRenderMode.System };
            // The menu bar uses the same icon buttons as the action buttons (hover for the name).
            _btnNew = MakeToolIcon("", "N", "New script", (s, e) => NewScript());
            _btnOpen = MakeToolIcon("", "O", "Open a script", (s, e) => OpenScript());
            _btnSave = MakeToolIcon("", "S", "Save", (s, e) => { if (SaveScript(false)) LicenseAllowsWork(); });
            _saveAs = MakeToolIcon("", "S+", "Save As", (s, e) => { if (SaveScript(true)) LicenseAllowsWork(); });
            tools.Items.Add(_btnNew);
            tools.Items.Add(_btnOpen);
            tools.Items.Add(_btnSave);
            tools.Items.Add(_saveAs);
            tools.Items.Add(new ToolStripSeparator());
            _collapseAll = MakeToolIcon("", "▲", "Collapse All", (s, e) => CollapseAll(true));
            _expandAll = MakeToolIcon("", "▼", "Expand All", (s, e) => CollapseAll(false));
            tools.Items.Add(_collapseAll);
            tools.Items.Add(_expandAll);
            // The run and record buttons sit at the right end of this bar. Right-aligned items are laid out from the right edge,
            // so they are added from the rightmost one to the leftmost one.
            _startStop = MakeToolIcon("", "▶", "Start", (s, e) => ToggleRun());
            _startSelected = MakeToolIcon("", "▶▶", "Start Selected: run only the selected actions", (s, e) => ToggleRunSelected());
            _record = MakeToolIcon("", "●", "Record: your mouse clicks, wheel and keys become actions", (s, e) => ToggleRecord());
            // Start, Start Selected and Record sit in the middle of this bar, between the file buttons and the action buttons on
            // the right: a spacer in front of them is resized with the bar (CenterRunButtons).
            tools.Items.Add(_centerSpacer);
            tools.Items.Add(_startStop);
            tools.Items.Add(_startSelected);
            tools.Items.Add(_record);
            tools.Resize += (s, e) => CenterRunButtons(tools);
            // License and Settings sit at the far right of the whole menu row, after the action buttons (see the iconBar below).
            _license = MakeToolIcon("", "⚿", "License", (s, e) => ShowLicense());
            _settingsButton = MakeToolIcon("", "⚙", "Settings: shortcut keys and options", (s, e) => ShowSettings());
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
            _btnTop = MakeToolIcon("\uE898", "\u2912", "Up to Top: move the selected actions to the start of their list", (s, e) => MoveToEdge(true));
            _btnDown = MakeToolIcon("\uE74B", "\u2193", "Down", (s, e) => MoveUpDown(1));
            _btnEnd = MakeToolIcon("\uE896", "\u2913", "Down to End: move the selected actions to the end of their list", (s, e) => MoveToEdge(false));
            // Add, Update, Delete | In, Out | Up, Up to Top, Down, Down to End
            foreach (ToolStripItem item in new ToolStripItem[] { _btnAdd, _btnUpdate, _btnDelete, new ToolStripSeparator(), _btnIn, _btnOut, new ToolStripSeparator(), _btnUp, _btnTop, _btnDown, _btnEnd })
                iconBar.Items.Add(item);
            // License and Settings at the right end of the row; right-aligned items are added from the rightmost one.
            foreach (ToolStripItem item in new ToolStripItem[] { _settingsButton, _license })
            {
                item.Alignment = ToolStripItemAlignment.Right;
                iconBar.Items.Add(item);
            }
            ApplyIconColors();

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
            AddRow(common, "State", _enabled);
            AddRow(common, "Category", _category);
            AddRow(common, "Action", _mouseAction);
            AddRow(common, "Action", _kbAction);
            AddRow(common, "Action", _cmdKind);
            AddRow(common, "Action", _webKind);
            AddRow(common, "Action", _xlKind);
            AddRow(common, "Action", _controlKind);

            // Every field is FieldWidth wide; a unit text and a Pick button go in a second fixed column and the "?" in a
            // third, so the right edges and the "?" icons line up from row to row.
            // Mouse
            _pickProcessButton.Click += (s, e) => PickProcess(_processName, _processTitle);
            _pickPositionButton.Click += (s, e) => PickPosition();
            var mouseTable = NewTable();
            AddRow(mouseTable, "Type", _button);
            AddRow(mouseTable, "Scroll (notches)", _notches);
            AddRow(mouseTable, "Time", _time, Help.MouseTime);
            AddRow(mouseTable, "Delay", _delay, Help.MouseDelay);
            AddRow(mouseTable, "Process", _processName, null, null, _pickProcessButton);
            AddRow(mouseTable, "", _processTitle, Help.TitleRegex);
            AddRow(mouseTable, "Anchor", _anchor, Help.Anchor);
            AddRow(mouseTable, "Position (px)", _posSource, Help.Anchor);
            AddRow(mouseTable, "", new Control[] { _x, _y }, Help.Anchor, null, _pickPositionButton);
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

            // Utilities
            var cmdTable = NewTable();
            AddRow(cmdTable, "Path", _cmdValue, null, null, MakeButton("Browse", (s, e) => BrowseCommandPath()));
            AddRow(cmdTable, "Time", _cmdTime, Help.ShutdownTime);
            AddRow(cmdTable, "Force", _cmdForce, Help.ShutdownForce);
            AddRow(cmdTable, "Delay", _cmdDelay, Help.KeyDelay);
            _commandBox.Controls.Add(cmdTable);

            // Web
            var webTable = NewTable();
            AddRow(webTable, "ID", _webId, Help.WebId); // an ID is always the first field of its group
            AddRow(webTable, "Browser", _webBrowser, Help.WebBrowser);
            AddRow(webTable, "Get", _webGet, Help.WebGet);
            AddRow(webTable, "Wait for", _webUntil, Help.WebUntil);
            AddRow(webTable, "Click type", _webClickType, Help.WebClickType);
            AddRow(webTable, "URL", _webUrl, Help.WebUrl);
            AddRow(webTable, "Title", _webTitle, Help.WebTitle);
            AddRow(webTable, "URL", _webUrlPattern, Help.WebUrlPattern);
            _webPick = MakeButton("Pick", (s, e) => PickWebElement());
            AddRow(webTable, "Selector", _webSelector, Help.WebSelector, null, _webPick);
            AddRow(webTable, "Frame", _webFrame, Help.WebFrame);
            AddRow(webTable, "Index", _webIndex, Help.WebIndex);
            AddRow(webTable, "Drop on", _webTarget, Help.WebDrag);
            AddRow(webTable, "Attribute", _webAttribute, Help.WebAttribute);
            AddRow(webTable, "Required", _webRequired, Help.WebRequired);
            AddRow(webTable, "Script", _webScript, Help.WebScript);
            AddRow(webTable, "Accept", _webAccept, Help.WebDialog);
            AddRow(webTable, "Text", _webText, Help.WebText);
            AddRow(webTable, "Expression", _webTextExpr, Help.ExpressionText);
            AddRow(webTable, "Full page", _webFullPage, Help.WebScreenshot);
            AddRow(webTable, "Timeout", _webTime, Help.WebTimeout);
            AddRow(webTable, "Delay", _webDelay, Help.KeyDelay);
            _webBox.Controls.Add(webTable);

            // Excel
            var xlTable = NewTable();
            AddRow(xlTable, "ID", _xlId, Help.ExcelId); // an ID is always the first field of its group
            AddRow(xlTable, "Path", _xlPath, Help.ExcelWorkbook, null, MakeButton("Browse", (s, e) => BrowseExcelFile(_xlPath, false)));
            AddRow(xlTable, "Name", _xlName, Help.ExcelWorkbookName);
            AddRow(xlTable, "Read only", _xlReadOnly, Help.ExcelReadOnly);
            AddRow(xlTable, "Visible", _xlVisible, Help.ExcelVisible);
            AddRow(xlTable, "Allow macros", _xlAllowMacros, Help.ExcelAllowMacros);
            AddRow(xlTable, "Sheet", _xlSheetName, Help.ExcelSheet);
            AddRow(xlTable, "Index", _xlIndex, Help.ExcelIndex);
            AddRow(xlTable, "Position", _xlPosition, Help.ExcelPosition);
            AddRow(xlTable, "Select it", _xlSelectNew, Help.ExcelSelectNew);
            AddRow(xlTable, "Get", _xlGet, Help.ExcelGet);
            AddRow(xlTable, "Info", _xlInfo, Help.ExcelInfo);
            _xlPick = MakeButton("Pick", (s, e) => PickExcelCell());
            AddRow(xlTable, "Macro", _xlMacro, Help.ExcelMacro);
            AddRow(xlTable, "Target", _xlTarget, Help.ExcelInsert);
            AddRow(xlTable, "Count", _xlCount, Help.ExcelInsert);
            AddRow(xlTable, "Clear", _xlClearWhat, Help.ExcelClear);
            AddRow(xlTable, "Cell", _xlCell, Help.ExcelCell, null, _xlPick);
            AddRow(xlTable, "Expr. (cell)", _xlCellExpr, Help.ExpressionText);
            AddRow(xlTable, "Write as", _xlAs, Help.ExcelSetAs);
            AddRow(xlTable, "Value", _xlValue, Help.ExcelValue);
            AddRow(xlTable, "Expr. (value)", _xlValueExpr, Help.ExpressionText);
            AddRow(xlTable, "Values only", _xlValuesOnly, Help.ExcelCopy);
            AddRow(xlTable, "Num. format", _xlNumberFormat, Help.ExcelFormat);
            AddRow(xlTable, "Bold", _xlBold, Help.ExcelFormat);
            AddRow(xlTable, "Italic", _xlItalic, Help.ExcelFormat);
            AddRow(xlTable, "Font colour", _xlFontColor, Help.ExcelFormat);
            AddRow(xlTable, "Fill colour", _xlFillColor, Help.ExcelFormat);
            AddRow(xlTable, "Col. width", _xlColumnWidth, Help.ExcelFormat);
            AddRow(xlTable, "Sort by", _xlSortBy, Help.ExcelSort);
            AddRow(xlTable, "Descending", _xlDescending, Help.ExcelSort);
            AddRow(xlTable, "Has header", _xlHasHeader, Help.ExcelSort);
            AddRow(xlTable, "Clear filter", _xlClearFilter, Help.ExcelFilter);
            AddRow(xlTable, "Filter col.", _xlFilterField, Help.ExcelFilter);
            AddRow(xlTable, "Criterion", _xlCriteria, Help.ExcelFilter);
            AddRow(xlTable, "Match case", _xlMatchCase, Help.ExcelFind);
            AddRow(xlTable, "Whole cell", _xlWholeCell, Help.ExcelFind);
            AddRow(xlTable, "Save As", _xlSaveAs, Help.ExcelSave);
            AddRow(xlTable, "New file", _xlSavePath, Help.ExcelSave, null, MakeButton("Browse", (s, e) => BrowseExcelFile(_xlSavePath, true)));
            AddRow(xlTable, "Save changes", _xlSaveChanges, Help.ExcelSaveChanges);
            AddRow(xlTable, "Timeout", _xlTime, Help.ExcelTimeout);
            AddRow(xlTable, "Delay", _xlDelay, Help.KeyDelay);
            _excelBox.Controls.Add(xlTable);

            // Control
            var controlTable = NewTable();
            AddRow(controlTable, "ID", _forId, Help.LoopForId); // an ID is always the first field of its group
            AddRow(controlTable, "Type", _loopType);
            AddRow(controlTable, "Times", _loopCount, Help.Count);
            AddRow(controlTable, "Duration", _loopDuration);
            AddRow(controlTable, "Until", _loopUntil);
            AddRow(controlTable, "From", _forFrom, Help.LoopFor);
            AddRow(controlTable, "To", _forTo, Help.LoopFor);
            AddRow(controlTable, "Step", _forStep, Help.LoopFor);
            AddRow(controlTable, "Process", _waitProcessName, null, null, _waitPick);
            AddRow(controlTable, "", _waitProcessTitle, Help.TitleRegex);
            AddRow(controlTable, "Timeout", _waitTime, Help.WaitTime);
            AddRow(controlTable, "Wait", _waitSettle, Help.WaitSettle);
            AddRow(controlTable, "Retry (times)", _waitRetry, Help.Retry);
            AddRow(controlTable, "ID", _imgId, Help.Id);
            AddRow(controlTable, "Process", _imgProcessName, null, null, _imgPickProcess);
            AddRow(controlTable, "", _imgProcessTitle, Help.TitleRegex);
            AddRow(controlTable, "Anchor", _imgAnchor, Help.RangeAnchor);
            AddRow(controlTable, "Range (px)", new Control[] { _imgX, _imgY }, Help.RangeSize);
            AddRow(controlTable, "", new Control[] { _imgW, _imgH }, null, null, _imgPickRange);
            AddRow(controlTable, "Picture", Stack(_imgPreview, _imgInfo), null, null, Stack(_imgCapture, _imgLoad), _imgPreview);
            AddRow(controlTable, "Accuracy (%)", _imgAccuracy, Help.Accuracy, null, _imgFind);
            AddRow(controlTable, "ID", _varId, Help.Id);
            AddRow(controlTable, "Value type", _varType, Help.ValueType);
            AddRow(controlTable, "", _varUseExpr);
            AddRow(controlTable, "Expression", _varExpr, Help.ExpressionVariable);
            AddRow(controlTable, "Value", _varText);
            AddRow(controlTable, "Value", _varBool);
            AddRow(controlTable, "Date", _varDate);
            AddRow(controlTable, "Date and time", _varDateTime);
            AddRow(controlTable, "Position (px)", new Control[] { _varX, _varY }, Help.PositionPick, null, _varPickPos);
            AddRow(controlTable, "Size (px)", new Control[] { _varW, _varH }, null, null, _varPickRange);
            AddRow(controlTable, "Condition", _caseExpr, Help.ExpressionCase);
            _controlBox.Controls.Add(controlTable);

            // Extra space above the first row of a new group, so related rows read together.
            const int Group = 12, Small = 6;
            Space(_enabled, Small); Space(_category, Small); Space(_mouseAction, Small); Space(_kbAction, Small); Space(_cmdKind, Small); Space(_controlKind, Small);
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
            _editArea.Controls.Add(_webBox);
            _editArea.Controls.Add(_excelBox);
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
            _list.SizeChanged += (s, e) => { if (_columnsReady) FitDetailsColumn(false); }; // only once the window is shown: opening must not touch the columns
            // No column can be dragged narrower than the minimum, so what the user sees is what is allowed.
            _list.ColumnWidthChanging += (s, e) => { if (e.NewWidth < MinColumnWidth) e.NewWidth = MinColumnWidth; };
            _list.ColumnWidthChanged += (s, e) => { if (_list.Columns[e.ColumnIndex].Width < MinColumnWidth) _list.Columns[e.ColumnIndex].Width = MinColumnWidth; };
            _list.MouseDown += (s, e) => ListMouseDown(e);
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) DeleteActions();
                else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
                {
                    // Like a tree: Left collapses, Right expands the selected action.
                    var single = SingleSelectedRow();
                    if (single != null && single.HasChildren)
                    {
                        SetCollapsed(single, e.KeyCode == Keys.Left);
                        e.Handled = true;
                    }
                }
                else if (e.Control && e.KeyCode == Keys.A)
                {
                    foreach (ListViewItem item in _list.Items) item.Selected = true;
                    e.SuppressKeyPress = true;
                }
            };
            _category.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _mouseAction.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _kbAction.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _cmdKind.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _webKind.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _webGet.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _webUntil.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _xlKind.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _xlInfo.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _xlSaveAs.CheckedChanged += (s, e) => UpdateEditorVisibility();
            _xlClearFilter.CheckedChanged += (s, e) => UpdateEditorVisibility();
            _controlKind.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _loopType.SelectedIndexChanged += (s, e) => UpdateEditorVisibility();
            _processName.TextChanged += (s, e) => UpdateEditorVisibility();
            _processTitle.TextChanged += (s, e) => UpdateEditorVisibility();

            // The shortcut keys are global hot keys while idle; they must not be swallowed while the user records one of them as a key.
            _waitPick.Click += (s, e) => PickProcess(_waitProcessName, _waitProcessTitle);
            _imgPickProcess.Click += (s, e) => PickProcess(_imgProcessName, _imgProcessTitle);
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
            _commandBox.Visible = category == ActionCategory.Utilities;
            var cmdKind = _cmdKind.SelectedItem as CommandKind? ?? CommandKind.Open;
            SetRowVisible(_cmdValue, cmdKind == CommandKind.Open); // only Open has a path
            SetRowVisible(_cmdTime, cmdKind == CommandKind.Shutdown);
            SetRowVisible(_cmdForce, cmdKind == CommandKind.Shutdown);
            SetRowVisible(_cmdDelay, cmdKind != CommandKind.Shutdown); // Shutdown has no "after" delay: the machine is gone
            _controlBox.Visible = category == ActionCategory.Control;
            _webBox.Visible = category == ActionCategory.Web;
            var web = _webKind.SelectedItem as WebKind? ?? WebKind.NewTab;
            var get = _webGet.SelectedItem as WebGetKind? ?? WebGetKind.Text;
            var until = _webUntil.SelectedItem as WebWaitKind? ?? WebWaitKind.ElementAppears;
            bool pageGet = web == WebKind.GetText && (get == WebGetKind.PageUrl || get == WebGetKind.PageTitle);
            bool waitElement = web == WebKind.WaitElement && (until == WebWaitKind.ElementAppears || until == WebWaitKind.ElementDisappears);
            bool optionalSelector = web == WebKind.PressKey || web == WebKind.Screenshot;
            bool webSelects = waitElement || web == WebKind.Input || web == WebKind.Click || web == WebKind.Hover || web == WebKind.Drag
                || web == WebKind.SelectOption || web == WebKind.Upload || (web == WebKind.GetText && !pageGet) || optionalSelector;
            bool webText = web == WebKind.Input || web == WebKind.Dialog || web == WebKind.PressKey || web == WebKind.SelectOption
                || web == WebKind.Upload || web == WebKind.Screenshot || (web == WebKind.WaitElement && !waitElement);
            SetRowVisible(_webKind, category == ActionCategory.Web);
            SetRowVisible(_webBrowser, category == ActionCategory.Web);
            SetRowVisible(_webUrl, web == WebKind.NewTab);
            SetRowVisible(_webTitle, web == WebKind.SwitchTab);
            SetRowVisible(_webUrlPattern, web == WebKind.SwitchTab);
            SetRowVisible(_webGet, web == WebKind.GetText);
            SetRowVisible(_webUntil, web == WebKind.WaitElement);
            SetRowVisible(_webClickType, web == WebKind.Click);
            SetRowVisible(_webSelector, webSelects);
            _webSelector.Placeholder = web == WebKind.PressKey ? "Element to focus first (optional)" : web == WebKind.Screenshot ? "Element to capture (empty = the page)"
                : web == WebKind.Upload ? "The file input (type=file)" : web == WebKind.Drag ? "Element to drag" : "CSS selector or text=Label";
            SetRowVisible(_webFrame, webSelects);
            SetRowVisible(_webIndex, (webSelects && !(web == WebKind.GetText && get == WebGetKind.Count)) || web == WebKind.SwitchTab);
            SetRowVisible(_webTarget, web == WebKind.Drag);
            SetRowVisible(_webAttribute, web == WebKind.GetText && get == WebGetKind.Attribute);
            SetRowVisible(_webAccept, web == WebKind.Dialog);
            SetRowVisible(_webFullPage, web == WebKind.Screenshot);
            SetRowVisible(_webTime, web != WebKind.CloseTab && web != WebKind.CloseBrowser); // closing has nothing to wait for
            SetRowVisible(_webId, web == WebKind.GetText || web == WebKind.Script);
            _webId.Placeholder = web == WebKind.Script ? "Variable ID (optional)" : "Variable ID";
            SetRowVisible(_webRequired, web == WebKind.GetText && !pageGet && get != WebGetKind.Count);
            SetRowVisible(_webScript, web == WebKind.Script);
            _webText.Placeholder = web == WebKind.Dialog ? "Answer of a prompt box"
                : web == WebKind.PressKey ? "Key (Enter, Tab, Ctrl+A, F5...)"
                : web == WebKind.SelectOption ? "Option (its text or its value)"
                : web == WebKind.Upload ? "File path (several: path1|path2)"
                : web == WebKind.Screenshot ? "File to save (.png or .jpg)"
                : web == WebKind.WaitElement ? (until == WebWaitKind.UrlMatches ? "Address pattern (regular expression)" : until == WebWaitKind.TitleMatches ? "Title pattern (regular expression)" : "Text pattern (regular expression)")
                : "Text";
            SetRowVisible(_webText, webText);
            SetRowVisible(_webTextExpr, webText || web == WebKind.Script);
            // Excel
            _excelBox.Visible = category == ActionCategory.Excel;
            var xl = _xlKind.SelectedItem as ExcelKind? ?? ExcelKind.Workbook;
            var xlInfo = _xlInfo.SelectedItem as ExcelInfoKind? ?? ExcelInfoKind.LastRow;
            bool xlOn = category == ActionCategory.Excel;
            bool infoCell = xl == ExcelKind.GetInfo && (xlInfo == ExcelInfoKind.LastRow || xlInfo == ExcelInfoKind.LastColumn);
            bool xlArea = xl == ExcelKind.Select || xl == ExcelKind.Clear || xl == ExcelKind.Copy || xl == ExcelKind.Format || xl == ExcelKind.Sort
                || (xl == ExcelKind.Filter && !_xlClearFilter.Checked) || xl == ExcelKind.InsertCells || xl == ExcelKind.DeleteCells;
            bool xlCell = xl == ExcelKind.GetCell || xl == ExcelKind.SetCell || xl == ExcelKind.GetRange || xl == ExcelKind.SetRange || xl == ExcelKind.Find || infoCell || xlArea;
            bool xlValue = ScriptRunner.ExcelValueKind(xl) || xl == ExcelKind.SetRange;
            bool insertDelete = xl == ExcelKind.InsertCells || xl == ExcelKind.DeleteCells;
            SetRowVisible(_xlKind, xlOn);
            SetRowVisible(_xlId, xl == ExcelKind.GetCell || xl == ExcelKind.GetRange || xl == ExcelKind.GetInfo || xl == ExcelKind.Find || xl == ExcelKind.Evaluate || xl == ExcelKind.RunMacro);
            _xlId.Placeholder = xl == ExcelKind.Find || xl == ExcelKind.Evaluate || xl == ExcelKind.RunMacro ? "Variable ID (optional)" : "Variable ID";
            SetRowVisible(_xlPath, xl == ExcelKind.Workbook);
            SetRowVisible(_xlName, xl == ExcelKind.Workbook);
            SetRowVisible(_xlReadOnly, xl == ExcelKind.Workbook);
            SetRowVisible(_xlVisible, xl == ExcelKind.Workbook);
            SetRowVisible(_xlAllowMacros, xl == ExcelKind.Workbook);
            SetRowVisible(_xlSheetName, xl == ExcelKind.SelectSheet || xl == ExcelKind.CreateSheet || xl == ExcelKind.DeleteSheet || xl == ExcelKind.RenameSheet);
            _xlSheetName.Placeholder = xl == ExcelKind.CreateSheet ? "Name of the new sheet (optional)" : xl == ExcelKind.RenameSheet ? "Sheet to rename (regular expr.; empty = this one)" : "Sheet name (regular expression)";
            SetRowVisible(_xlIndex, xl == ExcelKind.SelectSheet || xl == ExcelKind.DeleteSheet || xl == ExcelKind.RenameSheet);
            _xlIndex.Placeholder = xl == ExcelKind.DeleteSheet || xl == ExcelKind.RenameSheet ? "Index (empty = the current sheet)" : "Index (1 = first)";
            SetRowVisible(_xlPosition, xl == ExcelKind.CreateSheet);
            SetRowVisible(_xlSelectNew, xl == ExcelKind.CreateSheet);
            SetRowVisible(_xlGet, xl == ExcelKind.GetCell || xl == ExcelKind.GetRange);
            SetRowVisible(_xlInfo, xl == ExcelKind.GetInfo);
            SetRowVisible(_xlTarget, insertDelete);
            SetRowVisible(_xlCount, insertDelete);
            SetRowVisible(_xlClearWhat, xl == ExcelKind.Clear);
            SetRowVisible(_xlCell, xlCell);
            SetRowVisible(_xlPick, xlCell);
            _xlCell.Placeholder = xl == ExcelKind.GetRange ? "Area to read (A1:D20)" : xl == ExcelKind.SetRange ? "Top-left cell (A1)"
                : xl == ExcelKind.Find ? "Area to search (empty = whole sheet)"
                : xl == ExcelKind.GetInfo ? (xlInfo == ExcelInfoKind.LastRow ? "A cell in the column (B1)" : "A cell in the row (B1)")
                : insertDelete ? "A cell in the first row or column (B5)"
                : xl == ExcelKind.Select ? "Cell or area to select"
                : xl == ExcelKind.Clear ? "Area to clear"
                : xl == ExcelKind.Copy ? "Area to copy (A1:C5)"
                : xl == ExcelKind.Format || xl == ExcelKind.Sort || xl == ExcelKind.Filter ? "Area (A1:D100)" : "Cell (B2)";
            SetRowVisible(_xlCellExpr, xlCell);
            SetRowVisible(_xlAs, xl == ExcelKind.SetCell || xl == ExcelKind.SetRange);
            SetRowVisible(_xlValue, xlValue);
            _xlValue.Placeholder = xl == ExcelKind.SetRange ? "Table variable, for example {t}" : xl == ExcelKind.Find ? "Text to find"
                : xl == ExcelKind.Evaluate ? "Formula, for example SUM(A1:A10)" : xl == ExcelKind.RenameSheet ? "New name of the sheet"
                : xl == ExcelKind.Copy ? "Destination (its top-left cell)" : xl == ExcelKind.RunMacro ? "Argument (optional)" : "Value";
            SetRowVisible(_xlValueExpr, xlValue && xl != ExcelKind.SetRange); // Set Range always takes a variable
            SetRowVisible(_xlValuesOnly, xl == ExcelKind.Copy);
            SetRowVisible(_xlNumberFormat, xl == ExcelKind.Format);
            SetRowVisible(_xlBold, xl == ExcelKind.Format);
            SetRowVisible(_xlItalic, xl == ExcelKind.Format);
            SetRowVisible(_xlFontColor, xl == ExcelKind.Format);
            SetRowVisible(_xlFillColor, xl == ExcelKind.Format);
            SetRowVisible(_xlColumnWidth, xl == ExcelKind.Format);
            SetRowVisible(_xlSortBy, xl == ExcelKind.Sort);
            SetRowVisible(_xlDescending, xl == ExcelKind.Sort);
            SetRowVisible(_xlHasHeader, xl == ExcelKind.Sort);
            SetRowVisible(_xlClearFilter, xl == ExcelKind.Filter);
            SetRowVisible(_xlFilterField, xl == ExcelKind.Filter && !_xlClearFilter.Checked);
            SetRowVisible(_xlCriteria, xl == ExcelKind.Filter && !_xlClearFilter.Checked);
            SetRowVisible(_xlMacro, xl == ExcelKind.RunMacro);
            SetRowVisible(_xlMatchCase, xl == ExcelKind.Find);
            SetRowVisible(_xlWholeCell, xl == ExcelKind.Find);
            SetRowVisible(_xlSaveAs, xl == ExcelKind.Save);
            SetRowVisible(_xlSavePath, xl == ExcelKind.Save && _xlSaveAs.Checked);
            SetRowVisible(_xlSaveChanges, xl == ExcelKind.CloseWorkbook || xl == ExcelKind.CloseExcel);
            SetRowVisible(_xlTime, xlOn);
            SetRowVisible(_xlDelay, xlOn);
            SetRowVisible(_mouseAction, category == ActionCategory.Mouse);
            SetRowVisible(_kbAction, category == ActionCategory.Keyboard);
            SetRowVisible(_cmdKind, category == ActionCategory.Utilities);
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
            SetRowVisible(_forId, loop && loopType == LoopType.For);
            SetRowVisible(_forFrom, loop && loopType == LoopType.For);
            SetRowVisible(_forTo, loop && loopType == LoopType.For);
            SetRowVisible(_forStep, loop && loopType == LoopType.For);
            SetRowVisible(_waitProcessName, wait);
            SetRowVisible(_waitProcessTitle, wait);
            SetRowVisible(_waitTime, wait);
            SetRowVisible(_waitSettle, wait);
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
            node.Disabled = !_enabled.Checked;
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

                case ActionCategory.Utilities:
                    return ActionNode.NewCommand(new CommandSettings
                    {
                        Kind = (CommandKind)_cmdKind.SelectedItem,
                        Value = (CommandKind)_cmdKind.SelectedItem == CommandKind.Open ? _cmdValue.Text.Trim() : "",
                        DelayMs = (CommandKind)_cmdKind.SelectedItem == CommandKind.Shutdown ? 0 : _cmdDelay.Milliseconds,
                        TimeMs = (CommandKind)_cmdKind.SelectedItem == CommandKind.Shutdown ? _cmdTime.Milliseconds : 0,
                        Force = (CommandKind)_cmdKind.SelectedItem == CommandKind.Shutdown && _cmdForce.Checked
                    });

                case ActionCategory.Excel:
                    var xk = (ExcelKind)_xlKind.SelectedItem;
                    return ActionNode.NewExcel(new ExcelSettings
                    {
                        Kind = xk,
                        Path = _xlPath.Text.Trim(),
                        Name = _xlName.Text.Trim(),
                        ReadOnly = _xlReadOnly.Checked,
                        Visible = _xlVisible.Checked,
                        SheetName = _xlSheetName.Text.Trim(),
                        Index = (int)_xlIndex.Value,
                        Position = (ExcelSheetPosition)_xlPosition.SelectedItem,
                        SelectNew = _xlSelectNew.Checked,
                        Cell = _xlCell.Text.Trim(),
                        CellIsExpression = _xlCellExpr.Checked,
                        Get = (ExcelGetKind)_xlGet.SelectedItem,
                        Info = (ExcelInfoKind)_xlInfo.SelectedItem,
                        As = (ExcelSetKind)_xlAs.SelectedItem,
                        Value = xk == ExcelKind.SetCell ? _xlValue.Text : _xlValue.Text.Trim(), // spaces count in a value that is written
                        ValueIsExpression = xk == ExcelKind.SetRange || _xlValueExpr.Checked,
                        MatchCase = _xlMatchCase.Checked,
                        WholeCell = _xlWholeCell.Checked,
                        SaveAs = _xlSaveAs.Checked,
                        SavePath = _xlSavePath.Text.Trim(),
                        SaveChanges = _xlSaveChanges.Checked,
                        Target = (ExcelTarget)_xlTarget.SelectedItem,
                        Count = Math.Max(1, (int)_xlCount.Value),
                        ClearWhat = (ExcelClearKind)_xlClearWhat.SelectedItem,
                        NumberFormat = _xlNumberFormat.Text.Trim(),
                        Bold = (ExcelFlag)_xlBold.SelectedItem,
                        Italic = (ExcelFlag)_xlItalic.SelectedItem,
                        FontColor = _xlFontColor.Text.Trim(),
                        FillColor = _xlFillColor.Text.Trim(),
                        ColumnWidth = _xlColumnWidth.Text.Trim(),
                        SortBy = _xlSortBy.Text.Trim(),
                        Descending = _xlDescending.Checked,
                        HasHeader = _xlHasHeader.Checked,
                        FilterField = Math.Max(1, (int)_xlFilterField.Value),
                        Criteria = _xlCriteria.Text.Trim(),
                        ClearFilter = _xlClearFilter.Checked,
                        ValuesOnly = _xlValuesOnly.Checked,
                        Macro = _xlMacro.Text.Trim(),
                        AllowMacros = _xlAllowMacros.Checked,
                        Id = _xlId.Text.Trim(),
                        TimeMs = _xlTime.Milliseconds,
                        DelayMs = _xlDelay.Milliseconds
                    });

                case ActionCategory.Web:
                    return ActionNode.NewWeb(new WebSettings
                    {
                        Kind = (WebKind)_webKind.SelectedItem,
                        Browser = (BrowserKind)_webBrowser.SelectedItem,
                        Url = ((WebKind)_webKind.SelectedItem == WebKind.SwitchTab ? _webUrlPattern.Text : _webUrl.Text).Trim(),
                        Title = _webTitle.Text.Trim(),
                        Index = (int)_webIndex.Value,
                        Get = (WebGetKind)_webGet.SelectedItem,
                        Attribute = _webAttribute.Text.Trim(),
                        Script = _webScript.Text,
                        Selector = _webSelector.Text.Trim(),
                        Frame = _webFrame.Text.Trim(),
                        ClickType = (WebClickKind)_webClickType.SelectedItem,
                        Until = (WebWaitKind)_webUntil.SelectedItem,
                        TargetSelector = _webTarget.Text.Trim(),
                        FullPage = _webFullPage.Checked,
                        Id = _webId.Text.Trim(),
                        Required = _webRequired.Checked,
                        Text = _webText.Text,
                        TextIsExpression = _webTextExpr.Checked,
                        Accept = _webAccept.Checked,
                        TimeMs = _webTime.Milliseconds,
                        DelayMs = _webDelay.Milliseconds
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
                                WaitMs = _waitSettle.Milliseconds,
                                Retry = (int)_waitRetry.Value
                            });
                    }
                    var loop = new LoopSettings
                    {
                        Mode = (LoopType)_loopType.SelectedItem,
                        Count = (long)_loopCount.Value,
                        DurationMs = _loopDuration.Milliseconds,
                        ForId = _forId.Text.Trim(),
                        ForFrom = _forFrom.Text.Trim(),
                        ForTo = _forTo.Text.Trim(),
                        ForStep = _forStep.Text.Trim()
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
            _enabled.Checked = !node.Disabled;
            _webBrowser.Enabled = true; // a Web action inside a New Tab or Switch Tab turns it off below
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

                case ActionCategory.Excel:
                    var xs = node.Excel;
                    _xlKind.SelectedItem = xs.Kind;
                    _xlPath.Text = xs.Path;
                    _xlName.Text = xs.Name;
                    _xlReadOnly.Checked = xs.ReadOnly;
                    _xlVisible.Checked = xs.Visible;
                    _xlSheetName.Text = xs.SheetName;
                    _xlIndex.Text = xs.Index > 0 ? xs.Index.ToString() : "";
                    _xlPosition.SelectedItem = xs.Position;
                    _xlSelectNew.Checked = xs.SelectNew;
                    _xlCell.Text = xs.Cell;
                    _xlCellExpr.Checked = xs.CellIsExpression;
                    _xlGet.SelectedItem = xs.Get;
                    _xlInfo.SelectedItem = xs.Info;
                    _xlAs.SelectedItem = xs.As;
                    _xlValue.Text = xs.Value;
                    _xlValueExpr.Checked = xs.ValueIsExpression;
                    _xlMatchCase.Checked = xs.MatchCase;
                    _xlWholeCell.Checked = xs.WholeCell;
                    _xlSaveAs.Checked = xs.SaveAs;
                    _xlSavePath.Text = xs.SavePath;
                    _xlSaveChanges.Checked = xs.SaveChanges;
                    _xlTarget.SelectedItem = xs.Target;
                    _xlCount.Text = xs.Count.ToString();
                    _xlClearWhat.SelectedItem = xs.ClearWhat;
                    _xlNumberFormat.Text = xs.NumberFormat;
                    _xlBold.SelectedItem = xs.Bold;
                    _xlItalic.SelectedItem = xs.Italic;
                    _xlFontColor.Text = xs.FontColor;
                    _xlFillColor.Text = xs.FillColor;
                    _xlColumnWidth.Text = xs.ColumnWidth;
                    _xlSortBy.Text = xs.SortBy;
                    _xlDescending.Checked = xs.Descending;
                    _xlHasHeader.Checked = xs.HasHeader;
                    _xlFilterField.Text = xs.FilterField.ToString();
                    _xlCriteria.Text = xs.Criteria;
                    _xlClearFilter.Checked = xs.ClearFilter;
                    _xlValuesOnly.Checked = xs.ValuesOnly;
                    _xlMacro.Text = xs.Macro;
                    _xlAllowMacros.Checked = xs.AllowMacros;
                    _xlId.Text = xs.Id;
                    _xlTime.Milliseconds = xs.TimeMs;
                    _xlDelay.Milliseconds = xs.DelayMs;
                    break;

                case ActionCategory.Web:
                    var web = node.Web;
                    _webKind.SelectedItem = web.Kind;
                    _webBrowser.SelectedItem = web.Browser;
                    _webUrl.Text = web.Url;
                    _webUrlPattern.Text = web.Url;
                    _webTitle.Text = web.Title;
                    _webIndex.Text = web.Index > 0 ? web.Index.ToString() : "1";
                    _webGet.SelectedItem = web.Get;
                    _webAttribute.Text = web.Attribute;
                    _webScript.Text = web.Script;
                    _webBrowser.Enabled = !web.BrowserInherited; // inside a New Tab or Switch Tab the browser is theirs
                    _webSelector.Text = web.Selector;
                    _webFrame.Text = web.Frame;
                    _webClickType.SelectedItem = web.ClickType;
                    _webUntil.SelectedItem = web.Until;
                    _webTarget.Text = web.TargetSelector;
                    _webFullPage.Checked = web.FullPage;
                    _webId.Text = web.Id;
                    _webRequired.Checked = web.Required;
                    _webText.Text = web.Text;
                    _webTextExpr.Checked = web.TextIsExpression;
                    _webAccept.Checked = web.Accept;
                    _webTime.Milliseconds = web.TimeMs;
                    _webDelay.Milliseconds = web.DelayMs;
                    break;

                case ActionCategory.Utilities:
                    _cmdKind.SelectedItem = node.Command.Kind;
                    _cmdValue.Text = node.Command.Value;
                    _cmdDelay.Milliseconds = node.Command.DelayMs;
                    _cmdTime.Milliseconds = node.Command.TimeMs;
                    _cmdForce.Checked = node.Command.Force;
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
                            _waitSettle.Milliseconds = w.WaitMs;
                            _waitRetry.Value = Clamp(_waitRetry, w.Retry);
                            break;
                        case ControlKind.Loop:
                            _loopType.SelectedItem = node.Loop.Mode;
                            _loopCount.Value = (decimal)Math.Min(node.Loop.Count, (long)_loopCount.Maximum);
                            _loopDuration.Milliseconds = node.Loop.DurationMs;
                            _forId.Text = node.Loop.ForId;
                            _forFrom.Text = node.Loop.ForFrom;
                            _forTo.Text = node.Loop.ForTo;
                            _forStep.Text = node.Loop.ForStep;
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
        /// <summary>A message when an Excel action could never run (a bad address or pattern, a missing field), otherwise null.</summary>
        private static string ValidateExcel(ExcelSettings x)
        {
            string problem;
            Func<string, string> badPattern = pattern =>
            {
                if (pattern.Trim().Length == 0) return null;
                try { new System.Text.RegularExpressions.Regex(pattern); return null; }
                catch (ArgumentException ex) { return "\"" + pattern + "\" is not a valid regular expression: " + ex.Message; }
            };
            Func<string, bool, string> cellProblem = (text, expression) =>
            {
                if (expression) { Expression.CheckSyntax(text); return null; }
                return AutomationTest.Core.Excel.ExcelAddress.Check(text, "cell");
            };
            switch (x.Kind)
            {
                case ExcelKind.Workbook:
                    if (x.Path.Length == 0 && x.Name.Length > 0 && (problem = badPattern(x.Name)) != null) return problem;
                    break;
                case ExcelKind.SelectSheet:
                case ExcelKind.DeleteSheet:
                    if ((problem = badPattern(x.SheetName)) != null) return problem;
                    break;
                case ExcelKind.CreateSheet:
                    if (x.SheetName.Length > 31 || x.SheetName.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }) >= 0)
                        return "A sheet name has at most 31 characters and none of : \\ / ? * [ ].";
                    break;
                case ExcelKind.GetCell:
                case ExcelKind.SetCell:
                    if ((problem = cellProblem(x.Cell, x.CellIsExpression)) != null) return problem;
                    break;
                case ExcelKind.GetInfo:
                    if ((x.Info == ExcelInfoKind.LastRow || x.Info == ExcelInfoKind.LastColumn) && (problem = cellProblem(x.Cell, x.CellIsExpression)) != null) return problem;
                    break;
                case ExcelKind.RenameSheet:
                    if ((problem = badPattern(x.SheetName)) != null) return problem;
                    if (x.Value.Trim().Length == 0) return "Enter the new name of the sheet.";
                    if (!x.ValueIsExpression && (x.Value.Trim().Length > 31 || x.Value.IndexOfAny(new[] { ':', '\\', '/', '?', '*', '[', ']' }) >= 0))
                        return "A sheet name has at most 31 characters and none of : \\ / ? * [ ].";
                    break;
                case ExcelKind.Select:
                    if ((problem = cellProblem(x.Cell, x.CellIsExpression)) != null) return problem;
                    break;
                case ExcelKind.InsertCells:
                case ExcelKind.DeleteCells:
                    if ((problem = cellProblem(x.Cell, x.CellIsExpression)) != null) return problem;
                    break;
                case ExcelKind.Clear:
                    if (x.CellIsExpression) Expression.CheckSyntax(x.Cell);
                    else if ((problem = AutomationTest.Core.Excel.ExcelAddress.Check(x.Cell, "area")) != null) return problem;
                    break;
                case ExcelKind.Copy:
                    if (x.CellIsExpression) Expression.CheckSyntax(x.Cell);
                    else if ((problem = AutomationTest.Core.Excel.ExcelAddress.Check(x.Cell, "area to copy")) != null) return problem;
                    if (x.Value.Trim().Length == 0) return "Enter the destination cell.";
                    if (!x.ValueIsExpression && (problem = AutomationTest.Core.Excel.ExcelAddress.Check(x.Value, "destination cell")) != null) return problem;
                    break;
                case ExcelKind.Format:
                    if (x.CellIsExpression) Expression.CheckSyntax(x.Cell);
                    else if ((problem = AutomationTest.Core.Excel.ExcelAddress.Check(x.Cell, "area")) != null) return problem;
                    if (x.NumberFormat.Length == 0 && x.Bold == ExcelFlag.Leave && x.Italic == ExcelFlag.Leave && x.FontColor.Length == 0 && x.FillColor.Length == 0 && x.ColumnWidth.Length == 0)
                        return "Nothing to change: fill in a number format, bold, italic, a colour or a column width.";
                    if (x.FontColor.Length > 0 && !AutomationTest.Core.Excel.ExcelColors.IsValid(x.FontColor)) return "Write the font colour as #RRGGBB, for example #FF0000.";
                    if (x.FillColor.Length > 0 && !AutomationTest.Core.Excel.ExcelColors.IsValid(x.FillColor)) return "Write the fill colour as #RRGGBB, for example #FFFF00.";
                    if (x.ColumnWidth.Length > 0 && !string.Equals(x.ColumnWidth, "auto", StringComparison.OrdinalIgnoreCase)
                        && !(double.TryParse(x.ColumnWidth, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double width) && width >= 0 && width <= 255))
                        return "The column width is a number from 0 to 255, or auto.";
                    break;
                case ExcelKind.Sort:
                    if (x.CellIsExpression) Expression.CheckSyntax(x.Cell);
                    else if ((problem = AutomationTest.Core.Excel.ExcelAddress.Check(x.Cell, "area")) != null) return problem;
                    if (!System.Text.RegularExpressions.Regex.IsMatch(x.SortBy.TrimStart('$'), "^[A-Za-z]{1,3}$")) return "Sort by: enter the column letters, for example C.";
                    break;
                case ExcelKind.Filter:
                    if (!x.ClearFilter)
                    {
                        if (x.CellIsExpression) Expression.CheckSyntax(x.Cell);
                        else if ((problem = AutomationTest.Core.Excel.ExcelAddress.Check(x.Cell, "area (with its header row)")) != null) return problem;
                        if (x.Criteria.Length == 0) return "Enter the criterion, for example >5 or =Hanoi.";
                    }
                    break;
                case ExcelKind.RunMacro:
                    if (x.Macro.Length == 0) return "Enter the name of the macro, for example Module1.Report.";
                    break;
                case ExcelKind.GetRange:
                    if (x.CellIsExpression) Expression.CheckSyntax(x.Cell);
                    else if ((problem = AutomationTest.Core.Excel.ExcelAddress.Check(x.Cell, "area")) != null) return problem;
                    if (x.Get != ExcelGetKind.Value && x.Get != ExcelGetKind.Text && x.Get != ExcelGetKind.Formula)
                        return "Get Range reads the Value, the Text or the Formula of the cells (not " + x.Get + ").";
                    break;
                case ExcelKind.SetRange:
                    if ((problem = cellProblem(x.Cell, x.CellIsExpression)) != null) return problem;
                    if (x.Value.Trim().Length == 0) return "Enter the table variable to write, for example {t} (read with Get Range).";
                    Expression.CheckSyntax(x.Value);
                    break;
                case ExcelKind.Find:
                    if (x.Value.Length == 0) return "Enter the text to find.";
                    if (x.Cell.Length > 0)
                    {
                        if (x.CellIsExpression) Expression.CheckSyntax(x.Cell);
                        else if ((problem = AutomationTest.Core.Excel.ExcelAddress.Check(x.Cell, "area")) != null) return problem;
                    }
                    break;
                case ExcelKind.Evaluate:
                    if (x.Value.Trim().Length == 0) return "Enter the formula to calculate, for example SUM(A1:A10).";
                    break;
                case ExcelKind.Save:
                    if (x.SaveAs)
                    {
                        if (x.SavePath.Length == 0) return "Enter the path of the new file.";
                        string extension = System.IO.Path.GetExtension(x.SavePath).ToLowerInvariant();
                        if (extension != ".xlsx" && extension != ".xlsm" && extension != ".xlsb" && extension != ".xls" && extension != ".csv" && extension != ".pdf")
                            return "The new file must end with .xlsx, .xlsm, .xlsb, .xls, .csv or .pdf.";
                    }
                    break;
            }
            if (ScriptRunner.ExcelValueKind(x.Kind) && x.ValueIsExpression) Expression.CheckSyntax(x.Value);
            bool needsId = x.Kind == ExcelKind.GetCell || x.Kind == ExcelKind.GetRange || x.Kind == ExcelKind.GetInfo;
            bool hasId = x.Kind == ExcelKind.Find || x.Kind == ExcelKind.Evaluate || x.Kind == ExcelKind.RunMacro || needsId;
            if (hasId && (needsId || x.Id.Length > 0) && !Expression.IsValidId(x.Id))
                return needsId ? "Enter an ID for the value: letters, digits and _, not starting with a digit (and not true, false, now, today)."
                               : "The ID may have letters, digits and _, not starting with a digit (and not true, false, now, today); or leave it empty.";
            return null;
        }

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

                    case ActionCategory.Excel:
                        return ValidateExcel(node.Excel);

                    case ActionCategory.Web:
                        var web = node.Web;
                        if (web.Kind == WebKind.SwitchTab)
                        {
                            foreach (var pattern in new[] { new[] { "Title", web.Title }, new[] { "URL", web.Url } })
                            {
                                if (pattern[1].Trim().Length == 0) continue;
                                try { new System.Text.RegularExpressions.Regex(pattern[1]); }
                                catch (ArgumentException ex) { return "The " + pattern[0] + " is not a valid regular expression: " + ex.Message; }
                            }
                        }
                        if (web.Kind == WebKind.GetText && !Expression.IsValidId(web.Id))
                            return "Enter an ID with letters, digits and _, not starting with a digit (and not true, false, now, today).";
                        if (web.Kind == WebKind.Script)
                        {
                            if (web.Script.Trim().Length == 0) return "Enter the JavaScript to run.";
                            if (web.Id.Length > 0 && !Expression.IsValidId(web.Id))
                                return "The ID may have letters, digits and _, not starting with a digit (and not true, false, now, today); or leave it empty.";
                            if (web.TextIsExpression) Expression.CheckSyntax(web.Script);
                        }
                        if (web.Kind == WebKind.GetText && web.Get == WebGetKind.Attribute && web.Attribute.Length == 0) return "Enter the name of the attribute to read (href, src, data-id...).";
                        bool pageGet = web.Kind == WebKind.GetText && (web.Get == WebGetKind.PageUrl || web.Get == WebGetKind.PageTitle);
                        bool waitsElement = web.Kind == WebKind.WaitElement && (web.Until == WebWaitKind.ElementAppears || web.Until == WebWaitKind.ElementDisappears);
                        if ((waitsElement || web.Kind == WebKind.Input || web.Kind == WebKind.Click || web.Kind == WebKind.Hover || web.Kind == WebKind.Drag
                            || web.Kind == WebKind.SelectOption || web.Kind == WebKind.Upload || (web.Kind == WebKind.GetText && !pageGet)) && web.Selector.Length == 0)
                            return "Enter a CSS selector (for example #email), or text=Label to find an element by its text.";
                        if (web.Kind == WebKind.Drag && web.TargetSelector.Length == 0) return "Enter the selector of the element to drop on.";
                        if (ScriptRunner.WebTextKind(web) && web.TextIsExpression) Expression.CheckSyntax(web.Text);
                        if (ScriptRunner.WebTextKind(web) && !web.TextIsExpression && web.Kind != WebKind.Input && web.Kind != WebKind.Dialog && web.Text.Trim().Length == 0)
                        {
                            switch (web.Kind)
                            {
                                case WebKind.PressKey: return "Enter the key to press, for example Enter, Tab, Ctrl+A.";
                                case WebKind.SelectOption: return "Enter the option to choose (its text or its value).";
                                case WebKind.Upload: return "Enter the path of the file to upload.";
                                case WebKind.Screenshot: return "Enter the file to save the picture in (.png or .jpg).";
                                default: return "Enter the pattern to wait for (a regular expression).";
                            }
                        }
                        if (web.Kind == WebKind.PressKey && !web.TextIsExpression)
                        {
                            string keyProblem = AutomationTest.Core.Web.WebKeys.Parse(web.Text, out int keyModifiers, out var keyDefinition);
                            if (keyProblem != null) return keyProblem;
                        }
                        if (web.Kind == WebKind.Screenshot && !web.TextIsExpression)
                        {
                            string shotExtension = System.IO.Path.GetExtension(web.Text.Trim()).ToLowerInvariant();
                            if (shotExtension != ".png" && shotExtension != ".jpg" && shotExtension != ".jpeg") return "The picture file must end with .png, .jpg or .jpeg.";
                        }
                        if (web.Kind == WebKind.WaitElement && !waitsElement && !web.TextIsExpression)
                        {
                            try { new System.Text.RegularExpressions.Regex(web.Text); }
                            catch (ArgumentException ex) { return "The pattern is not a valid regular expression: " + ex.Message; }
                        }
                        break;

                    case ActionCategory.Control:
                        if (node.IsKind(ControlKind.Loop) && node.Loop.Mode == LoopType.For)
                        {
                            var lf = node.Loop;
                            if (!Expression.IsValidId(lf.ForId))
                                return "Enter an ID for the counter: letters, digits and _, not starting with a digit (and not true, false, now, today).";
                            Expression.CheckSyntax(lf.ForFrom);
                            Expression.CheckSyntax(lf.ForTo);
                            Expression.CheckSyntax(lf.ForStep);
                            if (double.TryParse(lf.ForStep, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double stepNumber) && stepNumber == 0)
                                return "Step cannot be 0.";
                        }
                        else if (node.IsKind(ControlKind.Image))
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

        /// <summary>Up to Top / Down to End: moves the selected actions (keeping their order) to the start or the end of their list. An else stays last.</summary>
        private void MoveToEdge(bool top)
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return;
            if (!SameList(selected)) { SetStatus("Select actions that are in the same list to move them."); return; }
            if (selected.Any(r => r.Node.IsKind(ControlKind.Else))) { SetStatus("The else line always stays last."); return; }

            var list = selected[0].Siblings;
            var picked = new HashSet<ActionNode>(selected.Select(r => r.Node));
            var moving = list.Where(picked.Contains).ToList();
            var rest = list.Where(n => !picked.Contains(n)).ToList();
            var last = rest.LastOrDefault(n => n.IsKind(ControlKind.Else)); // the else of a Condition stays at the end
            if (last != null) rest.Remove(last);
            var order = top ? moving.Concat(rest).ToList() : rest.Concat(moving).ToList();
            if (last != null) order.Add(last);
            list.Clear();
            list.AddRange(order);
            RefreshList(picked);
        }

        /// <summary>The colour of every icon in the menu rows.</summary>
        private void ApplyIconColors()
        {
            var green = Color.FromArgb(22, 163, 74);
            var blue = Color.FromArgb(37, 99, 235);
            var indigo = Color.FromArgb(79, 70, 229);
            _btnNew.BaseColor = blue;
            _btnOpen.BaseColor = Color.FromArgb(217, 119, 6);
            _btnSave.BaseColor = Color.FromArgb(13, 148, 136);
            _saveAs.BaseColor = Color.FromArgb(14, 116, 144);
            _collapseAll.BaseColor = _expandAll.BaseColor = Color.FromArgb(124, 58, 237);
            _startStop.BaseColor = green;
            _startSelected.BaseColor = Color.FromArgb(5, 150, 105);
            _record.BaseColor = Color.FromArgb(220, 38, 38);
            _license.BaseColor = Color.FromArgb(202, 138, 4);
            _settingsButton.BaseColor = Color.FromArgb(71, 85, 105);
            _btnAdd.BaseColor = green;
            _btnUpdate.BaseColor = Color.FromArgb(234, 88, 12);
            _btnDelete.BaseColor = Color.FromArgb(220, 38, 38);
            _btnIn.BaseColor = _btnOut.BaseColor = blue;
            _btnUp.BaseColor = _btnTop.BaseColor = _btnDown.BaseColor = _btnEnd.BaseColor = indigo;
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
            BrowserInheritance.Propagate(_script.Actions); // actions inside a New Tab or Switch Tab take over its browser
            _rows.Clear();
            if (wanted.Count > 0) ExpandTo(_script.Actions, wanted);
            Flatten(_script.Actions, null, 0);

            int top = _list.TopItem != null ? _list.TopItem.Index : 0; // keep the scroll position: a long list must not jump back to the top
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
            if (top > 0 && _list.Items.Count > 0) _list.TopItem = _list.Items[Math.Min(top, _list.Items.Count - 1)];
            if (last >= 0 && !FullyVisible(last)) _list.EnsureVisible(last); // scroll only when what was selected is out of sight
            UpdateButtonStates();
        }

        private readonly ToolStripLabel _centerSpacer = new ToolStripLabel { AutoSize = false, Width = 0, Text = "" };

        /// <summary>Puts Start, Start Selected and Record in the middle of the left bar, so the gap to the file buttons equals the gap to the action buttons.</summary>
        private void CenterRunButtons(ToolStrip tools)
        {
            int left = 0, group = 0;
            bool after = false;
            foreach (ToolStripItem item in tools.Items)
            {
                if (item == _centerSpacer) { after = true; continue; }
                if (!item.Available) continue; // hidden items (Save As before a file exists) take no room
                int width = item.Width + item.Margin.Horizontal;
                if (after) group += width; else left += width;
            }
            int wanted = Math.Max(0, (tools.DisplayRectangle.Width - left - group) / 2); // equal gaps: after the file buttons and before the action buttons
            if (_centerSpacer.Width != wanted)
            {
                _centerSpacer.Width = wanted;
                tools.PerformLayout();
            }
        }

        /// <summary>Whether the list row is completely inside the visible part of the list.</summary>
        private bool FullyVisible(int index)
        {
            if (index < 0 || index >= _list.Items.Count || _list.TopItem == null) return false;
            var bounds = _list.Items[index].GetBounds(ItemBoundsPortion.Entire);
            return bounds.Top >= _list.TopItem.Bounds.Top && bounds.Bottom <= _list.ClientRectangle.Height;
        }

        private void RefreshList()
        {
            RefreshList(null);
        }

        private void Flatten(List<ActionNode> nodes, ActionNode parent, int depth, bool parentDisabled = false)
        {
            foreach (var node in nodes)
            {
                bool has = node.Children.Count > 0;
                bool collapsed = has && _collapsed.Contains(node);
                bool disabled = parentDisabled || node.Disabled;
                _rows.Add(new Row { Node = node, Parent = parent, Siblings = nodes, Depth = depth, HasChildren = has, Collapsed = collapsed, Disabled = disabled });
                if (collapsed) continue; // children (and a Condition's End line) are hidden
                Flatten(node.Children, node, depth + 1, disabled);
                if (node.IsKind(ControlKind.Condition))
                    _rows.Add(new Row { Node = node, Parent = parent, Siblings = nodes, Depth = depth, IsEnd = true, Disabled = disabled });
            }
        }

        /// <summary>Un-collapses every ancestor of the wanted nodes so they are visible. True when any wanted node is in the list.</summary>
        private bool ExpandTo(List<ActionNode> nodes, HashSet<ActionNode> wanted)
        {
            bool any = false;
            foreach (var node in nodes)
            {
                if (ExpandTo(node.Children, wanted)) { _collapsed.Remove(node); any = true; }
                if (wanted.Contains(node)) any = true;
            }
            return any;
        }

        private void SetCollapsed(Row row, bool collapse)
        {
            if (row == null || row.IsEnd || !row.HasChildren || row.Collapsed == collapse) return;
            if (collapse) _collapsed.Add(row.Node); else _collapsed.Remove(row.Node);
            RefreshList(new[] { row.Node });
        }

        private void CollapseAll(bool collapse)
        {
            _collapsed.Clear();
            if (collapse) AddBranches(_script.Actions);
            RefreshList();
        }

        private void AddBranches(List<ActionNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Count == 0) continue;
                _collapsed.Add(node);
                AddBranches(node.Children);
            }
        }

        /// <summary>Clicking the arrow in front of a name collapses or expands that action.</summary>
        private void ListMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            var hit = _list.HitTest(e.Location);
            if (hit.Item == null) return;
            int index = hit.Item.Index;
            if (index < 0 || index >= _rows.Count || !_rows[index].HasChildren) return;
            var row = _rows[index];
            var label = hit.Item.GetBounds(ItemBoundsPortion.Label);
            var flags = TextRenderer_NoPadding;
            string indent = new string(' ', row.Depth * 4);
            int left = label.Left + System.Windows.Forms.TextRenderer.MeasureText(indent + "|", _list.Font, Size.Empty, flags).Width
                - System.Windows.Forms.TextRenderer.MeasureText("|", _list.Font, Size.Empty, flags).Width;
            int width = System.Windows.Forms.TextRenderer.MeasureText(row.Collapsed ? CollapsedMark : ExpandedMark, _list.Font, Size.Empty, flags).Width;
            if (e.X >= left - 2 && e.X <= left + width + 2) SetCollapsed(row, !row.Collapsed);
        }

        private const TextFormatFlags TextRenderer_NoPadding = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        /// <summary>
        /// Keeps the Details column in step with the width of the list when the window is resized.
        /// Whether the list scrolled sideways just before this resize is worked out from the widths themselves (the columns against
        /// the previous width of the list), not from events, so quick or continuous resizing cannot leave it wrong:
        /// - it did not scroll: Details grows or shrinks (down to a minimum) so that no scroll bar appears;
        /// - it scrolled and still would: the columns stay exactly as they are;
        /// - it scrolled but there is room now: Details takes the free room, and from then on follows the window.
        /// </summary>
        private void FitDetailsColumn(bool force)
        {
            const int MinimumDetails = MinColumnWidth;
            int client = _list.ClientSize.Width;
            if (client <= 0) return; // hidden or minimized: nothing to fit, and the last width is kept
            bool scrolledBefore = ColumnsWidth() > _lastListWidth && _lastListWidth > 0;
            _lastListWidth = client;
            if (!force && scrolledBefore && ColumnsWidth() > client) return;
            int others = ColumnsWidth() - _list.Columns[DetailsColumn].Width;
            // A little room is always left over, because the list is not exactly as wide as it looks (its border and rounding); filling
            // the list to the pixel can bring up a sideways scroll bar. (ClientSize already leaves out a vertical scroll bar.)
            int spare = 8;
            _list.Columns[DetailsColumn].Width = Math.Max(MinimumDetails, client - others - spare);
            // The list may have shown its sideways scroll bar while the window was already smaller than the old column widths, and
            // it does not take it away again by itself once the columns fit: hide it when it is not needed.
            if (ColumnsWidth() <= client) ShowScrollBar(_list.Handle, 0 /* SB_HORZ */, false);
        }

        private int ColumnsWidth()
        {
            int total = 0;
            foreach (ColumnHeader column in _list.Columns) total += column.Width;
            return total;
        }

        private int _lastListWidth;
        private bool _columnsReady, _columnsRestored; // ready: the window has been shown; restored: the widths came from Setting.ini
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr hWnd, int bar, bool show);


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

                case ActionCategory.Excel:
                    var xd = n.Excel;
                    action = xd.Kind.ToString();
                    switch (xd.Kind)
                    {
                        case ExcelKind.Workbook:
                            details = xd.Path.Length > 0 ? xd.Path : xd.Name.Length > 0 ? "open workbook /" + xd.Name + "/" : "(new workbook)";
                            if (xd.ReadOnly && xd.Path.Length > 0) details += " (read only)";
                            break;
                        case ExcelKind.SelectSheet: details = "sheet " + (xd.SheetName.Length > 0 ? "/" + xd.SheetName + "/ " : "") + "#" + Math.Max(1, xd.Index); break;
                        case ExcelKind.CreateSheet: details = "new sheet " + (xd.SheetName.Length > 0 ? "\"" + xd.SheetName + "\" " : "") + "(" + xd.Position + ")"; break;
                        case ExcelKind.DeleteSheet: details = xd.SheetName.Length == 0 && xd.Index <= 0 ? "the current sheet" : "sheet " + (xd.SheetName.Length > 0 ? "/" + xd.SheetName + "/ " : "") + "#" + Math.Max(1, xd.Index); break;
                        case ExcelKind.GetCell: details = xd.Cell + " " + xd.Get + " -> " + xd.Id; break;
                        case ExcelKind.SetCell: details = xd.Cell + " = " + xd.Value + (xd.As != ExcelSetKind.Auto ? " (" + xd.As + ")" : ""); break;
                        case ExcelKind.RenameSheet: details = (xd.SheetName.Length == 0 && xd.Index <= 0 ? "this sheet" : "sheet " + (xd.SheetName.Length > 0 ? "/" + xd.SheetName + "/ " : "") + "#" + Math.Max(1, xd.Index)) + " -> " + xd.Value; break;
                        case ExcelKind.Select: details = "select " + xd.Cell; break;
                        case ExcelKind.InsertCells: details = "insert " + xd.Count + " " + xd.Target.ToString().ToLowerInvariant() + " at " + xd.Cell; break;
                        case ExcelKind.DeleteCells: details = "delete " + xd.Count + " " + xd.Target.ToString().ToLowerInvariant() + " at " + xd.Cell; break;
                        case ExcelKind.Clear: details = "clear " + xd.ClearWhat.ToString().ToLowerInvariant() + " of " + xd.Cell; break;
                        case ExcelKind.Copy: details = xd.Cell + " -> " + xd.Value + (xd.ValuesOnly ? " (values only)" : ""); break;
                        case ExcelKind.Format:
                            details = xd.Cell + ": " + string.Join(", ", new[]
                            {
                                xd.NumberFormat.Length > 0 ? "format " + xd.NumberFormat : null,
                                xd.Bold != ExcelFlag.Leave ? "bold " + xd.Bold : null,
                                xd.Italic != ExcelFlag.Leave ? "italic " + xd.Italic : null,
                                xd.FontColor.Length > 0 ? "font " + xd.FontColor : null,
                                xd.FillColor.Length > 0 ? "fill " + xd.FillColor : null,
                                xd.ColumnWidth.Length > 0 ? "width " + xd.ColumnWidth : null
                            }.Where(p => p != null));
                            break;
                        case ExcelKind.Sort: details = xd.Cell + " by " + xd.SortBy + (xd.Descending ? " descending" : " ascending") + (xd.HasHeader ? " (header)" : ""); break;
                        case ExcelKind.Filter: details = xd.ClearFilter ? "remove the filter" : xd.Cell + " column " + xd.FilterField + " " + xd.Criteria; break;
                        case ExcelKind.Refresh: details = "refresh data connections and pivot tables"; break;
                        case ExcelKind.RunMacro: details = xd.Macro + (xd.Value.Length > 0 ? "(" + xd.Value + ")" : "") + (xd.Id.Length > 0 ? " -> " + xd.Id : ""); break;
                        case ExcelKind.GetRange: details = xd.Cell + " " + xd.Get + " -> table " + xd.Id; break;
                        case ExcelKind.SetRange: details = xd.Value + " -> " + xd.Cell + (xd.As != ExcelSetKind.Auto ? " (" + xd.As + ")" : ""); break;
                        case ExcelKind.Find: details = "find \"" + xd.Value + "\"" + (xd.Cell.Length > 0 ? " in " + xd.Cell : "") + (xd.Id.Length > 0 ? " -> " + xd.Id : ""); break;
                        case ExcelKind.GetInfo: details = xd.Info + (xd.Info == ExcelInfoKind.LastRow || xd.Info == ExcelInfoKind.LastColumn ? " of " + xd.Cell : "") + " -> " + xd.Id; break;
                        case ExcelKind.Evaluate: details = xd.Value + (xd.Id.Length > 0 ? " -> " + xd.Id : ""); break;
                        case ExcelKind.Save: details = xd.SaveAs ? "save as " + xd.SavePath : "save"; break;
                        case ExcelKind.CloseWorkbook: details = "close the workbook" + (xd.SaveChanges ? " (save changes)" : " (drop changes)"); break;
                        case ExcelKind.CloseExcel: details = "quit Excel" + (xd.SaveChanges ? " (save changes)" : " (drop changes)"); break;
                    }
                    if (xd.Kind == ExcelKind.Workbook || xd.Kind == ExcelKind.CloseExcel) time = TimeBox.Format(xd.TimeMs);
                    delay = TimeBox.Format(xd.DelayMs);
                    break;

                case ActionCategory.Web:
                    var wb = n.Web;
                    action = wb.Kind.ToString();
                    process = wb.Browser.ToString(); // the Process column tells the browser
                    switch (wb.Kind)
                    {
                        case WebKind.NewTab: details = wb.Url.Length == 0 ? "(start page)" : wb.Url; break;
                        case WebKind.Input: details = wb.Selector + " <- " + (wb.TextIsExpression ? wb.Text : "\"" + wb.Text + "\""); break;
                        case WebKind.Dialog: details = (wb.Accept ? "OK" : "Cancel") + (wb.Accept && wb.Text.Length > 0 ? " \"" + wb.Text + "\"" : ""); break;
                        case WebKind.GetText:
                            details = (wb.Get == WebGetKind.PageUrl || wb.Get == WebGetKind.PageTitle ? "" : wb.Selector + (wb.Index > 1 ? " #" + wb.Index : "") + " ")
                                + wb.Get + (wb.Get == WebGetKind.Attribute ? " " + wb.Attribute : "") + " -> " + wb.Id + (wb.Required || wb.Get == WebGetKind.Count ? "" : " (optional)");
                            break;
                        case WebKind.Hover: details = "hover " + wb.Selector + (wb.Index > 1 ? " #" + wb.Index : ""); break;
                        case WebKind.Drag: details = wb.Selector + " -> " + wb.TargetSelector; break;
                        case WebKind.SelectOption: details = wb.Selector + " = " + (wb.TextIsExpression ? wb.Text : "\"" + wb.Text + "\""); break;
                        case WebKind.PressKey: details = (wb.Selector.Length > 0 ? wb.Selector + ": " : "") + "press " + wb.Text; break;
                        case WebKind.Upload: details = wb.Selector + " <- " + wb.Text; break;
                        case WebKind.Screenshot: details = (wb.Selector.Length > 0 ? wb.Selector : wb.FullPage ? "full page" : "page") + " -> " + wb.Text; break;
                        case WebKind.WaitElement:
                            details = wb.Until == WebWaitKind.ElementAppears ? wb.Selector
                                : wb.Until == WebWaitKind.ElementDisappears ? wb.Selector + " disappears"
                                : (wb.Until == WebWaitKind.UrlMatches ? "address" : wb.Until == WebWaitKind.TitleMatches ? "title" : "page text") + " matches " + wb.Text;
                            break;
                        case WebKind.CloseTab: details = "close the tab the script is using"; break;
                        case WebKind.CloseBrowser: details = "close all " + wb.Browser + " windows"; break;
                        case WebKind.Script:
                            details = (wb.TextIsExpression ? "(expression) " : "") + (wb.Script.Replace("\r", " ").Replace("\n", " ").Trim()) + (wb.Id.Length > 0 ? " -> " + wb.Id : "");
                            break;
                        case WebKind.SwitchTab:
                            details = "tab #" + Math.Max(1, wb.Index) + (wb.Title.Length > 0 || wb.Url.Length > 0 ? " of the tabs with " : " of the window");
                            details += string.Join(" and ", new[]
                            {
                                wb.Title.Length > 0 ? "title /" + wb.Title + "/" : null,
                                wb.Url.Length > 0 ? "address /" + wb.Url + "/" : null
                            }.Where(x => x != null));
                            break;
                        default: details = wb.Selector; break;
                    }
                    if (wb.Frame.Length > 0 && wb.Kind != WebKind.NewTab && wb.Kind != WebKind.SwitchTab && wb.Kind != WebKind.Script && wb.Kind != WebKind.CloseTab && wb.Kind != WebKind.CloseBrowser) details = "[" + wb.Frame + "] " + details;
                    if (wb.Kind != WebKind.SwitchTab && wb.Kind != WebKind.GetText && wb.Index > 1) details += " #" + wb.Index;
                    if (wb.Kind != WebKind.CloseTab && wb.Kind != WebKind.CloseBrowser) time = TimeBox.Format(wb.TimeMs);
                    delay = TimeBox.Format(wb.DelayMs);
                    break;

                case ActionCategory.Utilities:
                    action = n.Command.Kind.ToString();
                    if (n.Command.Kind == CommandKind.Shutdown)
                    {
                        details = "shut down the computer" + (n.Command.Force ? " (force)" : "");
                        time = TimeBox.Format(n.Command.TimeMs);
                    }
                    else
                    {
                        details = n.Command.Kind == CommandKind.Active ? "sends a harmless input event so the computer does not lock or sleep" : n.Command.Value;
                        delay = TimeBox.Format(n.Command.DelayMs);
                    }
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
                                case LoopType.For: details = "for " + n.Loop.ForId + " = " + n.Loop.ForFrom + " to " + n.Loop.ForTo + (n.Loop.ForStep.Trim() == "1" ? "" : " step " + n.Loop.ForStep); break;
                                default: details = n.Loop.Count == 0 ? "forever" : n.Loop.Count + " time(s)"; break;
                            }
                            // The time one pass over the actions inside takes (their Time and Delay added up).
                            long? pass = ActionTiming.PerIterationMs(n);
                            time = !pass.HasValue ? "?" : n.Children.Exists(c => !c.Disabled) ? TimeBox.FormatApprox(pass.Value) : "";
                            break;
                    }
                    break;
            }
            // Name column: the user's alias, or the action type when no alias is set; indented by tree depth.
            if (name == null) name = string.IsNullOrWhiteSpace(n.Name) ? action : n.Name;
            string mark = row.IsEnd || !row.HasChildren ? LeafMark : row.Collapsed ? CollapsedMark : ExpandedMark;
            var item = new ListViewItem(indent + mark + name);
            item.SubItems.Add(action);
            item.SubItems.Add(process);
            item.SubItems.Add(details);
            item.SubItems.Add(time);
            item.SubItems.Add(delay);
            if (row.Disabled) item.ForeColor = SystemColors.GrayText; // disabled itself or through a parent
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
        private bool RunOverlay(out Point point, out WindowInfo? window, bool highlightWindows = false)
        {
            HideToTray("picking");
            Application.DoEvents();
            Thread.Sleep(250); // let the minimize animation finish so the overlay is not captured behind it
            Point? picked = null;
            using (var picker = new PointPickerForm { HighlightWindows = highlightWindows })
            {
                if (picker.ShowDialog() == DialogResult.OK) picked = picker.SelectedPoint;
            }
            Thread.Sleep(150); // let the overlay disappear so WindowFromPoint sees the window underneath
            // Resolve the clicked window now, before this form is restored on top of it.
            window = picked.HasValue ? WindowLocator.WindowAt(picked.Value.X, picked.Value.Y) : null;
            ShowFromTray();
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
        /// Lets the user click a point on a frozen picture of the screen. Without a process it is a desktop position; with one,
        /// the program is brought to the front, the picture is taken when the user presses the Pick key (so the program keeps the look
        /// it has; clicking Pick takes the focus away and many programs shrink or close pop-ups then), and the point is stored
        /// as an offset from the anchor of its client area as it was when the picture was taken. Any point on the picture can be
        /// chosen, also outside the window (a pop-up, or room the window will have once it grows).
        /// </summary>
        private bool PickPoint(WindowTarget process, WindowAnchor anchor, out int x, out int y)
        {
            x = y = 0;
            if (!process.IsEmpty && !ActivateProcess(process)) return false;
            Minimize();
            if (!process.IsEmpty && _settings.AdvancedPositionPicker && !WaitForFreeze("Pick a position"))
            {
                Restore();
                SetStatus("Pick cancelled.");
                return false;
            }
            if (!Freeze(process, out _, out Bitmap frozen, out ClientArea? area, out _, out _, process.IsEmpty || !_settings.AdvancedPositionPicker))
            {
                Restore();
                return false;
            }

            Point? picked = null;
            using (frozen)
            using (var picker = new PointPickerForm(frozen))
            {
                if (area.HasValue)
                {
                    var a = area.Value;
                    picker.AcceptArea = new Rectangle(a.Left, a.Top, a.Width, a.Height);
                    picker.CoordinateLabel = TargetLabel(process) + " " + anchor;
                    picker.Relative = p => { WindowLocator.FromScreen(a, anchor, p.X, p.Y, out int rx, out int ry); return new Point(rx, ry); };
                }
                if (picker.ShowDialog() == DialogResult.OK) picked = picker.SelectedPoint;
            }
            Thread.Sleep(150); // let the overlay disappear
            Restore();
            if (!picked.HasValue) return false;

            x = picked.Value.X;
            y = picked.Value.Y;
            if (area.HasValue) WindowLocator.FromScreen(area.Value, anchor, x, y, out x, out y);
            return true;
        }

        /// <summary>
        /// Waits, with this window out of the way, until the Pick key is pressed or "Freeze now" is clicked (neither takes the focus away
        /// from the program being picked); Esc or Cancel gives up. False when cancelled.
        /// </summary>
        private bool WaitForFreeze(string what, string instruction = null)
        {
            _freezeSignal = 0;
            SetToggleHotKey(false); // the Start, Start Selected and Record keys must not start anything meanwhile
            bool pickKey = HotKeyNative.RegisterHotKey(Handle, HotKeyFreezeId, HotKeyNative.MOD_NOREPEAT, (uint)_settings.PickCode);
            HotKeyNative.RegisterHotKey(Handle, HotKeyFreezeCancelId, HotKeyNative.MOD_NOREPEAT, (uint)Keys.Escape);
            var hint = new FreezeHintForm(instruction ?? (what + ": get the program into the state you want to pick on, then press " + _settings.Pick + " to take the picture."), pickKey, _settings.Pick);
            hint.FreezeClicked += () => _freezeSignal = 1;
            hint.CancelClicked += () => _freezeSignal = 2;
            try
            {
                hint.ShowAtTop();
                while (_freezeSignal == 0 && !IsDisposed)
                {
                    Application.DoEvents();
                    Thread.Sleep(15);
                }
            }
            finally
            {
                HotKeyNative.UnregisterHotKey(Handle, HotKeyFreezeId);
                HotKeyNative.UnregisterHotKey(Handle, HotKeyFreezeCancelId);
                hint.Dispose();
                SetToggleHotKey(true);
            }
            Application.DoEvents();
            Thread.Sleep(120); // let the bar disappear so it is not in the picture
            return _freezeSignal == 1;
        }

        private int _freezeSignal;

        private static string TargetLabel(WindowTarget process)
        {
            return string.IsNullOrWhiteSpace(process.ProcessName) ? "window" : process.ProcessName.Trim();
        }

        /// <summary>
        /// Takes one picture of the whole screen now (this window must already be minimized) and, when a process is given,
        /// notes where its window's client area is at that very moment. Selecting on the picture then matches what was
        /// captured even when the screen changes afterwards (pop-ups closing, a window shrinking when it loses focus).
        /// </summary>
        /// <param name="settle">Move the cursor to a corner first (so no control under it shows its hover look) and wait a moment.
        /// Not wanted when the program was set up by hand: moving the mouse could change it.</param>
        private bool Freeze(WindowTarget process, out RawImage snapshot, out Bitmap frozen, out ClientArea? area, out int left, out int top, bool settle = true)
        {
            snapshot = null;
            frozen = null;
            area = null;
            ScreenCapture.GetVirtualScreen(out left, out top, out int width, out int height);

            IntPtr hwnd = IntPtr.Zero;
            if (!process.IsEmpty)
            {
                try { hwnd = WindowLocator.Find(process); }
                catch (ArgumentException ex) { SetStatus(ex.Message); return false; }
                if (hwnd == IntPtr.Zero) { SetStatus("The program is no longer available."); return false; }
            }

            var cursor = Cursor.Position;
            if (settle)
            {
                Cursor.Position = new Point(left + width - 1, top + height - 1);
                Thread.Sleep(250);
            }
            try
            {
                if (hwnd != IntPtr.Zero) area = WindowLocator.GetClientArea(hwnd); // read right before the picture, so both agree
                snapshot = ScreenCapture.Capture(left, top, width, height);
            }
            catch (InvalidOperationException ex)
            {
                if (settle) Cursor.Position = cursor;
                SetStatus(ex.Message);
                return false;
            }
            if (settle) Cursor.Position = cursor;
            frozen = ToBitmap(snapshot);
            return true;
        }

        /// <summary>Lets the user click any window to fill in a process name box.</summary>
        private void PickProcess(TextBox nameBox, TextBox titleBox)
        {
            if (!RunOverlay(out Point p, out WindowInfo? info, true) || !info.HasValue) return;
            if (WindowLocator.IsDesktopSurface(info.Value.Handle))
            {
                nameBox.Text = "";
                titleBox.Text = "";
                SetStatus("Full desktop (no process): the desktop, taskbar and Start menu are not a program, so Process is left empty.");
                return;
            }
            if (string.Equals(info.Value.ProcessName, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("That is this program's own window; pick another window.");
                return;
            }
            nameBox.Text = info.Value.ProcessName;
            // The window's own title is kept too, so a program with several windows (or a dialog of it) is told apart. It is
            // an exact-match pattern; loosen it (for example "Notepad$") when the title changes, such as a file name.
            string title = (info.Value.Title ?? "").Trim();
            titleBox.Text = title.Length == 0 ? "" : "^" + System.Text.RegularExpressions.Regex.Escape(title) + "$";
            SetStatus("Process: " + info.Value.ProcessName + (title.Length == 0 ? "" : "  title: \"" + title + "\" (saved as an exact pattern in Title; edit it if the title changes)"));
        }

        /// <summary>Lets the user point at an element in the browser; the selector (and iframe) it gets fills the form.</summary>
        private void PickWebElement()
        {
            var browser = (BrowserKind)_webBrowser.SelectedItem;
            if (!AutomationTest.Core.Web.WebSession.Shared.IsConnected(browser))
            {
                SetStatus(browser + " is not connected. Start " + browser + " (with the Automation Test extension) first.");
                return;
            }
            // Like the other Picks: this window gets out of the way and the Start, Start Selected and Record keys are off meanwhile
            // (a run's clicks would be swallowed by the pick and taken for the user's choice).
            _webPick.Enabled = false;
            SetToggleHotKey(false);
            Minimize();
            SetStatus("Pick an element in the browser: move the mouse over it and click. Esc cancels.");
            bool picked = false;
            string selector = "", frame = "", error = null;
            int index = 1;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { picked = AutomationTest.Core.Web.WebSession.Shared.PickElement(browser, System.Threading.CancellationToken.None, 120000, out selector, out frame, out index); }
                catch (Exception ex) when (ex is InvalidOperationException || ex is FormatException || ex is KeyNotFoundException) { error = ex.Message; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        _webPick.Enabled = true;
                        if (IsDisposed) return;
                        Restore();
                        SetToggleHotKey(true);
                        Activate();
                        if (error != null) { SetStatus("Pick failed: " + error); return; }
                        if (!picked) { SetStatus("Pick cancelled."); return; }
                        _webSelector.Text = selector;
                        _webFrame.Text = frame;
                        _webIndex.Text = index.ToString();
                        SetStatus("Picked: " + selector + (index > 1 ? " (match #" + index + ")" : "") + (frame.Length > 0 ? "  (in frame " + frame + ")" : ""));
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        private void BrowseExcelFile(HintTextBox target, bool save)
        {
            FileDialog dialog = save
                ? (FileDialog)new SaveFileDialog { Filter = "Excel workbook (*.xlsx)|*.xlsx|Macro-enabled workbook (*.xlsm)|*.xlsm|Binary workbook (*.xlsb)|*.xlsb|Excel 97-2003 (*.xls)|*.xls|CSV UTF-8 (*.csv)|*.csv|PDF (*.pdf)|*.pdf" }
                : new OpenFileDialog { Filter = "Excel files (*.xlsx;*.xlsm;*.xlsb;*.xls;*.csv)|*.xlsx;*.xlsm;*.xlsb;*.xls;*.csv|All files (*.*)|*.*" };
            using (dialog)
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
            }
        }

        /// <summary>Lets the user select a cell (or area) in Excel; its address fills the Cell field.</summary>
        private void PickExcelCell()
        {
            IExcelHost host;
            try { host = AutomationTest.Core.Excel.ExcelSession.Shared.Host; }
            catch (InvalidOperationException ex) { SetStatus(ex.Message); return; }
            Minimize(); // this window gets out of the way (and the Start, Start Selected and Record keys are off while waiting)
            bool shown;
            try { shown = host.BringToFront(); }
            catch (InvalidOperationException ex) { Restore(); SetStatus(ex.Message); return; }
            if (!shown)
            {
                Restore();
                SetStatus("Excel has no workbook open: open the workbook first (a Workbook action, or in Excel), then Pick.");
                return;
            }
            bool taken = WaitForFreeze("Pick a cell", "Select the cell (or area) in Excel, then press " + _settings.Pick + " (or click Freeze now). Esc cancels.");
            string sheet = "", address = "";
            bool read = taken && host.TryGetSelection(out sheet, out address);
            Restore();
            if (!taken) { SetStatus("Pick cancelled."); return; }
            if (!read || address.Length == 0) { SetStatus("Excel has no cell selected (a picture or chart is selected, or the workbook was closed)."); return; }
            _xlCell.Text = address;
            _xlCellExpr.Checked = false;
            SetStatus("Picked " + address + " (sheet " + sheet + ")");
        }

        private void BrowseCommandPath()
        {
            using (var dialog = new OpenFileDialog { Filter = "All files (*.*)|*.*" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) _cmdValue.Text = dialog.FileName;
            }
        }

        // ---------------------------------------------------------------- tray icon

        private NotifyIcon _tray;

        /// <summary>
        /// While a script runs, input is recorded or a point is picked, this window is not shown (no taskbar button either); an
        /// icon in the notification area stands for it. Double-clicking that icon brings the window back, and so does the end of
        /// whatever was going on.
        /// </summary>
        private void HideToTray(string what)
        {
            EnsureTray();
            _tray.Text = "Automation Test - " + what;
            _tray.Visible = true;
            Hide();
        }

        private void ShowFromTray()
        {
            if (_tray != null) _tray.Visible = false;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            WindowLocator.BringToFront(Handle);
            Activate();
        }

        private void EnsureTray()
        {
            if (_tray != null) return;
            var menu = new ContextMenuStrip();
            var show = new ToolStripMenuItem("Show Automation Test");
            var stop = new ToolStripMenuItem("Stop");
            show.Click += (s, e) => ShowFromTray();
            stop.Click += (s, e) =>
            {
                if (_running) _cts?.Cancel();
                else if (_recording) StopRecording();
            };
            menu.Items.Add(show);
            menu.Items.Add(stop);
            menu.Opening += (s, e) => stop.Enabled = _running || _recording;
            _tray = new NotifyIcon { Icon = AppIcon.Get() ?? SystemIcons.Application, ContextMenuStrip = menu, Text = "Automation Test" };
            _tray.DoubleClick += (s, e) => ShowFromTray();
        }

        // ---------------------------------------------------------------- image

        private void Minimize()
        {
            HideToTray("picking");
            Application.DoEvents();
            Thread.Sleep(250);
        }

        private void Restore()
        {
            ShowFromTray();
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
        /// <param name="anchor">With a process, the coordinates shown while selecting are measured from this anchor of the window (null: no coordinates relative to the window).</param>
        private bool PickRegion(WindowTarget process, bool capture, WindowAnchor? anchor, out Rectangle region, out RawImage image, out ClientArea? area)
        {
            region = Rectangle.Empty;
            image = null;
            area = null;
            if (!process.IsEmpty && !ActivateProcess(process)) return false;

            Minimize();
            if (!process.IsEmpty && _settings.AdvancedPositionPicker && !WaitForFreeze(capture ? "Capture an image" : "Pick an area"))
            {
                Restore();
                SetStatus("Pick cancelled.");
                return false;
            }
            if (!Freeze(process, out RawImage snapshot, out Bitmap frozen, out area, out int vl, out int vt, process.IsEmpty || !_settings.AdvancedPositionPicker))
            {
                Restore();
                return false;
            }

            bool ok;
            using (frozen)
            using (var picker = new RegionPickerForm(frozen))
            {
                if (area.HasValue && anchor.HasValue)
                {
                    var a = area.Value;
                    var corner = anchor.Value;
                    picker.CoordinateLabel = TargetLabel(process) + " " + corner;
                    picker.Relative = r => RegionToWindow(a, corner, r);
                }
                ok = picker.ShowDialog() == DialogResult.OK;
                if (ok) region = picker.SelectedRegion;
            }
            Thread.Sleep(150); // let the overlay disappear
            if (ok && capture) image = Crop(snapshot, region.X - vl, region.Y - vt, region.Width, region.Height);
            Restore();
            return ok;
        }

        /// <summary>The anchor corner (centre for Center) of a screen rectangle, measured from the same anchor point of the window's client area.</summary>
        private static Point RegionToWindow(ClientArea window, WindowAnchor anchor, Rectangle screenRegion)
        {
            var regionArea = new ClientArea { Left = screenRegion.X, Top = screenRegion.Y, Width = screenRegion.Width, Height = screenRegion.Height };
            WindowLocator.ToScreen(regionArea, anchor, 0, 0, out int cornerX, out int cornerY);
            WindowLocator.FromScreen(window, anchor, cornerX, cornerY, out int x, out int y);
            return new Point(x, y);
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
            if (!PickRegion(process, false, anchor, out Rectangle region, out _, out ClientArea? snapshotArea)) return false;

            x = region.X;
            y = region.Y;
            w = region.Width;
            h = region.Height;
            if (snapshotArea.HasValue)
            {
                // The range's anchor corner (centre for Center) measured from the same anchor point of the window, as it was
                // when the picture was taken.
                var corner = RegionToWindow(snapshotArea.Value, anchor, region);
                x = corner.X;
                y = corner.Y;
            }
            return true;
        }

        private void CaptureImage()
        {
            if (!PickRegion(Target(_imgProcessName, _imgProcessTitle), true, null, out Rectangle region, out RawImage image, out _)) return;
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

        /// <summary>
        /// Asks whether to save unsaved changes before <paramref name="next"/> (e.g. "creating a new script").
        /// Yes = save first (to the current file, or via the Save dialog); No = go on without saving; Cancel = stay.
        /// Returns true when it is fine to go on.
        /// </summary>
        private bool ConfirmLoseChanges(string next)
        {
            if (!IsDirty()) return true;
            string what = _currentPath == null
                ? "The actions in the list have not been saved."
                : "\"" + Path.GetFileName(_currentPath) + "\" has changes that have not been saved.";
            string message = what + "\n\nDo you want to save before " + next + "?";
            DialogResult answer = ConfirmSave != null
                ? ConfirmSave(message)
                : MessageBox.Show(this, message, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (answer == DialogResult.Yes) return SaveScript(false); // a cancelled Save dialog cancels the whole action
            return answer == DialogResult.No;
        }

        private void NewScript()
        {
            if (!ConfirmLoseChanges("creating a new script")) return;
            _script = new AutomationScript();
            _category.SelectedItem = ActionCategory.Mouse; // a new script starts the editor on Mouse again
            _collapsed.Clear();
            _currentPath = null;
            RefreshList();
            UpdateTitle();
            MarkSaved();
            LicenseAllowsWork();
        }

        private void OpenScript()
        {
            if (!ConfirmLoseChanges("opening another script")) return;
            using (var dialog = new OpenFileDialog
            {
                Filter = "Automation Test script (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = GetScriptFolder()
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                RememberScriptFolder(dialog.FileName);
                try
                {
                    _script = ScriptSerializer.Load(dialog.FileName);
                    _collapsed.Clear();
                    _currentPath = dialog.FileName;
                    RefreshList(_script.Actions.Take(1));
                    UpdateTitle();
                    MarkSaved();
                    SetStatus("Opened " + Path.GetFileName(dialog.FileName));
                    LicenseAllowsWork();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Cannot open the file:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>Folder for the Open/Save dialogs: the one last used (Setting.ini [Files] ScriptFolder), else the program's own folder.</summary>
        private static string GetScriptFolder()
        {
            string folder = IniFile.Load(IniFile.DefaultPath).Get("Files", "ScriptFolder");
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) return folder;
            return Path.GetDirectoryName(Application.ExecutablePath);
        }

        /// <summary>Stores the folder of the file the user just picked, keeping the other Setting.ini entries.</summary>
        private void RememberScriptFolder(string filePath)
        {
            try
            {
                string folder = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(folder) || !SettingsEnabled) return;
                var ini = IniFile.Load(IniFile.DefaultPath);
                ini.Set("Files", "ScriptFolder", folder);
                ini.Save(IniFile.DefaultPath);
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException) { /* not remembering the folder is harmless */ }
        }

        /// <summary>Save writes to the current file when there is one; Save As (or a new script) asks for a path.</summary>
        private bool SaveScript(bool saveAs)
        {
            string path = _currentPath;
            if (saveAs || path == null)
            {
                using (var dialog = new SaveFileDialog
                {
                    Filter = "Automation Test script (*.json)|*.json",
                    DefaultExt = "json",
                    FileName = _currentPath == null ? "script.json" : Path.GetFileName(_currentPath),
                    InitialDirectory = GetScriptFolder()
                })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    path = dialog.FileName;
                    RememberScriptFolder(path);
                }
            }
            try
            {
                ScriptSerializer.Save(_script, path);
                _currentPath = path;
                MarkSaved();
                UpdateTitle();
                SetStatus("Saved " + Path.GetFileName(path));
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Cannot save the file:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void UpdateTitle()
        {
            Text = "Automation Test - " + (_currentPath == null ? "(unsaved)" : Path.GetFileName(_currentPath));
            _saveAs.Visible = _currentPath != null; // Save As only makes sense once there is a file to copy from
            if (_startStop != null && _startStop.Owner != null) CenterRunButtons(_startStop.Owner);
        }

        // ---------------------------------------------------------------- recording

        private void ToggleRecord()
        {
            if (_running) return;
            if (_recording) StopRecording();
            else if (LicenseAllowsWork()) StartRecording();
        }

        private void StartRecording()
        {
            // Where the recorded actions go: decided now, from the selection (none selected = the end of the list).
            _recordTarget = SelectedRows();
            SetToggleHotKey(false); // the Record key is watched by the recorder itself while recording
            _recorder.AllEvents = _settings.RecordAllEvent;
            _recorder.RecordKey = _settings.RecordCode;
            // The program's own shortcut keys are not part of what is recorded.
            _recorder.IgnoredKeys = new HashSet<int> { _settings.StartCode, _settings.StartSelectedCode, _settings.PickCode };
            if (!_recorder.Start(() => BeginInvoke((Action)StopRecording)))
            {
                SetToggleHotKey(true);
                SetStatus("Recording could not start (the input hooks were refused).");
                return;
            }
            _recording = true;
            ApplyBusyState();
            HideToTray("recording"); // get out of the way of the programs being recorded
            SetStatus("Recording... click, scroll and type in your programs; press " + _settings.Record + " to finish (the program is hidden; double-click its icon in the notification area to show it).");
        }

        private void StopRecording()
        {
            if (!_recording) return;
            var events = _recorder.Stop();
            _recording = false;
            // Bring the window back first so the user sees at once that recording has stopped.
            ShowFromTray();
            var nodes = RecordConverter.Build(events, InputRecorder.CharOf);
            if (nodes.Count > 0)
            {
                InsertNodes(nodes, _recordTarget);
                RefreshList(nodes);
            }
            ApplyBusyState();
            SetToggleHotKey(true);
            SetStatus(nodes.Count == 0 ? "Nothing was recorded." : "Recorded " + nodes.Count + " action(s).");
            LicenseAllowsWork(); // recording just ended counts as the program being used
        }

        /// <summary>
        /// Puts actions where Add would put one: inside the selected Loop, Wait, Image, case or else; after the selected
        /// action (after the whole Condition when a Condition line is selected); at the end when nothing is selected.
        /// </summary>
        private void InsertNodes(List<ActionNode> nodes, List<Row> selected)
        {
            if (selected.Count == 0)
            {
                _script.Actions.AddRange(nodes);
                return;
            }
            var last = selected[selected.Count - 1];
            var list = FindListOrNull(last.Node);
            if (list == null) { _script.Actions.AddRange(nodes); return; } // the selection no longer exists
            if (selected.Count == 1 && last.Node.IsContainer && !last.Node.IsKind(ControlKind.Condition))
                last.Node.Children.AddRange(nodes);
            else
                list.InsertRange(list.IndexOf(last.Node) + 1, nodes);
        }

        private List<ActionNode> FindListOrNull(ActionNode node)
        {
            return _rows.FirstOrDefault(r => !r.IsEnd && ReferenceEquals(r.Node, node))?.Siblings;
        }

        // ---------------------------------------------------------------- running

        /// <summary>
        /// Re-checks the license (set by Program; null = no check, as in tests). It returns false when the license is no
        /// longer valid and the user did not provide a new one.
        /// </summary>
        public Func<bool> LicenseGuard { get; set; }

        /// <summary>True when running or recording may begin; otherwise the program closes.</summary>
        private bool LicenseAllowsWork()
        {
            if (LicenseGuard == null || IsDisposed || _closing || LicenseGuard()) return true;
            Close();
            return false;
        }

        private void ShowLicense()
        {
            var result = LicenseManager.Check();
            using (var form = new LicenseForm(false, result)) form.ShowDialog(this);
        }

        private void ToggleRun()
        {
            if (_recording) return;
            if (_running) _cts?.Cancel();
            else if (LicenseAllowsWork()) _ = StartAsync();
        }

        /// <summary>The Start Selected key (and button): starts the selected actions, or stops the run that is going on.</summary>
        private void ToggleRunSelected()
        {
            if (_recording) return;
            if (_running) _cts?.Cancel();
            else StartSelected();
        }

        private async Task StartAsync()
        {
            if (_running) return;
            if (_script.Actions.Count == 0) { SetStatus("Add at least one action first."); return; }

            var snapshot = _script.Clone();
            await RunAsync((runner, token) => runner.Run(snapshot, token), RunKind.All);
        }

        /// <summary>
        /// Start Selected: runs only the selected actions, to try them out quickly. An action inside a Loop or Wait runs on its
        /// own, once; a selected Loop, Wait, Image or Condition runs with everything inside it. When the selected actions read
        /// variables that nothing in the selection sets, a dialog asks for their values first.
        /// </summary>
        private async void StartSelected()
        {
            if (_running || _recording) return;
            if (!LicenseAllowsWork()) return;
            var rows = SelectedRows();
            if (rows.Count == 0) { SetStatus("Select at least one action first."); return; }

            var plan = SelectionPlanner.Build(_script, rows.Select(r => r.Node));
            if (plan.RunNodes.Count == 0) { SetStatus("The selected actions have nothing to run."); return; }

            Dictionary<string, object> values = null;
            if (plan.Needs.Count > 0)
            {
                SetToggleHotKey(false); // a shortcut pressed while the dialog is open must not start anything else
                try
                {
                    using (var form = new VariableInputForm(plan.Needs))
                    {
                        if (form.ShowDialog(this) != DialogResult.OK) return;
                        values = form.Values;
                    }
                }
                finally
                {
                    SetToggleHotKey(true);
                }
            }

            // The actions chosen by hand run even when they are disabled (choosing them is a clear request); what is inside them
            // keeps its own state, so a disabled action inside a chosen Loop is still skipped.
            var chosen = new HashSet<ActionNode>(rows.Select(r => r.Node));
            var nodes = plan.RunNodes.Select(n => { var copy = n.Clone(); if (chosen.Contains(n)) copy.Disabled = false; return copy; }).ToList();
            await RunAsync((runner, token) => runner.Run(nodes, values, token), RunKind.Selected);
        }

        /// <summary>Runs something on the script runner in the background with the status messages, locking the window meanwhile.</summary>
        private async Task RunAsync(Action<ScriptRunner, CancellationToken> run, RunKind kind)
        {
            if (_running) return;
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

            _runKind = kind;
            SetRunning(true);
            try
            {
                await Task.Run(() => run(runner, token));
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
                LicenseAllowsWork(); // a run that just ended counts as the program being used
            }
        }

        /// <summary>
        /// While a script runs or input is recorded, every button and field is locked; only the Stop of what is going on
        /// (Start's Stop, or Record's Stop) stays usable.
        /// </summary>
        private void ApplyBusyState()
        {
            bool busy = _running || _recording;
            foreach (var item in new ToolStripItem[] { _btnNew, _btnOpen, _btnSave, _saveAs })
                item.Enabled = !busy;
            bool runAll = _running && _runKind == RunKind.All, runSelected = _running && _runKind == RunKind.Selected;
            // While a run goes on, the button that started it is the Stop button and the other one waits.
            _startStop.Enabled = !_recording && (!_running || runAll);
            _record.Enabled = !_running;
            _settingsButton.Enabled = !busy;
            _license.Enabled = !busy;
            // Start / Start Selected / Record turn red while they are going on (Start shows a square, Stop).
            if (runAll) SetIcon(_startStop, "", "■", "Stop (" + _settings.Start + ")");
            else SetIcon(_startStop, "", "▶", "Start (" + _settings.Start + ")");
            if (runSelected) SetIcon(_startSelected, "", "■", "Stop (" + _settings.StartSelected + ")");
            else SetIcon(_startSelected, "", "▶▶", "Start Selected (" + _settings.StartSelected + "): run only the selected actions");
            if (_recording) SetIcon(_record, "", "●", "Stop recording (" + _settings.Record + ")");
            else SetIcon(_record, "", "●", "Record (" + _settings.Record + "): your mouse clicks, wheel and keys become actions");
            _editArea.Enabled = !busy;
            _list.Enabled = !busy;
            UpdateButtonStates();
        }


        private void SetRunning(bool running)
        {
            _running = running;
            ApplyBusyState();
            if (running)
            {
                // While a script runs, only the key that started it stops it (Start for a full run, Start Selected for a selected run),
                // through a keyboard hook that ignores keys the script sends itself (and does not swallow them), so a script can still
                // send those keys to another program. Esc stops nothing here: it only cancels a Pick.
                SetToggleHotKey(false);
                bool selectedRun = _runKind == RunKind.Selected;
                int stopKey = selectedRun ? _settings.StartSelectedCode : _settings.StartCode;
                if (!_stopHook.Start(() => _cts?.Cancel(), stopKey))
                {
                    // Fallback if the hook cannot be installed: a plain global hot key (it also swallows the key).
                    _hotKeyFallback = true;
                    HotKeyNative.RegisterHotKey(Handle, selectedRun ? HotKeyStartSelectedId : HotKeyToggleId, HotKeyNative.MOD_NOREPEAT, (uint)stopKey);
                }
                HideToTray("running");
            }
            else
            {
                _stopHook.Stop();
                if (_hotKeyFallback)
                {
                    HotKeyNative.UnregisterHotKey(Handle, HotKeyToggleId);
                    HotKeyNative.UnregisterHotKey(Handle, HotKeyStartSelectedId);
                    _hotKeyFallback = false;
                }
                _runKind = RunKind.None;
                ApplyBusyState();
                SetToggleHotKey(true);
                ShowFromTray();
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

        /// <summary>
        /// Registers or releases the global Start, Start Selected and Record keys. They are released while the Key box records
        /// a key, while a script runs (a hook watches the Start keys and Esc then) and while recording (the recorder watches the Record key).
        /// </summary>
        /// <param name="report">Say in the status bar when a key is taken by another program (only at start-up, so it never hides a run's result).</param>
        private void SetToggleHotKey(bool enabled, bool report = false)
        {
            if (!IsHandleCreated) return;
            HotKeyNative.UnregisterHotKey(Handle, HotKeyToggleId);
            HotKeyNative.UnregisterHotKey(Handle, HotKeyStartSelectedId);
            HotKeyNative.UnregisterHotKey(Handle, HotKeyRecordId);
            if (!enabled || _running || _recording) return;
            var taken = new List<string>();
            if (!HotKeyNative.RegisterHotKey(Handle, HotKeyToggleId, HotKeyNative.MOD_NOREPEAT, (uint)_settings.StartCode)) taken.Add(_settings.Start + " (Start)");
            if (!HotKeyNative.RegisterHotKey(Handle, HotKeyStartSelectedId, HotKeyNative.MOD_NOREPEAT, (uint)_settings.StartSelectedCode)) taken.Add(_settings.StartSelected + " (Start Selected)");
            if (!HotKeyNative.RegisterHotKey(Handle, HotKeyRecordId, HotKeyNative.MOD_NOREPEAT, (uint)_settings.RecordCode)) taken.Add(_settings.Record + " (Record)");
            if (taken.Count > 0 && report)
                SetStatus("Used by another program: " + string.Join(", ", taken) + ". Use the buttons, or choose other keys in Settings.");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Without saved widths (the first run) Details takes the room that is left. With saved widths the columns stay exactly
            // as the user left them: opening the program is not a resize, so nothing is fitted until the window is resized.
            if (_columnsRestored) _lastListWidth = _list.ClientSize.Width;
            else FitDetailsColumn(true);
            _columnsReady = true;
            // Listen for the browser extension from the start: a browser that has been idle wakes its extension only now and then,
            // and the extension keeps its connection (and itself) alive once it has connected.
            System.Threading.Tasks.Task.Run(() =>
            {
                try { AutomationTest.Core.Web.WebSession.Shared.ToString(); }
                catch (InvalidOperationException ex)
                {
                    try { BeginInvoke((Action)(() => SetStatus("Web actions are not available: " + ex.Message))); } catch (InvalidOperationException) { }
                }
            });
            if (_startStop.Owner != null) CenterRunButtons(_startStop.Owner); // item widths are known only once shown
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (SettingsEnabled) LoadSettings(); // before the keys are registered
            ApplyBusyState();                    // tooltips with the keys
            SetToggleHotKey(true, true);
        }

        // ---------------------------------------------------------------- settings (Setting.ini)

        /// <summary>Reads the settings; the first time, the defaults are written to Setting.ini so they can be seen and edited.</summary>
        private void LoadSettings()
        {
            var ini = IniFile.Load(IniFile.DefaultPath);
            bool first = AppSettings.IsMissing(ini);
            _settings = AppSettings.Load(ini);
            if (first) SaveSettings();
        }

        /// <summary>Writes the settings into a freshly read Setting.ini, so the other entries (window, license state) stay as they are.</summary>
        private void SaveSettings()
        {
            if (!SettingsEnabled) return;
            var ini = IniFile.Load(IniFile.DefaultPath);
            _settings.Save(ini);
            try { ini.Save(IniFile.DefaultPath); }
            catch (IOException) { SetStatus("The settings could not be saved next to the program; they apply until it is closed."); }
        }

        private void ShowSettings()
        {
            SetToggleHotKey(false); // a key pressed in the dialog must not start anything
            try
            {
                using (var form = new SettingsForm(_settings))
                {
                    if (form.ShowDialog(this) != DialogResult.OK) return;
                    _settings = form.Result;
                    SaveSettings();
                }
            }
            finally
            {
                SetToggleHotKey(true, true);
                ApplyBusyState(); // tooltips with the new keys
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _stopHook.Stop();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
            _recorder.Dispose();
            HotKeyNative.UnregisterHotKey(Handle, HotKeyToggleId);
            HotKeyNative.UnregisterHotKey(Handle, HotKeyStartSelectedId);
            HotKeyNative.UnregisterHotKey(Handle, HotKeyRecordId);
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == HotKeyNative.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HotKeyToggleId) ToggleRun();
                else if (id == HotKeyStartSelectedId) ToggleRunSelected();
                else if (id == HotKeyRecordId) ToggleRecord();
                else if (id == HotKeyFreezeId) _freezeSignal = 1;
                else if (id == HotKeyFreezeCancelId) _freezeSignal = 2;
                return;
            }
            // The Minimize button (and Alt+Space, Minimize) hides the window to the notification-area icon instead of the taskbar.
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt64() & 0xFFF0) == SC_MINIMIZE && !_closing)
            {
                HideToTray("hidden");
                return;
            }
            base.WndProc(ref m);
        }

        private const int WM_SYSCOMMAND = 0x0112, SC_MINIMIZE = 0xF020;

        /// <summary>Other ways of minimizing (Show desktop, Win+M) hide it to the notification-area icon too.</summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (WindowState != FormWindowState.Minimized || _closing || !IsHandleCreated) return;
            BeginInvoke((Action)(() =>
            {
                if (WindowState != FormWindowState.Minimized || _closing) return;
                WindowState = FormWindowState.Normal; // so showing it again later gives a normal window
                HideToTray("hidden");
            }));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true; // no license check from here on: the program is leaving anyway
            if (e.CloseReason == CloseReason.WindowsShutDown)
            {
                // Windows is shutting down (possibly because of a Shutdown action): never ask, never block it.
                _cts?.Cancel();
                AutomationTest.Core.Excel.ExcelSession.SharedIfCreated?.Dispose(); // an Excel started hidden is not left behind
                if (SettingsEnabled) SaveWindowSettings();
                base.OnFormClosing(e);
                return;
            }
            // A recording in progress is finished first, so its actions count as unsaved changes in the question below.
            if (_recording) StopRecording();
            // Ask first: answering No must not stop a running script.
            if (!ConfirmLoseChanges("closing the program"))
            {
                _closing = false;
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();
            AutomationTest.Core.Excel.ExcelSession.SharedIfCreated?.Dispose(); // an Excel started hidden is not left behind
            if (SettingsEnabled) SaveWindowSettings();
            base.OnFormClosing(e);
        }

        // ---------------------------------------------------------------- Setting.ini (window)

        /// <summary>True when the program runs for real (set by Program): the window size and place are kept in Setting.ini.</summary>
        public bool SettingsEnabled { get; set; }

        private bool _closing;

        protected override void OnLoad(EventArgs e)
        {
            if (SettingsEnabled) { ApplyWindowSettings(); _columnsRestored = ApplyColumnSettings(); }
            base.OnLoad(e);
        }

        private void ApplyWindowSettings()
        {
            var ini = IniFile.Load(IniFile.DefaultPath);
            int? left = ini.GetInt("Window", "Left"), top = ini.GetInt("Window", "Top");
            int? width = ini.GetInt("Window", "Width"), height = ini.GetInt("Window", "Height");
            if (!left.HasValue || !top.HasValue || !width.HasValue || !height.HasValue) return;
            var bounds = new Rectangle(left.Value, top.Value, Math.Max(width.Value, MinimumSize.Width), Math.Max(height.Value, MinimumSize.Height));
            // Only where the window can still be seen (a monitor may have been unplugged).
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds) && Rectangle.Intersect(s.WorkingArea, bounds).Width >= 100 && Rectangle.Intersect(s.WorkingArea, bounds).Height >= 50)) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            if (ini.Get("Window", "Maximized") == "1") WindowState = FormWindowState.Maximized;
        }

        private const int DetailsColumn = 3;
        private const int MinColumnWidth = 65; // the narrowest any column can be, by hand or by the automatic fit

        /// <summary>
        /// Puts the columns back at the widths the user left them at (Setting.ini, section Columns). A saved width below the minimum
        /// becomes the minimum; any other width is used as saved. True when there were saved widths.
        /// </summary>
        private bool ApplyColumnSettings()
        {
            var ini = IniFile.Load(IniFile.DefaultPath);
            bool any = false;
            foreach (ColumnHeader column in _list.Columns)
            {
                int? width = ini.GetInt("Columns", column.Text);
                if (!width.HasValue) continue;
                column.Width = Math.Max(MinColumnWidth, width.Value);
                any = true;
            }
            return any;
        }

        private void SaveWindowSettings()
        {
            var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            var ini = IniFile.Load(IniFile.DefaultPath); // keeps the other entries (such as the license state)
            ini.Set("Window", "Left", bounds.Left);
            ini.Set("Window", "Top", bounds.Top);
            ini.Set("Window", "Width", bounds.Width);
            ini.Set("Window", "Height", bounds.Height);
            ini.Set("Window", "Maximized", WindowState == FormWindowState.Maximized ? "1" : "0");
            // The widths of the columns, as the user left them.
            foreach (ColumnHeader column in _list.Columns)
                ini.Set("Columns", column.Text, column.Width);
            ini.Remove("Columns", "Layout"); // an older version kept a layout number here
            try { ini.Save(IniFile.DefaultPath); }
            catch (IOException) { } // a read-only folder just means the window is not remembered
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

        private static readonly bool HasMdl2 = FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets");

        /// <summary>Shows another icon (and tip) on a toolbar button made by <see cref="MakeToolIcon"/>.</summary>
        private static void SetIcon(ToolStripButton button, string mdl2Glyph, string fallbackGlyph, string tip)
        {
            button.Text = HasMdl2 ? mdl2Glyph : fallbackGlyph;
            button.ToolTipText = tip;
            button.AccessibleName = tip;
        }

        /// <summary>An icon button for the toolbar (Segoe MDL2 Assets when installed, else a plain symbol).</summary>
        private IconButton MakeToolIcon(string mdl2Glyph, string fallbackGlyph, string tip, EventHandler onClick)
        {
            bool mdl2 = HasMdl2;
            var button = new IconButton(mdl2 ? mdl2Glyph : fallbackGlyph)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = new Font(mdl2 ? "Segoe MDL2 Assets" : "Segoe UI Symbol", 13f, FontStyle.Bold),
                ToolTipText = tip,
                AccessibleName = tip,
                Padding = new Padding(4, 2, 4, 2) // the button grows with its glyph, so it fits at any display scaling
            };
            // A ToolStrip button does not take focus, so the field being edited would keep it: move focus away first.
            // That commits typed numbers (Validating) and ends key recording (Leave re-registers the hot keys).
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
            bool busy = _running || _recording;
            _btnAdd.Enabled = !busy;
            foreach (var button in new[] { _btnDelete, _btnIn, _btnOut, _btnUp, _btnTop, _btnDown, _btnEnd })
                button.Enabled = any && !busy;
            _btnUpdate.Enabled = SingleSelectedRow() != null && !busy; // Update changes one action, so exactly one must be selected
            // Collapse All / Expand All only mean something when some action has actions inside it.
            _startSelected.Enabled = _running ? _runKind == RunKind.Selected : any && !_recording; // while its own run goes on it is the Stop button
            bool branches = HasBranches(_script.Actions);
            _collapseAll.Enabled = _expandAll.Enabled = branches && !busy;
            // Each icon keeps its own colour; pale grey when it cannot be used, red when it is the Stop of what is going on.
            foreach (var button in new[] { _btnAdd, _btnUpdate, _btnDelete, _btnIn, _btnOut, _btnUp, _btnTop, _btnDown, _btnEnd,
                                           _btnNew, _btnOpen, _btnSave, _saveAs, _collapseAll, _expandAll, _startStop, _startSelected, _record, _license, _settingsButton })
            {
                button.Highlight = (button == _startStop && _running && _runKind == RunKind.All) || (button == _startSelected && _running && _runKind == RunKind.Selected) || (button == _record && _recording);
                button.Invalidate();
            }
        }

        private static bool HasBranches(List<ActionNode> nodes)
        {
            return nodes.Any(n => n.Children.Count > 0);
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

        /// <summary>Controls stacked top to bottom in one cell (for example a preview with its caption, or two buttons).</summary>
        private static FlowLayoutPanel Stack(params Control[] controls)
        {
            var panel = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            panel.Controls.AddRange(controls);
            return panel;
        }

        /// <summary>Gives a single input field the common width.</summary>
        private static void FitField(Control c)
        {
            if (c is TextBox || c is ComboBox || c is NumericUpDown || c is DateTimePicker) c.Width = FieldWidth;
        }

        /// <summary>
        /// Adds a "label | field" row. Fields are FieldWidth wide (two fields, such as X and Y, share that width). A unit goes
        /// into the label ("Accuracy (%)"). A Pick / Check button and the "?" sit in one column right after the field: the
        /// button first, then the "?", so buttons and "?" icons start at the same x on every row. The first field stands for
        /// the row in SetRowVisible (or key, when given).
        /// </summary>
        private void AddRow(TableLayoutPanel table, string label, Control field, HelpTopic help = null, string unit = null, Control button = null, Control key = null)
        {
            AddRow(table, label, new[] { field }, help, unit, button, key);
        }

        private void AddRow(TableLayoutPanel table, string label, Control[] fields, HelpTopic help = null, string unit = null, Control button = null, Control key = null)
        {
            if (unit != null) label += " (" + unit + ")";
            var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 8, 0) };
            Control cell;
            if (fields.Length == 1 && help == null && button == null)
            {
                FitField(fields[0]);
                cell = fields[0];
            }
            else
            {
                // field | button then ?
                var host = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Tag = RowHostTag };
                host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, FieldWidth + 6));
                host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                if (fields.Length == 1)
                {
                    FitField(fields[0]);
                    host.Controls.Add(fields[0], 0, 0);
                }
                else
                {
                    // Several fields share the one field width (X and Y side by side).
                    var pair = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(3), Padding = new Padding(0) };
                    int each = (FieldWidth - 4 * (fields.Length - 1)) / fields.Length;
                    for (int i = 0; i < fields.Length; i++)
                    {
                        fields[i].Width = each;
                        fields[i].Margin = new Padding(0, 0, i < fields.Length - 1 ? 4 : 0, 0);
                        pair.Controls.Add(fields[i]);
                    }
                    host.Controls.Add(pair, 0, 0);
                }

                var after = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0) };
                if (button != null) after.Controls.Add(button);
                if (help != null) after.Controls.Add(new HelpIcon(help));
                host.Controls.Add(after, 1, 0);
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
