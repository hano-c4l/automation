using System;
using System.Diagnostics;

namespace AutomationTest.Core.Runner
{
    /// <summary>Shuts the computer down.</summary>
    public interface ISystemShutdown
    {
        /// <param name="force">Close other programs even if they have unsaved work.</param>
        void Shutdown(bool force);
    }

    /// <summary>Real shutdown through shutdown.exe.</summary>
    public sealed class WindowsShutdown : ISystemShutdown
    {
        public void Shutdown(bool force)
        {
            try
            {
                var info = new ProcessStartInfo("shutdown.exe", force ? "/s /f /t 0" : "/s /t 0")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = Process.Start(info))
                {
                    if (process != null && process.WaitForExit(10000) && process.ExitCode != 0)
                        throw new InvalidOperationException("Cannot shut down the computer: shutdown.exe returned code " + process.ExitCode + ".");
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                throw new InvalidOperationException("Cannot shut down the computer: " + ex.Message);
            }
        }
    }
}
