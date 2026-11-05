using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Automation.Core.Imaging;
using Automation.Core.Input;

namespace Automation.Core.Model
{
    /// <summary>Settings of a Mouse action. All times are milliseconds.</summary>
    [DataContract]
    public sealed class MouseSettings
    {
        public MouseAction Action { get; set; }
        public MouseButton Button { get; set; }

        /// <summary>Duration of the cursor move to the position.</summary>
        [DataMember(Order = 3)] public long TimeMs { get; set; } = 500;

        /// <summary>Wait after the action finished.</summary>
        [DataMember(Order = 4)] public long DelayMs { get; set; } = 500;

        /// <summary>Window the position is relative to. Empty means absolute desktop coordinates.</summary>
        [DataMember(Order = 5)] public WindowTarget Process { get; set; } = new WindowTarget();

        public WindowAnchor Anchor { get; set; }
        [DataMember(Order = 7)] public int X { get; set; }
        [DataMember(Order = 8)] public int Y { get; set; }

        /// <summary>Wheel notches for ScrollUp / ScrollDown.</summary>
        [DataMember(Order = 9)] public int ScrollNotches { get; set; } = 1;

        // Enums are stored by name, not number, so reordering them never changes the meaning of saved files.
        [DataMember(Name = "Action", Order = 1)]
        private string ActionName
        {
            get { return Action.ToString(); }
            set { Action = ActionNode.Parse(value, MouseAction.Click); }
        }

        [DataMember(Name = "Button", Order = 2)]
        private string ButtonName
        {
            get { return Button.ToString(); }
            set { Button = ActionNode.Parse(value, MouseButton.Left); }
        }

        [DataMember(Name = "Anchor", Order = 6)]
        private string AnchorName
        {
            get { return Anchor.ToString(); }
            set { Anchor = ActionNode.Parse(value, WindowAnchor.TopLeft); }
        }

        // The serializer skips constructors and initializers, so every non-zero default is set here.
        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            TimeMs = 500;
            DelayMs = 500;
            ScrollNotches = 1;
            Process = new WindowTarget();
        }

        public MouseSettings Clone()
        {
            var copy = (MouseSettings)MemberwiseClone();
            copy.Process = (Process ?? new WindowTarget()).Clone();
            return copy;
        }

        internal void Normalize()
        {
            if (Process == null) Process = new WindowTarget();
            if (TimeMs < 0) TimeMs = 0;
            if (DelayMs < 0) DelayMs = 0;
            if (ScrollNotches < 1) ScrollNotches = 1;
        }
    }

    /// <summary>Settings of a Keyboard action.</summary>
    [DataContract]
    public sealed class KeyboardSettings
    {
        public KeyboardAction Action { get; set; }

        /// <summary>
        /// KeyPress / KeyUp / KeyDown: one key name (see KeyCodes). ComboKey: key names joined by "+" in press order.
        /// Text: the text to type.
        /// </summary>
        [DataMember(Order = 2)] public string Value { get; set; } = "";

        /// <summary>KeyPress and ComboKey: how long the keys are held. Text: total typing time, spread over the characters.</summary>
        [DataMember(Order = 3)] public long TimeMs { get; set; } = 500;

        /// <summary>Wait after the action finished.</summary>
        [DataMember(Order = 4)] public long DelayMs { get; set; } = 500;

        [DataMember(Name = "Action", Order = 1)]
        private string ActionName
        {
            get { return Action.ToString(); }
            set { Action = ActionNode.Parse(value, KeyboardAction.KeyPress); }
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Value = "";
            TimeMs = 500;
            DelayMs = 500;
        }

        public KeyboardSettings Clone()
        {
            return (KeyboardSettings)MemberwiseClone();
        }

        internal void Normalize()
        {
            if (Value == null) Value = "";
            if (TimeMs < 0) TimeMs = 0;
            if (DelayMs < 0) DelayMs = 0;
        }
    }

    /// <summary>Settings of a Command action.</summary>
    [DataContract]
    public sealed class CommandSettings
    {
        public CommandKind Kind { get; set; }

        /// <summary>Open: path of the program or document to open.</summary>
        [DataMember(Order = 2)] public string Value { get; set; } = "";

        [DataMember(Name = "Kind", Order = 1)]
        private string KindName
        {
            get { return Kind.ToString(); }
            set { Kind = ActionNode.Parse(value, CommandKind.Open); }
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Value = "";
        }

        public CommandSettings Clone()
        {
            return (CommandSettings)MemberwiseClone();
        }

        internal void Normalize()
        {
            if (Value == null) Value = "";
        }
    }

    /// <summary>Settings of a Loop action.</summary>
    [DataContract]
    public sealed class LoopSettings
    {
        public LoopType Mode { get; set; }

        /// <summary>Mode Count: number of iterations; 0 repeats until stopped.</summary>
        [DataMember(Order = 2)] public long Count { get; set; } = 1;

        /// <summary>Mode Duration: how long to keep repeating.</summary>
        [DataMember(Order = 3)] public long DurationMs { get; set; } = 10000;

        /// <summary>Mode Until: local date and time as "yyyy-MM-ddTHH:mm:ss"; stored as text to avoid the JSON date format.</summary>
        [DataMember(Order = 4)] public string Until { get; set; }

        [DataMember(Name = "Mode", Order = 1)]
        private string ModeName
        {
            get { return Mode.ToString(); }
            set { Mode = ActionNode.Parse(value, LoopType.Count); }
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Count = 1;
            DurationMs = 10000;
        }

        public DateTime? GetUntil()
        {
            return DateTime.TryParseExact(Until, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
                ? value : (DateTime?)null;
        }

        public void SetUntil(DateTime value)
        {
            Until = value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        public LoopSettings Clone()
        {
            return (LoopSettings)MemberwiseClone();
        }

        internal void Normalize()
        {
            if (Count < 0) Count = 0;
            if (DurationMs < 0) DurationMs = 0;
        }
    }

    /// <summary>
    /// Settings of a Wait action: waits for a program's window to exist and come to the front, then runs the children.
    /// </summary>
    [DataContract]
    public sealed class WaitSettings
    {
        [DataMember(Order = 1)] public WindowTarget Process { get; set; } = new WindowTarget();

        /// <summary>How long one attempt waits for the window to appear and become active.</summary>
        [DataMember(Order = 2)] public long TimeMs { get; set; } = 10000;

        /// <summary>Extra attempts after the first one times out.</summary>
        [DataMember(Order = 3)] public int Retry { get; set; }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Process = new WindowTarget();
            TimeMs = 10000;
        }

        public WaitSettings Clone()
        {
            var copy = (WaitSettings)MemberwiseClone();
            copy.Process = (Process ?? new WindowTarget()).Clone();
            return copy;
        }

        internal void Normalize()
        {
            if (Process == null) Process = new WindowTarget();
            if (TimeMs < 0) TimeMs = 0;
            if (Retry < 0) Retry = 0;
        }
    }

    /// <summary>
    /// Settings of an Image action: looks for a picture inside a range and, when it is found, runs the children
    /// relative to where it was found.
    /// </summary>
    [DataContract]
    public sealed class ImageSettings
    {
        /// <summary>Window the range is relative to. Empty means the desktop.</summary>
        [DataMember(Order = 1)] public WindowTarget Process { get; set; } = new WindowTarget();

        /// <summary>With a Process: the point of its client area that the range is measured from.</summary>
        public WindowAnchor Anchor { get; set; }

        /// <summary>Top-left of the search range, as an offset from the anchor (or absolute desktop pixels without a Process).</summary>
        [DataMember(Order = 3)] public int RangeX { get; set; }
        [DataMember(Order = 4)] public int RangeY { get; set; }

        /// <summary>Size of the search range; 0 means the whole client area (or the whole desktop without a Process).</summary>
        [DataMember(Order = 5)] public int RangeWidth { get; set; }
        [DataMember(Order = 6)] public int RangeHeight { get; set; }

        /// <summary>Minimum similarity in percent (1-100) for the picture to count as found.</summary>
        [DataMember(Order = 7)] public int Accuracy { get; set; } = 90;

        [DataMember(Order = 8)] public int ImageWidth { get; set; }
        [DataMember(Order = 9)] public int ImageHeight { get; set; }

        /// <summary>The picture to look for: deflate-compressed BGRA pixels, base64 (see RawImage).</summary>
        [DataMember(Order = 10, EmitDefaultValue = false)] public string ImageData { get; set; }

        [DataMember(Name = "Anchor", Order = 2)]
        private string AnchorName
        {
            get { return Anchor.ToString(); }
            set { Anchor = ActionNode.Parse(value, WindowAnchor.TopLeft); }
        }

        public bool HasImage
        {
            get { return !string.IsNullOrEmpty(ImageData) && ImageWidth > 0 && ImageHeight > 0; }
        }

        /// <exception cref="System.IO.InvalidDataException">There is no picture, or its data is damaged.</exception>
        public RawImage GetImage()
        {
            return RawImage.Decode(ImageWidth, ImageHeight, ImageData);
        }

        public void SetImage(RawImage image)
        {
            ImageWidth = image.Width;
            ImageHeight = image.Height;
            ImageData = image.Encode();
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Process = new WindowTarget();
            Accuracy = 90;
        }

        public ImageSettings Clone()
        {
            var copy = (ImageSettings)MemberwiseClone();
            copy.Process = (Process ?? new WindowTarget()).Clone();
            return copy;
        }

        internal void Normalize()
        {
            if (Process == null) Process = new WindowTarget();
            if (Accuracy < 1) Accuracy = 1;
            if (Accuracy > 100) Accuracy = 100;
            if (RangeWidth < 0) RangeWidth = 0;
            if (RangeHeight < 0) RangeHeight = 0;
        }
    }

    /// <summary>
    /// One node of the action tree. The settings of the node's category live in a sub-object (Mouse, Keyboard,
    /// Command, Loop or Wait); the others are absent from the JSON. Only Control actions (Loop, Wait) have children.
    /// </summary>
    [DataContract]
    public sealed class ActionNode
    {
        /// <summary>Optional alias shown in the action list; empty means the action type is shown.</summary>
        [DataMember(Order = 0, EmitDefaultValue = false)] public string Name { get; set; }

        public ActionCategory Category { get; set; }
        public ControlKind Control { get; set; }

        [DataMember(Order = 3, EmitDefaultValue = false)] public MouseSettings Mouse { get; set; }
        [DataMember(Order = 4, EmitDefaultValue = false)] public KeyboardSettings Keyboard { get; set; }
        [DataMember(Order = 5, EmitDefaultValue = false)] public CommandSettings Command { get; set; }
        [DataMember(Order = 6, EmitDefaultValue = false)] public LoopSettings Loop { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public WaitSettings Wait { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public ImageSettings Image { get; set; }

        public List<ActionNode> Children { get; set; } = new List<ActionNode>();

        public bool IsContainer
        {
            get { return Category == ActionCategory.Control; }
        }

        public static ActionNode NewMouse(MouseSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Mouse, Mouse = settings ?? new MouseSettings() };
        }

        public static ActionNode NewKeyboard(KeyboardSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Keyboard, Keyboard = settings ?? new KeyboardSettings() };
        }

        public static ActionNode NewCommand(CommandSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Command, Command = settings ?? new CommandSettings() };
        }

        public static ActionNode NewLoop(LoopSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Control, Control = ControlKind.Loop, Loop = settings ?? new LoopSettings() };
        }

        public static ActionNode NewImage(ImageSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Control, Control = ControlKind.Image, Image = settings ?? new ImageSettings() };
        }

        public static ActionNode NewWait(WaitSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Control, Control = ControlKind.Wait, Wait = settings ?? new WaitSettings() };
        }

        [DataMember(Name = "Category", Order = 1)]
        private string CategoryName
        {
            get { return Category.ToString(); }
            set { Category = Parse(value, ActionCategory.Mouse); }
        }

        [DataMember(Name = "Control", Order = 2, EmitDefaultValue = false)]
        private string ControlName
        {
            get { return Category == ActionCategory.Control ? Control.ToString() : null; }
            set { Control = Parse(value, ControlKind.Loop); }
        }

        // Written only for containers that have children, so leaf nodes stay small.
        [DataMember(Name = "Children", Order = 8, EmitDefaultValue = false)]
        private List<ActionNode> ChildrenData
        {
            get { return IsContainer && Children.Count > 0 ? Children : null; }
            set { Children = value ?? new List<ActionNode>(); }
        }

        internal static T Parse<T>(string value, T fallback) where T : struct
        {
            return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Children = new List<ActionNode>();
        }

        /// <summary>Deep copy including children.</summary>
        public ActionNode Clone()
        {
            var copy = (ActionNode)MemberwiseClone();
            copy.Mouse = Mouse?.Clone();
            copy.Keyboard = Keyboard?.Clone();
            copy.Command = Command?.Clone();
            copy.Loop = Loop?.Clone();
            copy.Wait = Wait?.Clone();
            copy.Image = Image?.Clone();
            copy.Children = Children.Select(c => c.Clone()).ToList();
            return copy;
        }

        /// <summary>Repairs values that a hand-edited file may have left invalid, and keeps only the sub-object that applies.</summary>
        internal void Normalize()
        {
            if (Children == null) Children = new List<ActionNode>();
            Children.RemoveAll(c => c == null);

            var mouse = Category == ActionCategory.Mouse ? (Mouse ?? new MouseSettings()) : null;
            var keyboard = Category == ActionCategory.Keyboard ? (Keyboard ?? new KeyboardSettings()) : null;
            var command = Category == ActionCategory.Command ? (Command ?? new CommandSettings()) : null;
            var loop = Category == ActionCategory.Control && Control == ControlKind.Loop ? (Loop ?? new LoopSettings()) : null;
            var wait = Category == ActionCategory.Control && Control == ControlKind.Wait ? (Wait ?? new WaitSettings()) : null;
            var image = Category == ActionCategory.Control && Control == ControlKind.Image ? (Image ?? new ImageSettings()) : null;
            Mouse = mouse; Keyboard = keyboard; Command = command; Loop = loop; Wait = wait; Image = image;
            mouse?.Normalize(); keyboard?.Normalize(); command?.Normalize(); loop?.Normalize(); wait?.Normalize(); image?.Normalize();

            if (!IsContainer) Children.Clear();
            foreach (var child in Children) child.Normalize();
        }
    }
}
