namespace Automation.Core.Model
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
        Command,
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
        Open
    }

    public enum ControlKind
    {
        Loop,
        Wait,
        Image
    }

    public enum LoopType
    {
        /// <summary>Repeat a fixed number of times.</summary>
        Count,
        /// <summary>Repeat until the given time has elapsed since the loop started.</summary>
        Duration,
        /// <summary>Repeat until a local date and time is reached.</summary>
        Until
    }
}
