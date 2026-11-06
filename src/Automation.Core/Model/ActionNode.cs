using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Automation.Core.Expressions;
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

        /// <summary>True: the position comes from PositionExpression (for example "{img.Position} + {10,20}") instead of X/Y.</summary>
        [DataMember(Order = 10, EmitDefaultValue = false)] public bool UseVariable { get; set; }

        [DataMember(Order = 11, EmitDefaultValue = false)] public string PositionExpression { get; set; }

        // Enums are stored by name, not number, so reordering them never changes the meaning of saved files.
        [DataMember(Name = "Action", Order = 1)]
        private string ActionName
        {
            get { return Action.ToString(); }
            set { Action = ActionNode.Parse(value, MouseAction.Click); }
        }

        // Moving the cursor has no button, so nothing is written for it.
        [DataMember(Name = "Button", Order = 2, EmitDefaultValue = false)]
        private string ButtonName
        {
            get { return Action == MouseAction.Move ? null : Button.ToString(); }
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

        /// <summary>Text action only: Value is an expression (for example "{name} + \" \" + {n}") instead of the literal text.</summary>
        [DataMember(Order = 5, EmitDefaultValue = false)] public bool ValueIsExpression { get; set; }

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
            set { Mode = ActionNode.Parse(value, LoopType.Times); }
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

        /// <summary>Optional variable ID. The result (found or not, Position, Size) is stored there for later actions.</summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public string Id { get; set; }

        /// <summary>
        /// Which point of the found picture the result Position refers to. The Position is measured from the same
        /// Anchor point of the Process window that the range uses (absolute screen pixels without a Process), so a
        /// Mouse action with the same Process and Anchor can use it directly.
        /// </summary>
        public WindowAnchor ResultAnchor { get; set; }

        [DataMember(Name = "ResultAnchor", Order = 12)]
        private string ResultAnchorName
        {
            get { return ResultAnchor.ToString(); }
            set { ResultAnchor = ActionNode.Parse(value, WindowAnchor.TopLeft); }
        }

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

    /// <summary>Settings of a Variable action: creates a variable, or changes one with the same ID.</summary>
    [DataContract]
    public sealed class VariableSettings
    {
        /// <summary>The ID other actions use: {Id}. Letters, digits and underscore; case-insensitive.</summary>
        [DataMember(Order = 1)] public string Id { get; set; } = "";

        public VariableType Type { get; set; } = VariableType.Number;

        /// <summary>True: the value is the result of Expression (such as "{abc} + 1"); false: it is the literal below.</summary>
        [DataMember(Order = 3)] public bool UseExpression { get; set; }

        [DataMember(Order = 4)] public string Expression { get; set; } = "";

        /// <summary>Literal value of String, Number, Boolean ("true"/"false"), Date (yyyy-MM-dd) and DateTime (yyyy-MM-ddTHH:mm:ss), invariant culture.</summary>
        [DataMember(Order = 5)] public string Text { get; set; } = "";

        /// <summary>Position and Range only: the window and anchor the picker measured X/Y from (the stored numbers are just X, Y, Width, Height).</summary>
        [DataMember(Order = 6)] public WindowTarget Process { get; set; } = new WindowTarget();

        public WindowAnchor Anchor { get; set; }
        [DataMember(Order = 8)] public int X { get; set; }
        [DataMember(Order = 9)] public int Y { get; set; }
        [DataMember(Order = 10)] public int Width { get; set; }
        [DataMember(Order = 11)] public int Height { get; set; }

        [DataMember(Name = "Type", Order = 2)]
        private string TypeName
        {
            get { return Type.ToString(); }
            set { Type = ActionNode.Parse(value, VariableType.Number); }
        }

        [DataMember(Name = "Anchor", Order = 7)]
        private string AnchorName
        {
            get { return Anchor.ToString(); }
            set { Anchor = ActionNode.Parse(value, WindowAnchor.TopLeft); }
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Id = "";
            Type = VariableType.Number;
            Expression = "";
            Text = "";
            Process = new WindowTarget();
        }

        /// <summary>The literal value converted to its type.</summary>
        /// <exception cref="ExpressionException">The stored text does not fit the type.</exception>
        public object GetLiteral()
        {
            switch (Type)
            {
                case VariableType.Position: return new PointValue(X, Y);
                case VariableType.Range: return new RangeValue(X, Y, Width, Height);
                case VariableType.String: return Text ?? "";
                default: return Coercion.To(Type, Text ?? "");
            }
        }

        public VariableSettings Clone()
        {
            var copy = (VariableSettings)MemberwiseClone();
            copy.Process = (Process ?? new WindowTarget()).Clone();
            return copy;
        }

        internal void Normalize()
        {
            if (Id == null) Id = "";
            if (Expression == null) Expression = "";
            if (Text == null) Text = "";
            if (Process == null) Process = new WindowTarget();
            if (Width < 0) Width = 0;
            if (Height < 0) Height = 0;
        }
    }

    /// <summary>Settings of a Case (one branch of a Condition).</summary>
    [DataContract]
    public sealed class CaseSettings
    {
        /// <summary>An expression giving true or false, such as "{abc} > 3".</summary>
        [DataMember(Order = 1)] public string Expression { get; set; } = "";

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Expression = "";
        }

        public CaseSettings Clone()
        {
            return (CaseSettings)MemberwiseClone();
        }

        internal void Normalize()
        {
            if (Expression == null) Expression = "";
        }
    }

    /// <summary>
    /// One node of the action tree. The settings of the node's category live in a sub-object (Mouse, Keyboard,
    /// Command, Loop, Wait, Image, Variable or Case); the others are absent from the JSON. Control actions except
    /// Variable have children. A Condition's children are its Cases followed by exactly one Else.
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
        [DataMember(Order = 10, EmitDefaultValue = false)] public VariableSettings Variable { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public CaseSettings Case { get; set; }

        public List<ActionNode> Children { get; set; } = new List<ActionNode>();

        public bool IsContainer
        {
            get { return Category == ActionCategory.Control && Control != ControlKind.Variable; }
        }

        public bool IsKind(ControlKind kind)
        {
            return Category == ActionCategory.Control && Control == kind;
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

        public static ActionNode NewVariable(VariableSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Control, Control = ControlKind.Variable, Variable = settings ?? new VariableSettings() };
        }

        public static ActionNode NewCase(CaseSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Control, Control = ControlKind.Case, Case = settings ?? new CaseSettings() };
        }

        public static ActionNode NewElse()
        {
            return new ActionNode { Category = ActionCategory.Control, Control = ControlKind.Else };
        }

        /// <summary>A Condition always has at least one Case and ends with its Else.</summary>
        public static ActionNode NewCondition()
        {
            var node = new ActionNode { Category = ActionCategory.Control, Control = ControlKind.Condition };
            node.Children.Add(NewCase(new CaseSettings { Expression = "true" }));
            node.Children.Add(NewElse());
            return node;
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
            copy.Variable = Variable?.Clone();
            copy.Case = Case?.Clone();
            copy.Children = Children.Select(c => c.Clone()).ToList();
            return copy;
        }

        /// <summary>Keeps a Condition valid: only Cases and one Else, at least one Case, Else last.</summary>
        private void RepairCondition()
        {
            var cases = new List<ActionNode>();
            ActionNode otherwise = null;
            foreach (var child in Children)
            {
                if (child.IsKind(ControlKind.Case)) cases.Add(child);
                else if (child.IsKind(ControlKind.Else) && otherwise == null) otherwise = child;
            }
            if (cases.Count == 0) cases.Add(NewCase(new CaseSettings { Expression = "true" }));
            if (otherwise == null) otherwise = NewElse();
            Children = new List<ActionNode>(cases) { otherwise };
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
            var variable = Category == ActionCategory.Control && Control == ControlKind.Variable ? (Variable ?? new VariableSettings()) : null;
            var caseSettings = Category == ActionCategory.Control && Control == ControlKind.Case ? (Case ?? new CaseSettings()) : null;
            Mouse = mouse; Keyboard = keyboard; Command = command; Loop = loop; Wait = wait; Image = image; Variable = variable; Case = caseSettings;
            mouse?.Normalize(); keyboard?.Normalize(); command?.Normalize(); loop?.Normalize(); wait?.Normalize(); image?.Normalize();
            variable?.Normalize(); caseSettings?.Normalize();

            if (!IsContainer) Children.Clear();
            if (IsKind(ControlKind.Condition)) RepairCondition();
            foreach (var child in Children) child.Normalize();
        }
    }
}
