using System.Runtime.Serialization;

namespace AutomationTest.Core.Model
{
    /// <summary>Identifies a program window by process name and window title.</summary>
    [DataContract]
    public sealed class WindowTarget
    {
        /// <summary>Process name without ".exe" (case-insensitive). Empty matches any process.</summary>
        [DataMember(Order = 1)] public string ProcessName { get; set; }

        /// <summary>Regular expression matched against the window title. Empty matches any title.</summary>
        [DataMember(Order = 2)] public string TitlePattern { get; set; }

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
