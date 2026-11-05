using System;
using System.IO;
using System.Threading;
using Automation.Core.Model;
using Automation.Core.Windows;

namespace Automation.Core.Imaging
{
    public sealed class LocateResult
    {
        public bool Found;

        /// <summary>Best similarity (0..1); -1 if nothing could be compared.</summary>
        public double Score = -1;

        /// <summary>Where the picture was found, in screen pixels (the Left/Top/Width/Height of the match).</summary>
        public ClientArea Rect;

        /// <summary>The area that was searched, in screen pixels.</summary>
        public ClientArea Searched;

        /// <summary>The client area of the Process window when one was used (null for a desktop search).</summary>
        public ClientArea? Window;
    }

    /// <summary>
    /// The single place that turns an Image action's settings into a screen search: finds and activates the window,
    /// applies anchor and range, captures the screen and matches. The runner and the editor both use it.
    /// </summary>
    public static class ImageLocator
    {
        private const int SettleAfterActivateMs = 120;

        /// <param name="process">Window the range is relative to; empty or null means the desktop.</param>
        /// <exception cref="InvalidOperationException">No picture is set or it is unusable, or the window cannot be found or shown.</exception>
        /// <exception cref="ArgumentException">The title pattern is not a valid regular expression.</exception>
        public static LocateResult Locate(ImageSettings settings, WindowTarget process, CancellationToken token = default)
        {
            RawImage needle;
            try
            {
                needle = settings.GetImage();
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException("Image: " + ex.Message + " Capture or load the picture again.");
            }

            ClientArea range = ResolveRange(settings, process, out ClientArea? window);
            ScreenCapture.GetVirtualScreen(out int vl, out int vt, out int vw, out int vh);
            range = Intersect(range, new ClientArea { Left = vl, Top = vt, Width = vw, Height = vh });

            var result = new LocateResult { Searched = range, Window = window };
            if (range.Width <= 0 || range.Height <= 0 || needle.Width > range.Width || needle.Height > range.Height)
                return result; // nothing to search, or the picture does not fit in the range

            token.ThrowIfCancellationRequested();
            RawImage haystack = ScreenCapture.Capture(range.Left, range.Top, range.Width, range.Height);
            MatchResult match;
            try
            {
                match = ImageMatcher.Find(haystack, needle, settings.Accuracy / 100.0, token);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Image: " + ex.Message);
            }

            result.Score = match.Score;
            result.Found = match.Found;
            if (match.Found)
                result.Rect = new ClientArea { Left = range.Left + match.X, Top = range.Top + match.Y, Width = needle.Width, Height = needle.Height };
            return result;
        }

        private static ClientArea ResolveRange(ImageSettings s, WindowTarget process, out ClientArea? window)
        {
            window = null;
            bool whole = s.RangeWidth <= 0 || s.RangeHeight <= 0;
            if (process == null || process.IsEmpty)
            {
                if (whole)
                {
                    ScreenCapture.GetVirtualScreen(out int l, out int t, out int w, out int h);
                    return new ClientArea { Left = l, Top = t, Width = w, Height = h };
                }
                return new ClientArea { Left = s.RangeX, Top = s.RangeY, Width = s.RangeWidth, Height = s.RangeHeight };
            }

            IntPtr hwnd = WindowLocator.Find(process);
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Window not found: process \"" + process.ProcessName + "\", title \"" + process.TitlePattern + "\"");
            if (!WindowLocator.IsActive(hwnd) || WindowLocator.IsMinimized(hwnd))
            {
                WindowLocator.BringToFront(hwnd);
                Thread.Sleep(SettleAfterActivateMs);
                if (WindowLocator.IsMinimized(hwnd))
                    throw new InvalidOperationException("The target window is minimized and could not be restored.");
            }
            ClientArea area = WindowLocator.GetClientArea(hwnd);
            window = area;
            if (whole) return area;

            WindowLocator.ToScreen(area, s.Anchor, s.RangeX, s.RangeY, out int x, out int y);
            return new ClientArea { Left = x, Top = y, Width = s.RangeWidth, Height = s.RangeHeight };
        }

        private static ClientArea Intersect(ClientArea a, ClientArea b)
        {
            int left = Math.Max(a.Left, b.Left), top = Math.Max(a.Top, b.Top);
            int right = Math.Min(a.Left + a.Width, b.Left + b.Width), bottom = Math.Min(a.Top + a.Height, b.Top + b.Height);
            return new ClientArea { Left = left, Top = top, Width = Math.Max(0, right - left), Height = Math.Max(0, bottom - top) };
        }
    }
}
