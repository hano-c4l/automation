using System.Drawing;
using System.Reflection;

namespace Automation.App
{
    /// <summary>The program's icon (embedded app.ico, the same picture as the exe's icon), for window title bars and the taskbar.</summary>
    internal static class AppIcon
    {
        private static Icon _icon;

        /// <summary>Null if the resource cannot be read (windows then keep the default icon).</summary>
        public static Icon Get()
        {
            if (_icon != null) return _icon;
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (stream != null) _icon = new Icon(stream);
            }
            catch (System.ArgumentException) { }
            return _icon;
        }
    }
}
