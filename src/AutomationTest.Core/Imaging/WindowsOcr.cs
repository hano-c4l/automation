using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace AutomationTest.Core.Imaging
{
    /// <summary>
    /// Reads the text of a picture with the OCR that comes with Windows 10 and 11 (Windows.Media.Ocr), so nothing has to be installed.
    /// The languages it can read are the OCR languages installed in Windows (Settings, Time and language, Language and region).
    /// The Windows classes are reached through reflection: this library does not reference the Windows SDK and still loads on
    /// machines where they are missing.
    /// </summary>
    public static class WindowsOcr
    {
        private const string WinRt = ", Windows, ContentType=WindowsRuntime";

        /// <summary>The language tags Windows can read (for example "en-US"); empty when OCR is not available.</summary>
        public static IReadOnlyList<string> AvailableLanguages()
        {
            try
            {
                Type engineType = Type.GetType("Windows.Media.Ocr.OcrEngine" + WinRt);
                if (engineType == null) return new string[0];
                var languages = (System.Collections.IEnumerable)engineType.GetProperty("AvailableRecognizerLanguages").GetValue(null, null);
                var tags = new List<string>();
                foreach (object language in languages) tags.Add((string)language.GetType().GetProperty("LanguageTag").GetValue(language, null));
                return tags;
            }
            catch (Exception) { return new string[0]; }
        }

        /// <summary>The text in the picture (lines separated by new lines; empty when there is none).</summary>
        /// <param name="language">A language tag such as "vi" or "en-US"; empty = the languages of the user's Windows profile.</param>
        /// <exception cref="InvalidOperationException">OCR is not available, or the language is not installed.</exception>
        public static string Recognize(RawImage image, string language)
        {
            try
            {
                return RecognizeCore(image, (language ?? "").Trim());
            }
            catch (InvalidOperationException) { throw; } // already worded for the user
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                if (inner is AggregateException) inner = inner.GetBaseException();
                throw Unavailable(inner);
            }
        }

        private static InvalidOperationException Unavailable(Exception ex)
        {
            return new InvalidOperationException("Text recognition is not available: " + ex.Message);
        }

        private static string RecognizeCore(RawImage image, string language)
        {
            Type engineType = Type.GetType("Windows.Media.Ocr.OcrEngine" + WinRt);
            if (engineType == null) throw new InvalidOperationException("Text recognition needs Windows 10 or later.");

            object engine;
            if (language.Length == 0)
                engine = engineType.GetMethod("TryCreateFromUserProfileLanguages").Invoke(null, null);
            else
            {
                Type languageType = Type.GetType("Windows.Globalization.Language" + WinRt);
                object lang = Activator.CreateInstance(languageType, language);
                engine = engineType.GetMethod("TryCreateFromLanguage").Invoke(null, new[] { lang });
            }
            if (engine == null)
            {
                string installed = string.Join(", ", AvailableLanguages());
                throw new InvalidOperationException(language.Length == 0
                    ? "Windows has no OCR language for your profile. Install one in Settings, Time and language, Language and region (installed: " + (installed.Length == 0 ? "none" : installed) + ")."
                    : "The OCR language \"" + language + "\" is not installed in Windows (installed: " + (installed.Length == 0 ? "none" : installed) + ").");
            }

            int max = (int)(uint)engineType.GetProperty("MaxImageDimension").GetValue(null, null);
            if (image.Width > max || image.Height > max)
                throw new InvalidOperationException("The area to read is too large (at most " + max + " pixels on a side).");

            Assembly runtime = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
            MethodInfo asBuffer = runtime.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions")
                .GetMethods().First(m => m.Name == "AsBuffer" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(byte[]));
            object buffer = asBuffer.Invoke(null, new object[] { image.Bgra });

            Type bitmapType = Type.GetType("Windows.Graphics.Imaging.SoftwareBitmap" + WinRt);
            Type formatType = Type.GetType("Windows.Graphics.Imaging.BitmapPixelFormat" + WinRt);
            object bgra8 = Enum.ToObject(formatType, 87); // BitmapPixelFormat.Bgra8
            object bitmap = bitmapType.GetMethod("CreateCopyFromBuffer", new[] { buffer.GetType().GetInterfaces().First(i => i.Name == "IBuffer"), formatType, typeof(int), typeof(int) })
                .Invoke(null, new[] { buffer, bgra8, image.Width, image.Height });
            try
            {
                object operation = engineType.GetMethod("RecognizeAsync").Invoke(engine, new[] { bitmap });
                Type resultType = Type.GetType("Windows.Media.Ocr.OcrResult" + WinRt);
                MethodInfo asTask = runtime.GetType("System.WindowsRuntimeSystemExtensions").GetMethods()
                    .First(m => m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1
                                && m.GetParameters()[0].ParameterType.Name.StartsWith("IAsyncOperation`1") && m.GetParameters()[0].ParameterType.GetGenericArguments().Length == 1);
                var task = (Task)asTask.MakeGenericMethod(resultType).Invoke(null, new[] { operation });
                if (!task.Wait(60000)) throw new InvalidOperationException("Text recognition did not finish in time.");
                object result = task.GetType().GetProperty("Result").GetValue(task, null);
                string text = (string)resultType.GetProperty("Text").GetValue(result, null);
                return (text ?? "").Replace("\r\n", "\n").Trim();
            }
            finally { (bitmap as IDisposable)?.Dispose(); }
        }
    }
}
