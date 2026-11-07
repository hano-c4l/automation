using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AutomationTest.App
{
    /// <summary>
    /// A toolbar button that shows one icon glyph in its own colour with a soft shadow, and a raised look when the mouse is over it
    /// (sunk when pressed). A disabled button is pale grey; a button that stands for something going on (Stop) can be set to red.
    /// </summary>
    internal sealed class IconButton : ToolStripButton
    {
        private static readonly Color DisabledColor = Color.FromArgb(190, 190, 195);
        private static readonly Color StopColor = Color.FromArgb(220, 38, 38);

        public IconButton(string text) : base(text)
        {
        }

        /// <summary>The colour of the icon.</summary>
        public Color BaseColor { get; set; } = Color.FromArgb(60, 60, 70);

        /// <summary>True: drawn red, to show that this button now stops what is going on.</summary>
        public bool Highlight { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var face = new Rectangle(1, 1, Width - 3, Height - 3);
            bool pressed = Enabled && Pressed, raised = Enabled && Selected && !Pressed;
            if (raised || pressed)
            {
                using (var path = Rounded(face, 5))
                {
                    Color top = pressed ? Color.FromArgb(214, 220, 232) : Color.FromArgb(255, 255, 255);
                    Color bottom = pressed ? Color.FromArgb(236, 240, 248) : Color.FromArgb(222, 229, 242);
                    using (var fill = new LinearGradientBrush(face, top, bottom, 90f))
                        g.FillPath(fill, path);
                    using (var border = new Pen(Color.FromArgb(pressed ? 150 : 175, 160, 175, 200)))
                        g.DrawPath(border, path);
                }
            }

            Color color = !Enabled ? DisabledColor : Highlight ? StopColor : BaseColor;
            var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
            var area = new RectangleF(face.X, face.Y, face.Width, face.Height);
            float nudge = pressed ? 1f : 0f; // sinks a little when pressed
            var textArea = new RectangleF(area.X + nudge, area.Y + nudge, area.Width, area.Height);

            if (Enabled)
            {
                // Soft shadow underneath, then the glyph with a light top and a darker bottom, so the icon looks slightly raised.
                var shadowArea = new RectangleF(textArea.X + 1f, textArea.Y + 1.5f, textArea.Width, textArea.Height);
                using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                    g.DrawString(Text, Font, shadow, shadowArea, format);
            }
            using (var brush = Enabled
                ? (Brush)new LinearGradientBrush(textArea, Mix(color, Color.White, 0.30f), Mix(color, Color.Black, 0.18f), 90f)
                : new SolidBrush(color))
                g.DrawString(Text, Font, brush, textArea, format);
        }

        private static Color Mix(Color a, Color b, float amount)
        {
            return Color.FromArgb(a.A, (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
