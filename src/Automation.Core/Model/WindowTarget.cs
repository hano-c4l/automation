using System.Runtime.Serialization;

namespace Automation.Core.Model
{
    /// <summary>Describes which window Window-mode steps are relative to.</summary>
    [DataContract]
    public sealed class WindowTarget
    {
        /// <summary>Process name without ".exe" (case-insensitive). Empty matches any process.</summary>
        [DataMember(Order = 1)] public string ProcessName { get; set; }

        /// <summary>Regular expression matched against the window title. Empty matches any title.</summary>
        [DataMember(Order = 2)] public string TitlePattern { get; set; }

        /// <summary>Restore and activate the window before the script starts.</summary>
        [DataMember(Order = 3)] public bool BringToFront { get; set; }

        public bool IsEmpty
        {
            get { return string.IsNullOrWhiteSpace(ProcessName) && string.IsNullOrWhiteSpace(TitlePattern); }
        }

        public WindowTarget Clone()
        {
            return (WindowTarget)MemberwiseClone();
        }
    }
}
