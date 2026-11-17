using System;
using System.Windows.Forms;
using AutomationTest.Core.Licensing;

namespace AutomationTest.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Only one copy runs at a time: a second start brings the running copy to the front and ends.
            if (!SingleInstance.TryStart()) return;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Nothing but the license screen is shown until a valid license is in place.
            if (!EnsureLicensed()) return;
            AutomationTest.Core.Excel.ExcelSession.HostFactory = () => new ExcelComHost(); // Excel is driven through COM, which only the program (not Core) knows
            Application.Run(new MainForm { LicenseGuard = EnsureLicensed, SettingsEnabled = true });
        }

        /// <summary>Checks the license; if it is not valid, asks for one. False when the user gives up.</summary>
        private static bool EnsureLicensed()
        {
            LicenseResult result = CheckLicense();
            while (!result.IsValid)
            {
                using (var form = new LicenseForm(true, result))
                {
                    if (form.ShowDialog() != DialogResult.OK) return false;
                }
                result = CheckLicense();
            }
            return true;
        }

        /// <summary>The check; a computer without a MachineGuid is an answer ("not valid") and not a crash, also the second time.</summary>
        private static LicenseResult CheckLicense()
        {
            try { return LicenseManager.Check(); }
            catch (InvalidOperationException ex) { return new LicenseResult { Status = LicenseStatus.Malformed, Message = ex.Message }; }
        }
    }
}
