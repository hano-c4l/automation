using System;
using System.Windows.Forms;

namespace AutomationTest.Tests.Dummy
{
    /// <summary>
    /// Usage: AtDummyApp "window title" [refuse]. Shows one window with that title. With "refuse" it does not close when asked
    /// (like a program that asks whether to save), so only Kill ends it.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var form = new Form { Text = args.Length > 0 ? args[0] : "AtDummy", Width = 200, Height = 100, ShowInTaskbar = false };
            bool refuse = args.Length > 1 && args[1] == "refuse";
            form.FormClosing += (s, e) => { if (refuse) e.Cancel = true; };
            Application.Run(form);
        }
    }
}
