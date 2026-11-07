using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation.App
{
    /// <summary>
    /// Full-screen translucent overlay covering every monitor. Click to choose a point, Esc to cancel.
    /// The returned point is in physical screen pixels (the process is Per-Monitor DPI aware).
    /// </summary>
    public sealed class PointPickerForm : Form
    {
        private readonly Label _hint = new Label
        {
            AutoSize = true,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Padding = new Padding(6),
            Text = "Click to pick a point, Esc to cancel"
        };

        public Point SelectedPoint { get; private set; }

        /// <summary>When true, a red frame follows the window under the mouse (used when picking a process).</summary>
        public bool HighlightWindows { get; set; }

        private WindowOutlineForm _outline;
        private IntPtr _outlined;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int max);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
        private const uint GW_HWNDNEXT = 2;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;

        /// <summary>The top-level window below this overlay that contains the screen point (own windows are skipped); Zero with the whole screen as bounds when it is the desktop.</summary>
        private IntPtr WindowBelow(Point p, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            for (IntPtr h = GetWindow(Handle, GW_HWNDNEXT); h != IntPtr.Zero; h = GetWindow(h, GW_HWNDNEXT))
            {
                if (!IsWindowVisible(h) || IsIconic(h)) continue;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == ownPid) continue;
                if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
                RECT r;
                if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0 && !GetWindowRect(h, out r)) continue;
                var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                if (rect.Width <= 0 || rect.Height <= 0 || !rect.Contains(p)) continue;
                if (Automation.Core.Windows.WindowLocator.IsDesktopSurface(h)) break; // desktop, taskbar, Start: the full desktop
                bounds = rect;
                return h;
            }
            bounds = SystemInformation.VirtualScreen; // nothing but the desktop under the mouse
            return IntPtr.Zero;
        }

        private void UpdateOutline(Point p)
        {
            if (!HighlightWindows) return;
            IntPtr h = WindowBelow(p, out Rectangle bounds);
            if (_outline == null) _outline = new WindowOutlineForm();
            if (h == _outlined && _outline.Visible && _outline.Bounds == bounds) return;
            _outlined = h;
            _outline.Outline(bounds);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_outline != null) { _outline.Dispose(); _outline = null; }
            base.OnFormClosed(e);
        }

        private readonly Bitmap _frozen;

        /// <summary>A rectangle (screen pixels) drawn on the frozen picture to show where a point is accepted, e.g. the program's client area.</summary>
        public Rectangle? AcceptArea { get; set; }

        /// <summary>Says what the shown coordinates are measured from (for example "notepad TopLeft"); null = the screen.</summary>
        public string CoordinateLabel { get; set; }

        /// <summary>Turns a screen point into the coordinates shown and stored (relative to a window); null = screen coordinates.</summary>
        public Func<Point, Point> Relative { get; set; }

        /// <param name="frozen">A picture of the whole virtual desktop taken before the overlay opened; when given it is shown instead of the live screen, so nothing changes under the pointer (pop-ups closing, windows shrinking when they lose focus).</param>
        public PointPickerForm(Bitmap frozen = null)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            _frozen = frozen;
            Opacity = frozen == null ? 0.25 : 1.0;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            Bounds = SystemInformation.VirtualScreen;
            Controls.Add(_hint);

            MouseMove += (s, e) => UpdateHint();
            MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                SelectedPoint = MousePosition;
                DialogResult = DialogResult.OK;
            };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = SystemInformation.VirtualScreen;
            Activate();
            UpdateHint();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_frozen == null) return;
            e.Graphics.DrawImageUnscaled(_frozen, 0, 0);
            if (!AcceptArea.HasValue) return;
            // Dim everything outside the accepted area a little and frame the area, so it is clear where a click counts.
            var area = new Rectangle(PointToClient(AcceptArea.Value.Location), AcceptArea.Value.Size);
            using (var dim = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                var clip = e.Graphics.Save();
                e.Graphics.SetClip(area, System.Drawing.Drawing2D.CombineMode.Exclude);
                e.Graphics.FillRectangle(dim, ClientRectangle);
                e.Graphics.Restore(clip);
            }
            using (var pen = new Pen(Color.Red, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                e.Graphics.DrawRectangle(pen, area);
        }

        private void UpdateHint()
        {
            var p = MousePosition;
            if (Relative != null)
            {
                var r = Relative(p);
                bool outside = AcceptArea.HasValue && !AcceptArea.Value.Contains(p);
                _hint.Text = CoordinateLabel + ":  X=" + r.X + "  Y=" + r.Y + (outside ? "  (outside the window)" : "") + "   (click to pick, Esc to cancel)";
            }
            else
            {
                _hint.Text = "Screen:  X=" + p.X + "  Y=" + p.Y + "   (click to pick, Esc to cancel)";
            }
            UpdateOutline(p);
            var client = PointToClient(p);
            _hint.Location = new Point(client.X + 20, client.Y + 20);
        }
    }
}
