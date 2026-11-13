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
        Web
    }

    public enum WebKind
    {
        /// <summary>Opens the address in a new tab; starts the browser first when it is not running. Actions inside it use that browser.</summary>
        NewTab,
        /// <summary>Makes another open tab (by title pattern, address pattern and/or position) the one the script uses. Actions inside it use that browser.</summary>
        SwitchTab,
        /// <summary>Waits until an element is on the page.</summary>
        WaitElement,
        /// <summary>Types text into an element.</summary>
        Input,
        /// <summary>Clicks an element.</summary>
        Click,
        /// <summary>Answers a JavaScript alert, confirm or prompt box.</summary>
        Dialog,
        /// <summary>Reads the text (or value) of an element into a variable.</summary>
        GetText,
        /// <summary>Runs JavaScript in the page, like the browser console, and can keep the result in a variable.</summary>
        Script
    }

    /// <summary>What a Get Text action reads.</summary>
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
        Shutdown
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
        Else
    }

    public enum VariableType
    {
        Position,
        Range,
        String,
        Number,
        Boolean,
        Date,
        DateTime
    }

    public enum LoopType
    {
        /// <summary>Repeat a fixed number of times.</summary>
        Times,
        /// <summary>Repeat until the given time has elapsed since the loop started.</summary>
        Duration,
        /// <summary>Repeat until a local date and time is reached.</summary>
        Until
    }
}
