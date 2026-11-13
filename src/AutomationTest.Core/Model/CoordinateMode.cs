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
        Keyboard
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
