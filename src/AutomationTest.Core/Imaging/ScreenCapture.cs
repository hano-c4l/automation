using System;
using AutomationTest.Core.Native;

namespace AutomationTest.Core.Imaging
{
    /// <summary>Captures part of the desktop with GDI. Coordinates are physical pixels of the virtual desktop.</summary>
    public static class ScreenCapture
    {
        /// <summary>The bounds of the whole virtual desktop (all monitors).</summary>
        public static void GetVirtualScreen(out int left, out int top, out int width, out int height)
        {
            left = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            top = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        }

        /// <exception cref="InvalidOperationException">The capture failed.</exception>
        public static RawImage Capture(int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("The capture area is empty.");

            IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new InvalidOperationException("Cannot access the screen.");
            IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero;
            try
            {
                memory = NativeMethods.CreateCompatibleDC(screen);
                bitmap = NativeMethods.CreateCompatibleBitmap(screen, width, height);
                if (memory == IntPtr.Zero || bitmap == IntPtr.Zero) throw new InvalidOperationException("Cannot create a capture buffer.");

                IntPtr previous = NativeMethods.SelectObject(memory, bitmap);
                bool copied = NativeMethods.BitBlt(memory, 0, 0, width, height, screen, x, y, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
                NativeMethods.SelectObject(memory, previous); // a bitmap must not be selected into a DC for GetDIBits
                if (!copied) throw new InvalidOperationException("Cannot copy the screen contents.");

                var info = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = 40,
                    biWidth = width,
                    biHeight = -height, // negative: top-down rows
                    biPlanes = 1,
                    biBitCount = 32
                };
                var bytes = new byte[checked(width * height * 4)];
                if (NativeMethods.GetDIBits(memory, bitmap, 0, (uint)height, bytes, ref info, 0) == 0)
                    throw new InvalidOperationException("Cannot read the captured pixels.");
                return new RawImage(width, height, bytes);
            }
            finally
            {
                if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
                if (memory != IntPtr.Zero) NativeMethods.DeleteDC(memory);
                NativeMethods.ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
