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
            LicenseResult result;
            try { result = LicenseManager.Check(); }
            catch (InvalidOperationException ex) { result = new LicenseResult { Status = LicenseStatus.Malformed, Message = ex.Message }; }
            while (!result.IsValid)
            {
                using (var form = new LicenseForm(true, result))
                {
                    if (form.ShowDialog() != DialogResult.OK) return false;
                }
                result = LicenseManager.Check();
            }
            return true;
        }
    }
}
