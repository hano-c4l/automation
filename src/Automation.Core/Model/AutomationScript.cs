using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Automation.Core.Model
{
    [DataContract]
    public sealed class AutomationScript
    {
        public const int CurrentVersion = 1;

        [DataMember(Order = 1)] public int Version { get; set; } = CurrentVersion;

        /// <summary>Delay before the first step, so the user can switch to the target window.</summary>
        [DataMember(Order = 2)] public int StartDelayMs { get; set; }

        /// <summary>Number of times the whole step list runs; 0 means repeat until stopped.</summary>
        [DataMember(Order = 3)] public int RepeatCount { get; set; } = 1;

        /// <summary>Delay between two runs of the step list.</summary>
        [DataMember(Order = 4)] public int RepeatDelayMs { get; set; }

        [DataMember(Order = 5)] public List<Step> Steps { get; set; } = new List<Step>();

        /// <summary>The window that Window-mode steps are relative to.</summary>
        [DataMember(Order = 6)] public WindowTarget Target { get; set; } = new WindowTarget();

        // The serializer skips constructors and initializers, so defaults for missing JSON fields are set here.
        // Add every new field that has a non-zero default to this method.
        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Version = CurrentVersion;
            RepeatCount = 1;
            Steps = new List<Step>();
            Target = new WindowTarget();
        }

        public AutomationScript Clone()
        {
            return new AutomationScript
            {
                Version = Version,
                StartDelayMs = StartDelayMs,
                RepeatCount = RepeatCount,
                RepeatDelayMs = RepeatDelayMs,
                Steps = Steps.Select(s => s.Clone()).ToList(),
                Target = Target.Clone()
            };
        }

        /// <summary>Repairs values that a hand-edited or older file may have left invalid.</summary>
        internal void Normalize()
        {
            if (Steps == null) Steps = new List<Step>();
            if (Target == null) Target = new WindowTarget();
            Steps.RemoveAll(s => s == null);
            foreach (var step in Steps)
            {
                if (step.Repeat < 1) step.Repeat = 1;
                if (step.MoveDurationMs < 0) step.MoveDurationMs = 0;
                if (step.DelayAfterMs < 0) step.DelayAfterMs = 0;
            }
            if (RepeatCount < 0) RepeatCount = 0;
            if (RepeatDelayMs < 0) RepeatDelayMs = 0;
            if (StartDelayMs < 0) StartDelayMs = 0;
        }
    }
}
