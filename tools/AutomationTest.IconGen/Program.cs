using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Draws the AutomationTest icon (a gear with a check mark: automation plus test) at several sizes and writes app.ico.
internal static class Program
{
    private static Bitmap Render(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float s = size / 256f;

            // Tile: deep indigo to violet
            float margin = 8 * s, radius = 52 * s;
            var tile = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);
            using (var path = Rounded(tile, radius))
            using (var fill = new LinearGradientBrush(tile, Color.FromArgb(255, 49, 46, 129), Color.FromArgb(255, 124, 58, 237), 55f))
                g.FillPath(fill, path);

            // Gear (automation): white, eight teeth, with a hole
            float cx = 108 * s, cy = 108 * s;
            using (var gear = Gear(cx, cy, 88 * s, 68 * s, 8, 0.36f, 30 * s))
            using (var body = new SolidBrush(Color.White))
                g.FillPath(body, gear);

            // Check mark badge (test): green disc with a white tick, ringed in the tile colour so it stands out from the gear
            float bx = 176 * s, by = 176 * s, br = 54 * s;
            using (var ring = new SolidBrush(Color.FromArgb(255, 67, 56, 202)))
                g.FillEllipse(ring, bx - br - 9 * s, by - br - 9 * s, 2 * (br + 9 * s), 2 * (br + 9 * s));
            using (var green = new SolidBrush(Color.FromArgb(255, 34, 197, 94)))
                g.FillEllipse(green, bx - br, by - br, 2 * br, 2 * br);
            using (var tick = new Pen(Color.White, Math.Max(1.5f, 14 * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(tick, new[] { new PointF(bx - 28 * s, by + 2 * s), new PointF(bx - 7 * s, by + 23 * s), new PointF(bx + 29 * s, by - 20 * s) });
        }
        return bmp;
    }

    /// <summary>A gear outline with a round hole; <paramref name="toothShare"/> is the part of each tooth pitch taken by the tooth top.</summary>
    private static GraphicsPath Gear(float cx, float cy, float outer, float root, int teeth, float toothShare, float hole)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        var points = new List<PointF>();
        double pitch = 2 * Math.PI / teeth;
        for (int i = 0; i < teeth; i++)
        {
            double a = i * pitch;
            double top = pitch * toothShare / 2;       // half width of the tooth top
            double foot = pitch * (toothShare / 2 + 0.12); // the tooth is wider at its foot
            points.Add(Polar(cx, cy, root, a - foot - 0.0));
            points.Add(Polar(cx, cy, outer, a - top));
            points.Add(Polar(cx, cy, outer, a + top));
            points.Add(Polar(cx, cy, root, a + foot));
        }
        path.AddPolygon(points.ToArray());
        path.AddEllipse(cx - hole, cy - hole, 2 * hole, 2 * hole);
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
