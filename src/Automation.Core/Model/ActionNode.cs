using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Automation.Core.Input;

namespace Automation.Core.Model
{
    /// <summary>
    /// One node of the action tree. A single class (instead of subclasses) keeps the JSON simple;
    /// fields that do not apply to the node's category are ignored. Control nodes (Loop) own Children.
    /// All times are milliseconds.
    /// </summary>
    [DataContract]
    public sealed class ActionNode
    {
        public ActionCategory Category { get; set; }

        // ---- Mouse
        public MouseAction Action { get; set; }
        public MouseButton Button { get; set; }

        /// <summary>Mouse: duration of the cursor move to the position.</summary>
        [DataMember(Order = 10)] public long TimeMs { get; set; } = 500;

        /// <summary>Wait after the action finished.</summary>
        [DataMember(Order = 11)] public long DelayMs { get; set; } = 500;

        /// <summary>Window the position is relative to. Empty means absolute desktop coordinates.</summary>
        [DataMember(Order = 12)] public WindowTarget Process { get; set; } = new WindowTarget();

        public WindowAnchor Anchor { get; set; }
        [DataMember(Order = 14)] public int X { get; set; }
        [DataMember(Order = 15)] public int Y { get; set; }

        /// <summary>Wheel notches for ScrollUp / ScrollDown.</summary>
        [DataMember(Order = 16)] public int ScrollNotches { get; set; } = 1;

        // ---- Control
        public ControlKind Control { get; set; }
        public LoopType LoopMode { get; set; }

        /// <summary>Loop (Count): number of iterations; 0 repeats until stopped.</summary>
        [DataMember(Order = 20)] public long LoopCount { get; set; } = 1;

        /// <summary>Loop (Duration): how long to keep repeating.</summary>
        [DataMember(Order = 21)] public long LoopDurationMs { get; set; } = 10000;

        /// <summary>Loop (Until): local date and time as "yyyy-MM-ddTHH:mm:ss"; stored as text to avoid the JSON date format.</summary>
        [DataMember(Order = 22)] public string LoopUntil { get; set; }

        [DataMember(Order = 30)] public List<ActionNode> Children { get; set; } = new List<ActionNode>();

        public bool IsContainer
        {
            get { return Category == ActionCategory.Control && Control == ControlKind.Loop; }
        }

        public DateTime? GetLoopUntil()
        {
            return DateTime.TryParseExact(LoopUntil, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
                ? value : (DateTime?)null;
        }

        public void SetLoopUntil(DateTime value)
        {
            LoopUntil = value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        // Enums are stored by name, not number, so reordering them never changes the meaning of saved files.
        [DataMember(Name = "Category", Order = 1)]
        private string CategoryName
        {
            get { return Category.ToString(); }
            set { Category = Parse(value, ActionCategory.Mouse); }
        }

        [DataMember(Name = "Action", Order = 2)]
        private string ActionName
        {
            get { return Action.ToString(); }
            set { Action = Parse(value, MouseAction.Click); }
        }

        [DataMember(Name = "Button", Order = 3)]
        private string ButtonName
        {
            get { return Button.ToString(); }
            set { Button = Parse(value, MouseButton.Left); }
        }

        [DataMember(Name = "Anchor", Order = 13)]
        private string AnchorName
        {
            get { return Anchor.ToString(); }
            set { Anchor = Parse(value, WindowAnchor.TopLeft); }
        }

        [DataMember(Name = "Control", Order = 18)]
        private string ControlName
        {
            get { return Control.ToString(); }
            set { Control = Parse(value, ControlKind.Loop); }
        }

        [DataMember(Name = "LoopMode", Order = 19)]
        private string LoopModeName
        {
            get { return LoopMode.ToString(); }
            set { LoopMode = Parse(value, LoopType.Count); }
        }

        private static T Parse<T>(string value, T fallback) where T : struct
        {
            return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }

        // The serializer skips constructors and initializers, so every field whose default is not zero is set here.
        // Do not add EmitDefaultValue=false to these fields: a user-set 0 would be dropped and come back as the default.
        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            TimeMs = 500;
            DelayMs = 500;
            ScrollNotches = 1;
            LoopCount = 1;
            LoopDurationMs = 10000;
            Process = new WindowTarget();
            Children = new List<ActionNode>();
        }

        /// <summary>Deep copy including children.</summary>
        public ActionNode Clone()
        {
            var copy = (ActionNode)MemberwiseClone();
            copy.Process = (Process ?? new WindowTarget()).Clone();
            copy.Children = Children.Select(c => c.Clone()).ToList();
            return copy;
        }

        /// <summary>Repairs values that a hand-edited file may have left invalid.</summary>
        internal void Normalize()
        {
            if (Process == null) Process = new WindowTarget();
            if (Children == null) Children = new List<ActionNode>();
            Children.RemoveAll(c => c == null);
            if (TimeMs < 0) TimeMs = 0;
            if (DelayMs < 0) DelayMs = 0;
            if (ScrollNotches < 1) ScrollNotches = 1;
            if (LoopCount < 0) LoopCount = 0;
            if (LoopDurationMs < 0) LoopDurationMs = 0;
            if (!IsContainer) Children.Clear();
            foreach (var child in Children) child.Normalize();
        }
    }
}
