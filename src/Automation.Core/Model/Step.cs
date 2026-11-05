using System;
using System.Runtime.Serialization;
using Automation.Core.Input;

namespace Automation.Core.Model
{
    /// <summary>
    /// One script step: move the cursor to (X, Y) over MoveDurationMs, perform Action,
    /// then wait DelayAfterMs. The whole sequence is performed Repeat times.
    /// </summary>
    [DataContract]
    public sealed class Step
    {
        public StepAction Action { get; set; }
        public MouseButton Button { get; set; }

        [DataMember(Order = 3)] public int X { get; set; }
        [DataMember(Order = 4)] public int Y { get; set; }
        [DataMember(Order = 5)] public int MoveDurationMs { get; set; }
        [DataMember(Order = 6)] public int DelayAfterMs { get; set; }
        [DataMember(Order = 7)] public int Repeat { get; set; } = 1;

        /// <summary>Screen: X/Y are absolute. Window: X/Y are offsets from Anchor of the script's target window.</summary>
        public CoordinateMode Mode { get; set; }
        public WindowAnchor Anchor { get; set; }

        [DataMember(Name = "Mode", Order = 8)]
        private string ModeName
        {
            get { return Mode.ToString(); }
            set { Mode = Enum.TryParse(value, true, out CoordinateMode parsed) ? parsed : CoordinateMode.Screen; }
        }

        [DataMember(Name = "Anchor", Order = 9)]
        private string AnchorName
        {
            get { return Anchor.ToString(); }
            set { Anchor = Enum.TryParse(value, true, out WindowAnchor parsed) ? parsed : WindowAnchor.TopLeft; }
        }

        // Enums are stored by name, not by number, so reordering the enums never changes the meaning of saved files.
        [DataMember(Name = "Action", Order = 1)]
        private string ActionName
        {
            get { return Action.ToString(); }
            set { Action = Enum.TryParse(value, true, out StepAction parsed) ? parsed : StepAction.Click; }
        }

        [DataMember(Name = "Button", Order = 2)]
        private string ButtonName
        {
            get { return Button.ToString(); }
            set { Button = Enum.TryParse(value, true, out MouseButton parsed) ? parsed : MouseButton.Left; }
        }

        // The serializer skips constructors and initializers, so defaults for missing JSON fields are set here.
        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Repeat = 1;
        }

        public Step Clone()
        {
            return (Step)MemberwiseClone();
        }
    }
}
