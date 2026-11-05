using System.Runtime.Serialization;

namespace Automation.Core.Model
{
    /// <summary>What a step does once the cursor has reached the step position.</summary>
    [DataContract]
    public enum StepAction
    {
        /// <summary>Press and release the button.</summary>
        [EnumMember] Click,
        /// <summary>Press and hold the button.</summary>
        [EnumMember] MouseDown,
        /// <summary>Release the button.</summary>
        [EnumMember] MouseUp,
        /// <summary>Only move the cursor.</summary>
        [EnumMember] Move
    }
}
