using System;
using System.Collections.Generic;
using System.Threading;

namespace AutomationTest.Core.Imaging
{
    public struct MatchResult
    {
        public bool Found;
        public int X;
        public int Y;
        /// <summary>Zero-mean normalized cross-correlation of the best position, -1..1 (1 = identical pattern).</summary>
        public double Score;
    }

    /// <summary>
    /// Finds a small image inside a larger one. The score is zero-mean normalized cross-correlation, so it compares the
    /// pattern and ignores overall brightness: a blank button never matches a button with text on it, and a slightly
    /// brighter or noisier copy of the right button still does. The search runs on downscaled copies first, then refines
    /// the best candidates at full resolution.
    /// </summary>
    public static class ImageMatcher
    {
        /// <summary>Standard deviation (0..255) below which a template has no pattern to look for.</summary>
        public const double MinDetail = 4.0;

        private const int CandidateCount = 5;

        public static bool HasEnoughDetail(RawImage image)
        {
            float[] gray = image.ToGray();
            double sum = 0, sumSq = 0;
            foreach (float v in gray) { sum += v; sumSq += (double)v * v; }
            double mean = sum / gray.Length;
            return Math.Sqrt(Math.Max(0, sumSq / gray.Length - mean * mean)) >= MinDetail;
        }

        /// <param name="accuracy">Minimum score, 0..1.</param>
        /// <exception cref="InvalidOperationException">The template has no pattern (nearly one colour).</exception>
        public static MatchResult Find(RawImage haystack, RawImage needle, double accuracy, CancellationToken token = default)
        {
            var none = new MatchResult { Found = false, Score = -1 };
            if (needle.Width > haystack.Width || needle.Height > haystack.Height) return none;
            if (!HasEnoughDetail(needle))
                throw new InvalidOperationException("The image has too little detail to search for (nearly one colour).");

            float[] hay = haystack.ToGray();
            float[] tpl = needle.ToGray();

            int factor = Math.Max(1, Math.Min(4, Math.Min(needle.Width, needle.Height) / 8));
            var candidates = CoarseSearch(hay, haystack.Width, haystack.Height, tpl, needle.Width, needle.Height, factor, token);
            if (candidates.Count == 0 && factor > 1)
            {
                factor = 1; // the downscaled template went flat; search at full resolution
                candidates = CoarseSearch(hay, haystack.Width, haystack.Height, tpl, needle.Width, needle.Height, 1, token);
            }

            double bestScore = -1;
            int bestX = 0, bestY = 0;
            float[] zeroMean = ZeroMean(tpl, out double tplNorm);
            foreach (var c in candidates)
            {
                for (int dy = -factor; dy <= factor; dy++)
                {
                    for (int dx = -factor; dx <= factor; dx++)
                    {
                        token.ThrowIfCancellationRequested();
                        int x = c.X * factor + dx, y = c.Y * factor + dy;
                        if (x < 0 || y < 0 || x + needle.Width > haystack.Width || y + needle.Height > haystack.Height) continue;
                        double score = Zncc(hay, haystack.Width, x, y, zeroMean, tplNorm, needle.Width, needle.Height);
                        if (score > bestScore) { bestScore = score; bestX = x; bestY = y; }
                    }
                }
            }

            if (bestScore < 0) return none;
            bool found = bestScore >= accuracy && ColourClose(haystack, needle, bestX, bestY, accuracy);
            return new MatchResult { Found = found, X = bestX, Y = bestY, Score = bestScore };
        }

        private struct Candidate
        {
            public int X, Y;
            public double Score;
        }

        /// <summary>Scores every position on downscaled images and keeps the best few that are not neighbours of each other.</summary>
        private static List<Candidate> CoarseSearch(float[] hay, int hw, int hh, float[] tpl, int tw, int th, int factor, CancellationToken token)
        {
            int w2 = hw, h2 = hh, tw2 = tw, th2 = th;
            float[] h = hay, t = tpl;
            if (factor > 1)
            {
                h = BoxDown(hay, hw, hh, factor, out w2, out h2);
                t = BoxDown(tpl, tw, th, factor, out tw2, out th2);
            }

            var result = new List<Candidate>();
            if (tw2 > w2 || th2 > h2) return result;

            float[] tz = ZeroMean(t, out double norm);
            if (norm < 1e-6) return result;

            // Integral images give each window's sum and sum of squares in O(1).
            int stride = w2 + 1;
            var sum = new double[stride * (h2 + 1)];
            var sumSq = new double[stride * (h2 + 1)];
            for (int y = 0; y < h2; y++)
            {
                double rowSum = 0, rowSq = 0;
                for (int x = 0; x < w2; x++)
                {
                    double v = h[y * w2 + x];
                    rowSum += v;
                    rowSq += v * v;
                    sum[(y + 1) * stride + x + 1] = sum[y * stride + x + 1] + rowSum;
                    sumSq[(y + 1) * stride + x + 1] = sumSq[y * stride + x + 1] + rowSq;
                }
            }

            double n = (double)tw2 * th2;
            int radius = Math.Max(2, Math.Min(tw2, th2) / 2);
            for (int y = 0; y + th2 <= h2; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x + tw2 <= w2; x++)
                {
                    double s = Rect(sum, stride, x, y, tw2, th2);
                    double variance = Rect(sumSq, stride, x, y, tw2, th2) - s * s / n;
                    if (variance < 1e-3 * n) continue; // flat window: no pattern

                    double dot = 0;
                    for (int j = 0; j < th2; j++)
                    {
                        int hi = (y + j) * w2 + x, ti = j * tw2;
                        for (int i = 0; i < tw2; i++) dot += h[hi + i] * tz[ti + i];
                    }
                    Offer(result, new Candidate { X = x, Y = y, Score = dot / (Math.Sqrt(variance) * norm) }, radius);
                }
            }
            return result;
        }

        private static void Offer(List<Candidate> list, Candidate c, int radius)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (Math.Abs(list[i].X - c.X) <= radius && Math.Abs(list[i].Y - c.Y) <= radius)
                {
                    if (c.Score > list[i].Score) list[i] = c; // same spot, keep the better position
                    return;
                }
            }
            if (list.Count < CandidateCount) { list.Add(c); return; }
            int worst = 0;
            for (int i = 1; i < list.Count; i++) if (list[i].Score < list[worst].Score) worst = i;
            if (c.Score > list[worst].Score) list[worst] = c;
        }

        private static double Rect(double[] integral, int stride, int x, int y, int w, int h)
        {
            return integral[(y + h) * stride + x + w] - integral[y * stride + x + w] - integral[(y + h) * stride + x] + integral[y * stride + x];
        }

        private static float[] ZeroMean(float[] values, out double norm)
        {
            double mean = 0;
            foreach (float v in values) mean += v;
            mean /= values.Length;
            var result = new float[values.Length];
            double sq = 0;
            for (int i = 0; i < values.Length; i++)
            {
                result[i] = (float)(values[i] - mean);
                sq += result[i] * (double)result[i];
            }
            norm = Math.Sqrt(sq);
            return result;
        }

        /// <summary>Full-resolution score of the template (given zero-mean) at a position.</summary>
        private static double Zncc(float[] hay, int hw, int x, int y, float[] tz, double tplNorm, int tw, int th)
        {
            double sum = 0, sumSq = 0;
            for (int j = 0; j < th; j++)
            {
                int hi = (y + j) * hw + x;
                for (int i = 0; i < tw; i++)
                {
                    double v = hay[hi + i];
                    sum += v;
                    sumSq += v * v;
                }
            }
            double n = (double)tw * th;
            double variance = sumSq - sum * sum / n;
            if (variance < 1e-3 * n || tplNorm < 1e-6) return -1;

            double dot = 0;
            for (int j = 0; j < th; j++)
            {
                int hi = (y + j) * hw + x, ti = j * tw;
                for (int i = 0; i < tw; i++) dot += hay[hi + i] * tz[ti + i];
            }
            return dot / (Math.Sqrt(variance) * tplNorm);
        }

        private static float[] BoxDown(float[] src, int w, int h, int factor, out int w2, out int h2)
        {
            w2 = w / factor;
            h2 = h / factor;
            var dst = new float[w2 * h2];
            float area = factor * factor;
            for (int y = 0; y < h2; y++)
            {
                for (int x = 0; x < w2; x++)
                {
                    float total = 0;
                    for (int j = 0; j < factor; j++)
                    {
                        int row = (y * factor + j) * w + x * factor;
                        for (int i = 0; i < factor; i++) total += src[row + i];
                    }
                    dst[y * w2 + x] = total / area;
                }
            }
            return dst;
        }

        /// <summary>Rejects a same-shaped match whose colours are clearly different (for example a red and a green button).</summary>
        private static bool ColourClose(RawImage hay, RawImage needle, int x, int y, double accuracy)
        {
            double[] hm = new double[3], nm = new double[3];
            for (int j = 0; j < needle.Height; j++)
            {
                int hi = ((y + j) * hay.Width + x) * 4, ni = j * needle.Width * 4;
                for (int i = 0; i < needle.Width; i++, hi += 4, ni += 4)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        hm[c] += hay.Bgra[hi + c];
                        nm[c] += needle.Bgra[ni + c];
                    }
                }
            }
            double count = (double)needle.Width * needle.Height;
            double allowed = 255 * (1 - accuracy) * 2 + 5;
            for (int c = 0; c < 3; c++)
                if (Math.Abs(hm[c] - nm[c]) / count > allowed) return false;
            return true;
        }
    }
}
