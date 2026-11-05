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

        public Step Clone()
        {
            return (Step)MemberwiseClone();
        }
    }
}
