using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
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
    /// One node of the action tree. The settings of the node's category live in a sub-object (Mouse or Loop);
    /// the other one is absent from the JSON. Only containers (Loop) have children.
    /// </summary>
    [DataContract]
    public sealed class ActionNode
    {
        public ActionCategory Category { get; set; }
        public ControlKind Control { get; set; }

        /// <summary>Set when Category is Mouse.</summary>
        [DataMember(Order = 3, EmitDefaultValue = false)] public MouseSettings Mouse { get; set; }

        /// <summary>Set when Category is Control and Control is Loop.</summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public LoopSettings Loop { get; set; }

        public List<ActionNode> Children { get; set; } = new List<ActionNode>();

        public bool IsContainer
        {
            get { return Category == ActionCategory.Control && Control == ControlKind.Loop; }
        }

        public static ActionNode NewMouse(MouseSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Mouse, Mouse = settings ?? new MouseSettings() };
        }

        public static ActionNode NewLoop(LoopSettings settings)
        {
            return new ActionNode { Category = ActionCategory.Control, Control = ControlKind.Loop, Loop = settings ?? new LoopSettings() };
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
        [DataMember(Name = "Children", Order = 5, EmitDefaultValue = false)]
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
            copy.Loop = Loop?.Clone();
            copy.Children = Children.Select(c => c.Clone()).ToList();
            return copy;
        }

        /// <summary>Repairs values that a hand-edited file may have left invalid.</summary>
        internal void Normalize()
        {
            if (Children == null) Children = new List<ActionNode>();
            Children.RemoveAll(c => c == null);
            if (Category == ActionCategory.Mouse)
            {
                if (Mouse == null) Mouse = new MouseSettings();
                Mouse.Normalize();
                Loop = null;
            }
            else
            {
                if (Loop == null) Loop = new LoopSettings();
                Loop.Normalize();
                Mouse = null;
            }
            if (!IsContainer) Children.Clear();
            foreach (var child in Children) child.Normalize();
        }
    }
}
