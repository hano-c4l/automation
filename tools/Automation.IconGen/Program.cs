using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Draws the Automation icon (a cursor with click ripples on a rounded blue tile) at several sizes and writes app.ico.
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

            // Tile
            float margin = 8 * s, radius = 52 * s;
            var tile = new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin);
            using (var path = Rounded(tile, radius))
            using (var fill = new LinearGradientBrush(tile, Color.FromArgb(255, 37, 99, 235), Color.FromArgb(255, 6, 182, 212), 55f))
                g.FillPath(fill, path);

            // Click ripples around the tip (upper left of the cursor)
            float cx = 98 * s, cy = 92 * s;
            using (var pen1 = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1.2f, 9 * s)))
            using (var pen2 = new Pen(Color.FromArgb(130, 255, 255, 255), Math.Max(1f, 7 * s)))
            {
                float r1 = 40 * s, r2 = 68 * s;
                if (size >= 32) g.DrawEllipse(pen2, cx - r2, cy - r2, 2 * r2, 2 * r2);
                g.DrawEllipse(pen1, cx - r1, cy - r1, 2 * r1, 2 * r1);
            }

            // Cursor arrow (tip at the ripple centre), white with a dark outline
            var tip = new PointF(cx, cy);
            PointF[] arrow =
            {
                P(tip, 0, 0, s), P(tip, 0, 132, s), P(tip, 30, 104, s), P(tip, 52, 150, s),
                P(tip, 76, 139, s), P(tip, 54, 94, s), P(tip, 94, 94, s)
            };
            using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                var moved = new PointF[arrow.Length];
                for (int i = 0; i < arrow.Length; i++) moved[i] = new PointF(arrow[i].X + 5 * s, arrow[i].Y + 6 * s);
                g.FillPolygon(shadow, moved);
            }
            using (var body = new SolidBrush(Color.White))
                g.FillPolygon(body, arrow);
            using (var outline = new Pen(Color.FromArgb(255, 15, 23, 42), Math.Max(1f, 8 * s)) { LineJoin = LineJoin.Round })
                g.DrawPolygon(outline, arrow);
        }
        return bmp;
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
