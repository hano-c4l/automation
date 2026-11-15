using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Imaging;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>The Image action as a type of Variable (and how older files are read), and OCR.</summary>
    internal static class ImageSuite
    {
        /// <summary>A picture with enough detail to be found: noise from a fixed seed.</summary>
        private static RawImage Noise(int width, int height, int seed)
        {
            var random = new Random(seed);
            var bytes = new byte[width * height * 4];
            for (int i = 0; i < bytes.Length; i += 4)
            {
                bytes[i] = (byte)random.Next(256); bytes[i + 1] = (byte)random.Next(256); bytes[i + 2] = (byte)random.Next(256); bytes[i + 3] = 255;
            }
            return new RawImage(width, height, bytes);
        }

        private static Bitmap ToBitmap(RawImage image)
        {
            var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(image.Bgra, 0, data.Scan0, image.Bgra.Length);
            bitmap.UnlockBits(data);
            return bitmap;
        }

        private static ActionNode OldImage(string id, string name, bool disabled, params ActionNode[] inside)
        {
            var settings = new ImageSettings { Id = id, RangeWidth = 0 };
            settings.SetImage(Noise(12, 12, 1));
            var node = ActionNode.NewImage(settings);
            node.Name = name;
            node.Disabled = disabled;
            node.Children.AddRange(inside);
            return node;
        }

        /// <summary>Saves the script as text and reads it again, the way a file from an older version would be opened.</summary>
        private static AutomationScript Reopen(params ActionNode[] actions)
        {
            var script = new AutomationScript();
            script.Actions.AddRange(actions);
            return ScriptSerializer.FromJson(ScriptSerializer.ToJson(script));
        }

        private static ActionNode Var(string id, string text)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = VariableType.Number, Text = text });
        }

        [Suite("image.migration")]
        public static void Migration(T t)
        {
            // an Image with actions inside becomes a Variable (Image) and a Condition on {id.Found}
            var inside = Var("x", "1");
            var back = Reopen(OldImage("logo", "find the logo", false, inside));
            t.Eq("two actions now", back.Actions.Count, 2);
            var variable = back.Actions[0];
            t.True("the first is a Variable of the type Image", variable.IsKind(ControlKind.Variable) && variable.Variable.Type == VariableType.Image);
            t.Eq("it has the old ID", variable.Variable.Id, "logo");
            t.True("it has the picture", variable.Image != null && variable.Image.HasImage && variable.Image.ImageWidth == 12);
            t.Eq("it keeps the name", variable.Name, "find the logo");
            var condition = back.Actions[1];
            t.True("the second is a Condition", condition.IsKind(ControlKind.Condition));
            t.Eq("on the Found of the variable", condition.Children[0].Case.Expression, "{logo.Found}");
            t.Eq("holding what was inside", condition.Children[0].Children.Count, 1);
            t.Eq("the Condition has the name too", condition.Name, "find the logo");

            // no actions inside: only the Variable
            t.Eq("an Image with nothing inside is only a Variable", Reopen(OldImage("solo", null, false)).Actions.Count, 1);

            // State goes to both
            var off = Reopen(OldImage("gone", null, true, Var("y", "1")));
            t.True("a disabled Image makes both disabled", off.Actions[0].Disabled && off.Actions[1].Disabled);

            // IDs: made up when missing, never one that is in use
            var made = Reopen(OldImage(null, null, false), OldImage("", null, false), Var("image1", "0"), OldImage(null, null, false));
            var ids = made.Actions.Where(a => a.Variable != null && a.Variable.Type == VariableType.Image).Select(a => a.Variable.Id).ToList();
            t.Eq("made-up IDs avoid the ones in use and each other", string.Join(",", ids), "image2,image3,image4");

            // inside a Condition case and inside a Loop
            var cond = ActionNode.NewCondition();
            cond.Children[0].Children.Add(OldImage("deep", null, false, Var("z", "1")));
            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 1 });
            loop.Children.Add(cond);
            var nested = Reopen(loop).Actions[0].Children[0].Children[0].Children;
            t.True("an Image in a case is converted in place", nested.Count == 2 && nested[0].Variable.Type == VariableType.Image && nested[1].IsKind(ControlKind.Condition));

            // saving writes the new form: no Image action is left, and the file reads back the same
            var again = ScriptSerializer.ToJson(Reopen(OldImage("logo", null, false, Var("x", "1"))));
            t.False("no Image action in what is saved", again.Contains("\"Control\": \"Image\""));
            t.Eq("a second round trip changes nothing", ScriptSerializer.ToJson(ScriptSerializer.FromJson(again)), again);
        }

        [Suite("image.variable")]
        public static void ImageVariable(T t)
        {
            var picture = Noise(40, 30, 7);
            using (var form = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(120, 140), ClientSize = new Size(picture.Width, picture.Height), TopMost = true, ShowInTaskbar = false })
            using (var bitmap = ToBitmap(picture))
            {
                form.Paint += (s, e) => e.Graphics.DrawImageUnscaled(bitmap, 0, 0);
                form.Show();
                for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(30); }

                var settings = new ImageSettings { RangeX = 0, RangeY = 0, RangeWidth = 400, RangeHeight = 400, Accuracy = 95 };
                settings.SetImage(picture);
                var node = ActionNode.NewVariable(new VariableSettings { Id = "pic", Type = VariableType.Image });
                node.Image = settings;
                var read = ActionNode.NewVariable(new VariableSettings { Id = "x", Type = VariableType.Number, UseExpression = true, Expression = "{pic.Position.X}" });

                var runner = new ScriptRunner(new MouseController());
                runner.Run(new List<ActionNode> { node, read }, null, CancellationToken.None);
                object value;
                runner.Variables.TryGet("pic", out value);
                var image = value as ImageValue;
                t.True("the picture is found", image != null && image.Found);
                if (image != null && image.Found)
                {
                    t.Eq("at its place on the screen (x)", image.Position.X, 120.0);
                    t.Eq("at its place on the screen (y)", image.Position.Y, 140.0);
                    t.Eq("with its size", image.Size.Width, 40.0);
                }
                runner.Variables.TryGet("x", out value);
                t.Eq("the position can be used in an expression", value, 120.0);

                // something else: not found, and the variable says so
                var other = new ImageSettings { RangeX = 0, RangeY = 0, RangeWidth = 400, RangeHeight = 400, Accuracy = 95 };
                other.SetImage(Noise(40, 30, 99));
                var notThere = ActionNode.NewVariable(new VariableSettings { Id = "pic", Type = VariableType.Image });
                notThere.Image = other;
                var messages = new List<string>();
                var runner2 = new ScriptRunner(new MouseController());
                runner2.Message += messages.Add;
                runner2.Run(new List<ActionNode> { notThere }, null, CancellationToken.None);
                runner2.Variables.TryGet("pic", out value);
                t.True("a picture that is not there is Found = false", value is ImageValue && !((ImageValue)value).Found);
                t.True("and a note says so", messages.Any(m => m.Contains("not found")));

                // a Variable of the type Image without a picture says what is missing
                var empty = ActionNode.NewVariable(new VariableSettings { Id = "none", Type = VariableType.Image });
                empty.Image = new ImageSettings();
                t.Throws("no picture", () => new ScriptRunner(new MouseController()).Run(new List<ActionNode> { empty }, null, CancellationToken.None), "no picture");
            }
        }

        [Suite("image.ocr")]
        public static void Ocr(T t)
        {
            var languages = WindowsOcr.AvailableLanguages();
            if (languages.Count == 0)
            {
                Console.WriteLine("  note: Windows has no OCR language installed here, so the reading itself is not tested.");
                // the message must still be a clear one
                var picture = new RawImage(20, 20, new byte[20 * 20 * 4]);
                t.Throws("no OCR language", () => WindowsOcr.Recognize(picture, ""), "OCR");
                return;
            }

            string tag = languages.FirstOrDefault(l => l.StartsWith("en", StringComparison.OrdinalIgnoreCase)) ?? languages[0];
            using (var bitmap = new Bitmap(520, 120, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                using (var font = new Font("Arial", 40, FontStyle.Regular, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.White);
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.DrawString("Hello World 123", font, Brushes.Black, 10, 30);
                }
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var bytes = new byte[bitmap.Width * bitmap.Height * 4];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                bitmap.UnlockBits(data);
                string text = WindowsOcr.Recognize(new RawImage(bitmap.Width, bitmap.Height, bytes), tag);
                t.True("the text is read (" + tag + "): \"" + text + "\"", text.IndexOf("Hello", StringComparison.OrdinalIgnoreCase) >= 0 && text.Contains("123"));
            }

            // the Variable action reads text from the screen: a window with text on it is shown for a moment
            using (var form = new Form { FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(150, 160), ClientSize = new Size(520, 120), TopMost = true, ShowInTaskbar = false, BackColor = Color.White })
            {
                form.Paint += (s, e) =>
                {
                    using (var font = new Font("Arial", 40, FontStyle.Regular, GraphicsUnit.Pixel))
                    {
                        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                        e.Graphics.DrawString("Hello World 123", font, Brushes.Black, 10, 30);
                    }
                };
                form.Show();
                for (int i = 0; i < 15; i++) { Application.DoEvents(); Thread.Sleep(30); }
                var node = ActionNode.NewVariable(new VariableSettings { Id = "words", Type = VariableType.OCR });
                node.Image = new ImageSettings { RangeX = 140, RangeY = 150, RangeWidth = 540, RangeHeight = 140, OcrLanguage = tag };
                var runner = new ScriptRunner(new MouseController());
                runner.Run(new List<ActionNode> { node }, null, CancellationToken.None);
                object value;
                runner.Variables.TryGet("words", out value);
                t.True("an OCR variable holds the text on the screen: \"" + value + "\"", value is string && ((string)value).IndexOf("Hello", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            t.Throws("a language that is not installed names the ones that are", () => WindowsOcr.Recognize(new RawImage(20, 20, new byte[20 * 20 * 4]), "xx-YY"), "not installed");
        }

        [Suite("image.in-the-editor")]
        public static void InTheEditor(T t)
        {
            var e = new EditorSuite.Editor();
            var settings = new ImageSettings { RangeX = 10, RangeY = 20, RangeWidth = 300, RangeHeight = 200, Accuracy = 80, Anchor = WindowAnchor.BottomRight, Process = new WindowTarget { ProcessName = "notepad", TitlePattern = "^x$" } };
            settings.SetImage(Noise(16, 16, 3));
            var node = ActionNode.NewVariable(new VariableSettings { Id = "logo", Type = VariableType.Image });
            node.Image = settings;
            var back = e.Through(node);
            t.Eq("type", back.Variable.Type, VariableType.Image);
            t.Eq("id", back.Variable.Id, "logo");
            t.Eq("the id is in the settings too", back.Image.Id, "logo");
            t.True("range", back.Image.RangeX == 10 && back.Image.RangeY == 20 && back.Image.RangeWidth == 300 && back.Image.RangeHeight == 200);
            t.Eq("accuracy", back.Image.Accuracy, 80);
            t.Eq("anchor", back.Image.Anchor, WindowAnchor.BottomRight);
            t.Eq("process", back.Image.Process.ProcessName, "notepad");
            t.True("the picture", back.Image.HasImage && back.Image.ImageWidth == 16);
            t.False("no expression or value is stored", back.Variable.UseExpression);

            var ocrArea = new ImageSettings { RangeX = 5, RangeY = 6, RangeWidth = 100, RangeHeight = 50, OcrLanguage = "vi" };
            var ocr = ActionNode.NewVariable(new VariableSettings { Id = "text", Type = VariableType.OCR });
            ocr.Image = ocrArea;
            var ocrBack = e.Through(ocr);
            t.Eq("OCR type", ocrBack.Variable.Type, VariableType.OCR);
            t.Eq("OCR language", ocrBack.Image.OcrLanguage, "vi");
            t.True("OCR area", ocrBack.Image.RangeX == 5 && ocrBack.Image.RangeWidth == 100);
            t.False("OCR has no picture", ocrBack.Image.HasImage);

            // a Variable that is not an Image or OCR keeps no picture settings
            var plain = ActionNode.NewVariable(new VariableSettings { Id = "p", Type = VariableType.String, Text = "t" });
            plain.Image = settings;
            t.True("a String variable has no image settings after Normalize", Reopen(plain).Actions[0].Image == null);

            // the Control list no longer offers Image
            var combo = (ComboBox)e.Field("_controlKind");
            t.False("Image is not an Action of Control any more", combo.Items.Contains(ControlKind.Image));
        }

        [Suite("image.start-selected")]
        public static void StartSelected(T t)
        {
            var settings = new ImageSettings();
            settings.SetImage(Noise(10, 10, 2));
            var define = ActionNode.NewVariable(new VariableSettings { Id = "found", Type = VariableType.Image });
            define.Image = settings;
            var use = ActionNode.NewVariable(new VariableSettings { Id = "r", Type = VariableType.Boolean, UseExpression = true, Expression = "{found.Found}" });
            var script = new AutomationScript();
            script.Actions.Add(define);
            script.Actions.Add(use);
            var plan = SelectionPlanner.Build(script, new[] { use });
            var need = plan.Needs.Single(n => n.Name == "found");
            t.True("the selection asks for the image value", need.IsImage);
            plan = SelectionPlanner.Build(script, new[] { define, use });
            t.Eq("when the Variable is in the selection nothing is asked", plan.Needs.Count, 0);

            var text = ActionNode.NewVariable(new VariableSettings { Id = "words", Type = VariableType.OCR });
            text.Image = new ImageSettings();
            var useText = ActionNode.NewVariable(new VariableSettings { Id = "len", Type = VariableType.String, UseExpression = true, Expression = "{words}" });
            var script2 = new AutomationScript();
            script2.Actions.Add(text);
            script2.Actions.Add(useText);
            var need2 = SelectionPlanner.Build(script2, new[] { useText }).Needs.Single();
            t.Eq("an OCR variable is asked for as a String", need2.Type, VariableType.String);
        }
    }
}
