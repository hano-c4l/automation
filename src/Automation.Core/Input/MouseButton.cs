using System.Runtime.Serialization;

namespace Automation.Core.Input
{
    [DataContract]
    public enum MouseButton
    {
        [EnumMember] Left,
        [EnumMember] Right,
        [EnumMember] Middle
    }
}
