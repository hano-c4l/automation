namespace Automation.Core.Model
{
    /// <summary>How a step's X/Y are interpreted. The default (0) must stay Screen so older files keep working.</summary>
    public enum CoordinateMode
    {
        /// <summary>Absolute screen pixels.</summary>
        Screen,
        /// <summary>Offset from an anchor point of the target window's client area.</summary>
        Window
    }

    /// <summary>The point of the target window's client area that a Window-mode offset is measured from.</summary>
    public enum WindowAnchor
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        Center
    }
}
