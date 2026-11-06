using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation.App
{
    /// <summary>A help text shown when the user clicks a "?" icon.</summary>
    public sealed class HelpTopic
    {
        public HelpTopic(string title, string text)
        {
            Title = title;
            Text = text;
        }

        public string Title { get; }
        public string Text { get; }
    }

    /// <summary>A small round "?" that opens a window explaining the field it sits next to.</summary>
    internal sealed class HelpIcon : Control
    {
        private readonly HelpTopic _topic;
        private bool _hot;

        public HelpIcon(HelpTopic topic)
        {
            _topic = topic;
            Size = new Size(20, 20);
            Margin = new Padding(4, 3, 0, 0);
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
            AccessibleName = "Help: " + topic.Title;
            AccessibleRole = AccessibleRole.PushButton;
            new ToolTip().SetToolTip(this, "Click for help: " + topic.Title);
        }

        protected override void OnMouseEnter(EventArgs e) { _hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hot = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            HelpForm.Show(FindForm(), _topic);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(1, 1, Width - 3, Height - 3);
            Color accent = _hot ? Color.FromArgb(0, 90, 180) : Color.FromArgb(70, 120, 190);
            using (var fill = new SolidBrush(_hot ? Color.FromArgb(225, 238, 252) : Color.White))
                e.Graphics.FillEllipse(fill, rect);
            using (var pen = new Pen(accent, 1.5f))
                e.Graphics.DrawEllipse(pen, rect);
            using (var font = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var brush = new SolidBrush(accent))
            {
                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString("?", font, brush, new RectangleF(0, 0, Width, Height - 1), format);
            }
        }
    }

    /// <summary>The window that shows a help topic.</summary>
    internal static class HelpForm
    {
        public static void Show(Form owner, HelpTopic topic)
        {
            using (var form = new Form
            {
                Text = "Help - " + topic.Title,
                StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(560, 460),
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.FixedDialog
            })
            {
                var box = new TextBox
                {
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Dock = DockStyle.Fill,
                    Font = new Font("Consolas", 9.5f),
                    BackColor = SystemColors.Window,
                    BorderStyle = BorderStyle.None,
                    Text = topic.Text.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                    TabStop = false
                };
                var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
                form.Controls.Add(box);
                form.Controls.Add(close);
                form.AcceptButton = close;
                form.CancelButton = close;
                form.Shown += (s, e) => box.Select(0, 0);
                form.ShowDialog(owner);
            }
        }
    }
}
