using System.Runtime.Serialization;

namespace AutomationTest.Core.Input
{
    [DataContract]
    public enum MouseButton
    {
        [EnumMember] Left,
        [EnumMember] Right,
        [EnumMember] Middle
    }
}
