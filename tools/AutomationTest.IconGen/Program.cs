using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Draws the Automation Test icon (circular arrows round a gear with a check mark: automation plus test) at several sizes and writes app.ico.
internal static class Program
{
    // Line-art icon in a cyan -> blue -> violet -> magenta gradient: two circular arrows (an endless loop, "automation") around
    // a gear whose hub holds a check mark ("test"). The background is transparent.
    private static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float s = size / 256f;
            float thick = size <= 32 ? 1.7f : size <= 64 ? 1.25f : 1f; // small sizes need bolder lines to stay readable

            var area = new RectangleF(0, 0, size, size);
            using (var brush = new LinearGradientBrush(area, Color.FromArgb(6, 182, 212), Color.FromArgb(217, 70, 239), 50f))
            {
                brush.InterpolationColors = new ColorBlend
                {
                    Positions = new[] { 0f, 0.38f, 0.7f, 1f },
                    Colors = new[] { Color.FromArgb(6, 200, 220), Color.FromArgb(59, 130, 246), Color.FromArgb(139, 92, 246), Color.FromArgb(217, 70, 239) }
                };
                Pen Line(float width) { return new Pen(brush, Math.Max(1.2f, width * s * thick)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round }; }

                float cx = size / 2f, cy = size / 2f;

                // Two circular arrows, each ending in an arrow head that points the way round
                using (var pen = Line(12))
                {
                    float r = 112 * s;
                    DrawArrowArc(g, pen, cx, cy, r, 198, 128, 34 * s * (thick > 1 ? 0.8f : 1f));
                    DrawArrowArc(g, pen, cx, cy, r, 18, 128, 34 * s * (thick > 1 ? 0.8f : 1f));
                }

                // Gear: rounded outline with eight teeth
                using (var pen = Line(11))
                using (var gear = Gear(cx, cy, 68 * s, 53 * s, 8, 0.30f))
                    g.DrawPath(pen, gear);

                // Hub with the check mark
                using (var pen = Line(11))
                {
                    float hub = 31 * s;
                    if (size > 32) g.DrawEllipse(pen, cx - hub, cy - hub, 2 * hub, 2 * hub);
                    g.DrawLines(pen, new[] { new PointF(cx - 15 * s, cy + 1 * s), new PointF(cx - 4 * s, cy + 13 * s), new PointF(cx + 17 * s, cy - 13 * s) });
                }
            }
        }
        return bmp;
    }

    /// <summary>An arc round (cx, cy) with an arrow head at its far end (clockwise); angles in degrees, GDI style (0 = right, 90 = down).</summary>
    private static void DrawArrowArc(Graphics g, Pen pen, float cx, float cy, float r, float startDeg, float sweepDeg, float head)
    {
        g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, startDeg, sweepDeg);
        double end = (startDeg + sweepDeg) * Math.PI / 180.0;
        var tip = new PointF(cx + (float)(r * Math.Cos(end)), cy + (float)(r * Math.Sin(end)));
        double tangent = end + Math.PI / 2; // clockwise direction of travel
        foreach (double spread in new[] { 0.72, -0.72 })
        {
            double back = tangent + Math.PI + spread;
            g.DrawLine(pen, tip, new PointF(tip.X + (float)(head * Math.Cos(back)), tip.Y + (float)(head * Math.Sin(back))));
        }
    }

    /// <summary>A gear outline: <paramref name="toothShare"/> is the part of each tooth pitch taken by the tooth top.</summary>
    private static GraphicsPath Gear(float cx, float cy, float outer, float root, int teeth, float toothShare)
    {
        var path = new GraphicsPath();
        var points = new List<PointF>();
        double pitch = 2 * Math.PI / teeth;
        for (int i = 0; i < teeth; i++)
        {
            double a = i * pitch - Math.PI / 2;
            double top = pitch * toothShare / 2;
            double foot = pitch * (toothShare / 2 + 0.14);
            points.Add(Polar(cx, cy, root, a - foot));
            points.Add(Polar(cx, cy, outer, a - top));
            points.Add(Polar(cx, cy, outer, a + top));
            points.Add(Polar(cx, cy, root, a + foot));
        }
        path.AddPolygon(points.ToArray());
        return path;
    }

    private static PointF Polar(float cx, float cy, float r, double angle)
    {
        return new PointF(cx + (float)(r * Math.Cos(angle)), cy + (float)(r * Math.Sin(angle)));
    }

    private static PointF P(PointF origin, float x, float y, float s)
    {
        return new PointF(origin.X + x * s, origin.Y + y * s);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    // 32-bit BMP entry (BITMAPINFOHEADER with doubled height, pixels bottom-up, then an all-zero AND mask).
    private static byte[] Dib(Bitmap bmp)
    {
        int n = bmp.Width;
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(40); w.Write(n); w.Write(n * 2); w.Write((short)1); w.Write((short)32);
            w.Write(0); w.Write(n * n * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int y = n - 1; y >= 0; y--)
                for (int x = 0; x < n; x++) w.Write(bmp.GetPixel(x, y).ToArgb());
            int maskRow = ((n + 31) / 32) * 4;
            w.Write(new byte[maskRow * n]);
            return ms.ToArray();
        }
    }

    private static void Main(string[] args)
    {
        string output = args.Length > 0 ? args[0] : "app.ico";
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (var bmp = Render(size))
            {
                images.Add(size >= 256 ? Png(bmp) : Dib(bmp));
                if (size == 256) bmp.Save(Path.ChangeExtension(output, ".png"), ImageFormat.Png);
            }
        }
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var data in images) w.Write(data);
            File.WriteAllBytes(output, ms.ToArray());
        }
        Console.WriteLine("Wrote " + output);
    }
}
