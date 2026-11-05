using System;
using System.IO;
using System.IO.Compression;

namespace Automation.Core.Imaging
{
    /// <summary>
    /// A bitmap held as plain BGRA bytes (4 bytes per pixel, top row first). Core avoids System.Drawing so it stays
    /// portable; the UI converts to and from Bitmap.
    /// </summary>
    public sealed class RawImage
    {
        public RawImage(int width, int height, byte[] bgra)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("The image is empty.");
            if (bgra == null || bgra.Length != checked(width * height * 4)) throw new ArgumentException("The pixel data does not match the image size.");
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Bgra { get; }

        /// <summary>Luma (0..255) per pixel.</summary>
        public float[] ToGray()
        {
            var gray = new float[Width * Height];
            for (int i = 0, p = 0; i < gray.Length; i++, p += 4)
                gray[i] = 0.114f * Bgra[p] + 0.587f * Bgra[p + 1] + 0.299f * Bgra[p + 2];
            return gray;
        }

        /// <summary>Deflate-compressed pixels as base64, for storing inside a script file.</summary>
        public string Encode()
        {
            using (var ms = new MemoryStream())
            {
                using (var deflate = new DeflateStream(ms, CompressionMode.Compress, true))
                    deflate.Write(Bgra, 0, Bgra.Length);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        /// <exception cref="InvalidDataException">The data is not a valid image of the given size.</exception>
        public static RawImage Decode(int width, int height, string base64)
        {
            try
            {
                if (width <= 0 || height <= 0 || string.IsNullOrEmpty(base64)) throw new InvalidDataException("The image is missing.");
                int length = checked(width * height * 4);
                var bytes = new byte[length];
                using (var ms = new MemoryStream(Convert.FromBase64String(base64)))
                using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
                {
                    int read = 0;
                    while (read < length)
                    {
                        int n = deflate.Read(bytes, read, length - read);
                        if (n <= 0) throw new InvalidDataException("The image data is incomplete.");
                        read += n;
                    }
                }
                return new RawImage(width, height, bytes);
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
            {
                throw new InvalidDataException("The image data is damaged.", ex);
            }
        }
    }
}
