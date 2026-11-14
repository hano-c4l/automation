using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace AutomationTest.Core.Model
{
    [DataContract]
    public sealed class AutomationScript
    {
        /// <summary>2 = action tree. Version 1 (flat step list) is no longer supported.</summary>
        public const int CurrentVersion = 2;

        [DataMember(Order = 1)] public int Version { get; set; } = CurrentVersion;

        /// <summary>Top-level actions, run once in order.</summary>
        [DataMember(Order = 2)] public List<ActionNode> Actions { get; set; } = new List<ActionNode>();

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Version = CurrentVersion;
            Actions = new List<ActionNode>();
        }

        public AutomationScript Clone()
        {
            return new AutomationScript { Version = Version, Actions = Actions.Select(a => a.Clone()).ToList() };
        }

        internal void Normalize()
        {
            if (Actions == null) Actions = new List<ActionNode>();
            Actions.RemoveAll(a => a == null);
            foreach (var action in Actions) action.Normalize();
            BrowserInheritance.Propagate(Actions);
        }
    }
}
