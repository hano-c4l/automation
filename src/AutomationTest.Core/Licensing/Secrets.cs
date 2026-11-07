namespace AutomationTest.Core.Licensing
{
    /// <summary>
    /// Secret mixed into the keys that protect the request key and the saved state. It is compiled into the program, so it
    /// keeps casual users and the output alone from revealing anything, but it is not a defence against someone who takes the
    /// program apart. Changing it makes old request keys and saved states unreadable.
    /// </summary>
    internal static class Secrets
    {
        public const string Value = "Atm!9fQ2#vL7xRk0Zp4Ws8Yd1Nb6Hc3Je5Tu%Gm";
    }
}
