namespace AutomationTest.Core.Model
{
    /// <summary>The point of the target window's client area that an offset is measured from.</summary>
    public enum WindowAnchor
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Center
    }

    public enum ActionCategory
    {
        Control,
        Utilities,
        Mouse,
        Keyboard,
        /// <summary>Drives a web browser through the browser extension.</summary>
        Web,
        /// <summary>Drives Microsoft Excel (workbooks, sheets, cells).</summary>
        Excel,
        /// <summary>Opens, closes and arranges programs and their windows (the settings are those of <see cref="CommandSettings"/>).</summary>
        Program
    }

    public enum WebKind
    {
        /// <summary>Opens the address in a new tab; starts the browser first when it is not running. Actions inside it use that browser.</summary>
        NewTab,
        /// <summary>Makes another open tab (by title pattern, address pattern and/or position) the one the script uses. Actions inside it use that browser.</summary>
        SwitchTab,
        /// <summary>Waits until an element appears or disappears, or the address, title or text of the page matches.</summary>
        WaitElement,
        /// <summary>Types text into an element.</summary>
        Input,
        /// <summary>Clicks an element (click, double click or right click).</summary>
        Click,
        /// <summary>Moves the mouse over an element (menus that open on hover).</summary>
        Hover,
        /// <summary>Drags an element and drops it on another one.</summary>
        Drag,
        /// <summary>Chooses an option of a select list.</summary>
        SelectOption,
        /// <summary>Presses a key or a combination (Enter, Tab, Ctrl+A...).</summary>
        PressKey,
        /// <summary>Puts files into a file input.</summary>
        Upload,
        /// <summary>Answers a JavaScript alert, confirm or prompt box.</summary>
        Dialog,
        /// <summary>Reads the text (or value) of an element into a variable.</summary>
        GetText,
        /// <summary>Runs JavaScript in the page, like the browser console, and can keep the result in a variable.</summary>
        Script,
        /// <summary>Saves a picture of the page, or of one element.</summary>
        Screenshot,
        /// <summary>Closes the tab the script is using.</summary>
        CloseTab,
        /// <summary>Closes every window of the browser.</summary>
        CloseBrowser
    }

    public enum WebClickKind
    {
        Click,
        DoubleClick,
        RightClick
    }

    public enum WebWaitKind
    {
        ElementAppears,
        ElementDisappears,
        /// <summary>The address of the tab matches a regular expression.</summary>
        UrlMatches,
        TitleMatches,
        /// <summary>The text of the page matches a regular expression.</summary>
        TextOnPage
    }

    public enum WebGetKind
    {
        /// <summary>The visible text of the element; for a field (input, textarea, select) its value.</summary>
        Text,
        /// <summary>The value of a field (the value of the selected option for a select), or the value attribute.</summary>
        Value,
        /// <summary>The text of the selected option(s) of a select; for a checked checkbox or radio button its label; otherwise like Text.</summary>
        SelectedText,
        /// <summary>true or false: a checkbox or radio button is checked, an option is selected, aria-checked is true.</summary>
        Checked,
        /// <summary>The HTML inside the element.</summary>
        Html,
        /// <summary>The HTML of the element itself.</summary>
        OuterHtml,
        /// <summary>An attribute (href, src, data-id, class, aria-label...), named in the Attribute field.</summary>
        Attribute,
        /// <summary>How many elements match the selector (a number; 0 when none).</summary>
        Count,
        /// <summary>The address of the tab.</summary>
        PageUrl,
        /// <summary>The title of the tab.</summary>
        PageTitle
    }

    public enum BrowserKind
    {
        Chrome,
        Edge
    }

    public enum MouseAction
    {
        Click,
        MouseUp,
        MouseDown,
        Move,
        ScrollUp,
        ScrollDown
    }

    public enum KeyboardAction
    {
        KeyPress,
        KeyUp,
        KeyDown,
        ComboKey,
        Text
    }

    public enum CommandKind
    {
        Open,
        /// <summary>Sends a harmless input event and asks Windows to stay awake, so a long run is not taken for being idle (lock, screen off, sleep).</summary>
        Active,
        /// <summary>Waits Time, then shuts the computer down.</summary>
        Shutdown,
        /// <summary>Closes running programs found by window title, process name, file path or process id.</summary>
        Close,
        // File/Folder actions: they work on a file or a folder, whichever is at the path.
        /// <summary>Copies a file or a folder (with everything in it) to a destination.</summary>
        FsCopy,
        FsMove,
        /// <summary>Gives a file or folder a new name in the same place.</summary>
        FsRename,
        FsDelete,
        /// <summary>Makes a folder (and the folders above it that are missing).</summary>
        FsCreate,
        /// <summary>Stores true or false in a variable: is there a file or folder at the path.</summary>
        FsExists,
        /// <summary>Reads a text file into a variable.</summary>
        FsRead,
        /// <summary>Writes (or appends) text to a file.</summary>
        FsWrite,
        /// <summary>Lists the files and folders of a folder into a table variable.</summary>
        FsList,
        // Window actions (category Program): they act on the first window found (every window found with All).
        /// <summary>Brings the window to the front.</summary>
        WindowActivate,
        WindowMinimize,
        WindowMaximize,
        /// <summary>Back to normal size after minimizing or maximizing.</summary>
        WindowRestore,
        /// <summary>Moves the window so that its top left corner is at X, Y on the screen.</summary>
        WindowMove,
        /// <summary>Gives the window the Width and Height.</summary>
        WindowResize
    }

    /// <summary>Which category an action of <see cref="CommandKind"/> belongs to.</summary>
    public static class CommandKinds
    {
        /// <summary>Open, Close and the window actions are in the category Program; the others (File/Folder, System) in Utilities.</summary>
        public static bool IsProgram(CommandKind kind)
        {
            return kind == CommandKind.Open || kind == CommandKind.Close || IsWindow(kind);
        }

        public static bool IsWindow(CommandKind kind)
        {
            return kind >= CommandKind.WindowActivate && kind <= CommandKind.WindowResize;
        }

        public static ActionCategory CategoryOf(CommandKind kind)
        {
            return IsProgram(kind) ? ActionCategory.Program : ActionCategory.Utilities;
        }
    }

    /// <summary>How the Close action finds the programs to close.</summary>
    public enum ProgramMatch
    {
        /// <summary>A regular expression on the title of the main window.</summary>
        Title,
        /// <summary>The process name, for example notepad (with or without .exe).</summary>
        Name,
        /// <summary>The full path of the program file.</summary>
        Path,
        /// <summary>The process id.</summary>
        Id
    }

    public enum ControlKind
    {
        Loop,
        Wait,
        Image,
        Variable,
        /// <summary>A switch-case: children are Case nodes followed by one Else node.</summary>
        Condition,
        /// <summary>One branch of a Condition; runs when its expression is true and no earlier case was.</summary>
        Case,
        /// <summary>The last branch of a Condition; runs when no case matched.</summary>
        Else,
        /// <summary>Runs its actions; when one of them fails the rest are skipped and the Catch at its end runs instead of the run stopping.</summary>
        Try,
        /// <summary>The last line of a Try; runs when an action of the Try failed. Made together with its Try, never added by hand.</summary>
        Catch,
        /// <summary>Leaves the nearest Loop.</summary>
        Break,
        /// <summary>Goes on with the next pass of the nearest Loop.</summary>
        Continue,
        /// <summary>Ends the script (a script called by Auto Script: goes back to the caller).</summary>
        Return,
        /// <summary>Groups actions under a name. Runs the actions inside it once.</summary>
        Section
    }

    public enum VariableType
    {
        Position,
        Range,
        String,
        Number,
        Boolean,
        Date,
        DateTime,
        /// <summary>Takes the text that is in the clipboard when the action runs (a copy: the variable does not follow the clipboard afterwards).</summary>
        Clipboard,
        /// <summary>Asked from the user when the script runs; never stored in the script file or the log.</summary>
        Password
    }

    public enum LoopType
    {
        /// <summary>Repeat a fixed number of times.</summary>
        Times,
        /// <summary>Repeat until the given time has elapsed since the loop started.</summary>
        Duration,
        /// <summary>Repeat until a local date and time is reached.</summary>
        Until,
        /// <summary>Count from a number to another with a step; the counter is a variable (rows of a sheet, for example).</summary>
        For
    }

    public enum ExcelKind
    {
        /// <summary>Switches to a workbook (opening the file, or making a new workbook, and starting Excel, when needed). Actions inside it work in that workbook.</summary>
        Workbook,
        /// <summary>Makes a sheet the current one. Actions inside it work on that sheet.</summary>
        SelectSheet,
        CreateSheet,
        RenameSheet,
        DeleteSheet,
        GetCell,
        SetCell,
        /// <summary>Reads the cells of an area into a table variable ({t.Rows}, {t.Columns}, {t[row, column]}).</summary>
        GetRange,
        /// <summary>Writes a table variable into the cells from a top-left cell.</summary>
        SetRange,
        /// <summary>Selects a cell or an area (so the user sees it, or keys that follow act on it).</summary>
        Select,
        /// <summary>Looks for text; the actions inside run when it is found.</summary>
        Find,
        GetInfo,
        /// <summary>Calculates an Excel formula.</summary>
        Evaluate,
        InsertCells,
        DeleteCells,
        Clear,
        /// <summary>Copies cells (with their formatting, or only the values) to another place on the sheet.</summary>
        Copy,
        /// <summary>Number format, bold, italic, colours, column width.</summary>
        Format,
        Sort,
        Filter,
        /// <summary>Refreshes the data connections and pivot tables of the workbook.</summary>
        Refresh,
        /// <summary>Runs a macro that exists in an open workbook.</summary>
        RunMacro,
        Save,
        CloseWorkbook,
        CloseExcel,
        /// <summary>Makes a copy of a sheet, in the same workbook or another open one.</summary>
        SheetCopy,
        /// <summary>Moves a sheet to another place in its workbook, or into another open workbook.</summary>
        SheetMove,
        /// <summary>Hides or shows a sheet.</summary>
        SheetVisible,
        /// <summary>Hides rows or columns.</summary>
        HideCells,
        /// <summary>Shows hidden rows or columns again.</summary>
        ShowCells,
        /// <summary>Moves cells (everything in them) to another place.</summary>
        CutCells,
        /// <summary>Replaces text in the cells of an area.</summary>
        ReplaceText
    }

    /// <summary>What a Range Copy pastes.</summary>
    public enum ExcelPasteKind
    {
        /// <summary>Values, formulas and formatting.</summary>
        All,
        /// <summary>Only the values (formulas arrive as their results).</summary>
        Values,
        /// <summary>The formulas (relative references shift like in a paste); values stay as they are.</summary>
        Formulas,
        /// <summary>Only the formatting.</summary>
        Formats,
        /// <summary>Only the column widths of the area.</summary>
        ColumnWidths
    }

    public enum ExcelVisibility
    {
        Visible,
        Hidden,
        /// <summary>Hidden so that the user cannot unhide it from Excel's menu.</summary>
        VeryHidden
    }

    public enum ExcelTarget
    {
        Rows,
        Columns
    }

    public enum ExcelClearKind
    {
        /// <summary>The values and formulas; the formatting stays.</summary>
        Contents,
        /// <summary>The formatting; the values stay.</summary>
        Formats,
        All
    }

    /// <summary>A formatting setting that can be left as it is.</summary>
    public enum ExcelFlag
    {
        Leave,
        Yes,
        No
    }

    public enum ExcelGetKind
    {
        /// <summary>The value, typed: a number, a date, true/false or text.</summary>
        Value,
        /// <summary>The text as Excel shows it (with the number format).</summary>
        Text,
        Formula,
        NumberFormat,
        /// <summary>true when the cell has nothing in it.</summary>
        IsEmpty
    }

    public enum ExcelInfoKind
    {
        LastRow,
        LastColumn,
        UsedRange,
        SheetCount,
        SheetName,
        WorkbookName,
        WorkbookPath,
        Selection
    }

    public enum ExcelSetKind
    {
        /// <summary>As the value is: numbers, dates and true/false are written as such, text like typed into the cell.</summary>
        Auto,
        /// <summary>Always as text ("007" stays 007).</summary>
        Text,
        Formula
    }

    public enum ExcelSheetPosition
    {
        End,
        Start,
        BeforeCurrent,
        AfterCurrent
    }
}
