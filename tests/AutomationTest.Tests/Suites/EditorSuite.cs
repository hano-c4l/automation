using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using AutomationTest.App;
using AutomationTest.Core.Model;

namespace AutomationTest.Tests.Suites
{
    /// <summary>
    /// The action editor: every kind goes into the editor (as when a row is selected) and comes back out (as when Update is pressed)
    /// unchanged. This is where a field that is shown by one kind and hidden by another goes wrong.
    /// </summary>
    internal static class EditorSuite
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        internal sealed class Editor
        {
            public MainForm Form;
            // The script object changes when a script is opened or an Undo puts one back: always ask for the current one.
            private System.Collections.IList _actions
            {
                get
                {
                    var script = Field("_script");
                    return (System.Collections.IList)script.GetType().GetProperty("Actions").GetValue(script);
                }
            }
            private readonly ListView _list;
            private readonly MethodInfo _load, _build, _refresh;

            public Editor()
            {
                Form = new MainForm { HotKeysEnabled = false, ConfirmSave = message => DialogResult.No }; // a question about unsaved changes must never block a test // never take the hotkeys of a program the user may have open
                var h = Form.Handle; // the list needs a window handle to select rows
                foreach (Control c in Form.Controls) { var h2 = c.Handle; }
                _list = (ListView)Field("_list");
                _load = typeof(MainForm).GetMethod("LoadSelectedIntoEditor", Private);
                _build = typeof(MainForm).GetMethod("BuildNode", Private);
                _refresh = typeof(MainForm).GetMethod("RefreshList", Private, null, Type.EmptyTypes, null);
            }

            public object Field(string name) { return typeof(MainForm).GetField(name, Private).GetValue(Form); }

            /// <summary>The actions of the script in the editor.</summary>
            public System.Collections.Generic.List<ActionNode> Actions
            {
                get { return (System.Collections.Generic.List<ActionNode>)_actions; }
            }

            public string Status { get { return ((dynamic)Field("_status")).Text; } }

            public void Show(params ActionNode[] actions)
            {
                _actions.Clear();
                foreach (var a in actions) _actions.Add(a);
                _refresh.Invoke(Form, null);
                _list.SelectedItems.Clear();
            }

            /// <summary>Selects the lines of these nodes (as a click would).</summary>
            public void Select(params ActionNode[] nodes)
            {
                var rows = (System.Collections.IList)Field("_rows");
                _list.SelectedItems.Clear();
                for (int i = 0; i < rows.Count; i++)
                {
                    object row = rows[i];
                    var node = (ActionNode)row.GetType().GetField("Node").GetValue(row);
                    bool end = (bool)row.GetType().GetField("IsEnd").GetValue(row);
                    if (!end && Array.IndexOf(nodes, node) >= 0) _list.Items[i].Selected = true;
                }
            }

            /// <summary>Chooses the Action of the Control category in the editor.</summary>
            public void Choose(ControlKind kind)
            {
                ((ComboBox)Field("_category")).SelectedItem = ActionCategory.Control;
                var combo = (ComboBox)Field("_controlKind");
                combo.SelectedItem = kind;
            }

            public void Call(string method, params object[] args)
            {
                typeof(MainForm).GetMethod(method, Private).Invoke(Form, args);
            }

            /// <summary>Puts the node into the editor and takes it out again.</summary>
            public ActionNode Through(ActionNode node)
            {
                _actions.Clear();
                _actions.Add(node);
                _refresh.Invoke(Form, null);
                _list.SelectedItems.Clear();
                _list.Items[0].Selected = true;
                _load.Invoke(Form, null);
                return (ActionNode)_build.Invoke(Form, null);
            }
        }

        [Suite("editor.groups")]
        public static void Groups(T t)
        {
            t.True("every Excel, Web and Utilities kind is in a group", ActionGroups.Covers());
        }

        [Suite("editor.roundtrip")]
        public static void RoundTrip(T t)
        {
            var e = new Editor();
            foreach (ExcelKind k in Enum.GetValues(typeof(ExcelKind)))
            {
                var back = e.Through(ActionNode.NewExcel(new ExcelSettings { Kind = k }));
                t.Eq("Excel " + k, back?.Excel?.Kind, k);
            }
            foreach (WebKind k in Enum.GetValues(typeof(WebKind)))
            {
                var back = e.Through(ActionNode.NewWeb(new WebSettings { Kind = k }));
                t.Eq("Web " + k, back?.Web?.Kind, k);
            }
            foreach (CommandKind k in Enum.GetValues(typeof(CommandKind)))
            {
                var back = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = k, Value = k == CommandKind.Close ? "x" : "", Match = ProgramMatch.Name, All = true, Id = "v" }));
                t.Eq("Utilities " + k, back?.Command?.Kind, k);
            }
            foreach (ControlKind k in Enum.GetValues(typeof(ControlKind)))
            {
                if (k == ControlKind.Image || k == ControlKind.Wait || k == ControlKind.Variable || k == ControlKind.Loop || k == ControlKind.Case) continue; // need fields filled in, or only offered inside a Condition: covered elsewhere
                ActionNode node = k == ControlKind.Condition ? ActionNode.NewCondition()
                    : k == ControlKind.Case ? ActionNode.NewCase(new CaseSettings { Expression = "true" })
                    : k == ControlKind.Else ? ActionNode.NewElse()
                    : k == ControlKind.Try ? ActionNode.NewTry()
                    : k == ControlKind.Catch ? ActionNode.NewCatch(new CatchSettings { Id = "err" })
                    : k == ControlKind.Section ? ActionNode.NewSection()
                    : ActionNode.NewJump(k);
                var back = e.Through(node);
                if (k == ControlKind.Else) continue; // an else has no settings: selecting it leaves the editor as it was
                t.Eq("Control " + k, back?.Control, k);
            }
        }

        [Suite("editor.fields")]
        public static void Fields(T t)
        {
            var e = new Editor();

            var c = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.Shutdown, TimeMs = 7000, Force = true })).Command;
            t.Eq("shutdown time", c.TimeMs, 7000L);
            t.Eq("shutdown force", c.Force, true);

            c = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.Close, Match = ProgramMatch.Path, Value = "C:/x.exe", All = true, Force = true, TimeMs = 9000, DelayMs = 1500 })).Command;
            t.Eq("close match", c.Match, ProgramMatch.Path);
            t.Eq("close value", c.Value, "C:/x.exe");
            t.Eq("close all", c.All, true);
            t.Eq("close kill", c.Force, true);
            t.Eq("close delay", c.DelayMs, 1500L);

            c = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.Close, Match = ProgramMatch.Name, Value = "a", Force = false, TimeMs = 9000 })).Command;
            t.Eq("close timeout kept", c.TimeMs, 9000L);
            t.Eq("close not kill", c.Force, false);

            c = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsDelete, Value = "C:/t", Recurse = true, Force = true })).Command;
            t.Eq("delete recurse", c.Recurse, true);
            t.Eq("delete force", c.Force, true);
            t.Eq("delete has no time", c.TimeMs, 0L);

            c = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsCopy, Value = "{p}", ValueIsExpression = true, Target = "C:/d", Overwrite = true })).Command;
            t.Eq("copy value is expression", c.ValueIsExpression, true);
            t.Eq("copy target", c.Target, "C:/d");
            t.Eq("copy overwrite", c.Overwrite, true);
            t.Eq("copy has no force", c.Force, false);

            c = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsWrite, Value = "C:/f.txt", Target = "  spaced text ", Append = true })).Command;
            t.Eq("write keeps the spaces of the text", c.Target, "  spaced text ");
            t.Eq("write append", c.Append, true);

            c = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsList, Value = "C:/f", Recurse = true, Id = "files" })).Command;
            t.Eq("list id", c.Id, "files");
            t.Eq("list recurse", c.Recurse, true);

            c = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.Open, Value = "notepad", Target = "stale", Id = "stale", Recurse = true })).Command;
            t.Eq("Open drops the fields of other kinds", c.Target + c.Id + c.Recurse, "False");

            var x = e.Through(ActionNode.NewExcel(new ExcelSettings { Kind = ExcelKind.Copy, Cell = "A1:B2", Value = "D1", Paste = ExcelPasteKind.Formulas, Transpose = true, DestWorkbook = "wb", DestSheet = "sh" })).Excel;
            t.Eq("copy paste", x.Paste, ExcelPasteKind.Formulas);
            t.Eq("copy transpose", x.Transpose, true);
            t.Eq("copy destination workbook", x.DestWorkbook, "wb");
            t.Eq("copy destination sheet", x.DestSheet, "sh");

            x = e.Through(ActionNode.NewExcel(new ExcelSettings { Kind = ExcelKind.CutCells, Cell = "A1", Value = "D1", DestWorkbook = "wb" })).Excel;
            t.Eq("cut destination workbook", x.DestWorkbook, "wb");
            t.Eq("cut has no paste kind", x.Paste, ExcelPasteKind.All);

            x = e.Through(ActionNode.NewExcel(new ExcelSettings { Kind = ExcelKind.ReplaceText, Cell = "A:A", Value = "x", ReplaceWith = " y ", MatchCase = true, WholeCell = true })).Excel;
            t.Eq("replace with", x.ReplaceWith, " y ");
            t.Eq("replace match case", x.MatchCase, true);
            t.Eq("replace whole cell", x.WholeCell, true);

            x = e.Through(ActionNode.NewExcel(new ExcelSettings { Kind = ExcelKind.SheetCopy, SheetName = "^S$", Value = "New", DestWorkbook = "wb", UseWorkbook = "src", Position = ExcelSheetPosition.Start, SelectNew = false })).Excel;
            t.Eq("sheet copy destination workbook", x.DestWorkbook, "wb");
            t.Eq("sheet copy source workbook", x.UseWorkbook, "src");
            t.Eq("sheet copy name", x.Value, "New");
            t.Eq("sheet copy position", x.Position, ExcelSheetPosition.Start);
            t.Eq("sheet copy select", x.SelectNew, false);
            t.Eq("sheet copy has no In sheet", x.UseSheet, "");

            x = e.Through(ActionNode.NewExcel(new ExcelSettings { Kind = ExcelKind.SheetVisible, SheetName = "^S$", Visibility = ExcelVisibility.VeryHidden })).Excel;
            t.Eq("visibility", x.Visibility, ExcelVisibility.VeryHidden);

            x = e.Through(ActionNode.NewExcel(new ExcelSettings { Kind = ExcelKind.HideCells, Cell = "B5", Target = ExcelTarget.Columns, Count = 3 })).Excel;
            t.Eq("hide target", x.Target, ExcelTarget.Columns);
            t.Eq("hide count", x.Count, 3);
            t.Eq("hide cell", x.Cell, "B5");

            x = e.Through(ActionNode.NewExcel(new ExcelSettings { Kind = ExcelKind.GetCell, Cell = "A1", Id = "v", Paste = ExcelPasteKind.Values, ReplaceWith = "stale" })).Excel;
            t.Eq("Get Cell drops the paste kind", x.Paste, ExcelPasteKind.All);
            t.Eq("Get Cell drops the replacement", x.ReplaceWith, "");
        }

        [Suite("editor.flow")]
        public static void Flow(T t)
        {
            var e = new Editor();

            // Add a Try: it comes with its Catch
            e.Show();
            e.Choose(ControlKind.Try);
            e.Call("AddAction");
            t.Eq("a Try was added", e.Actions.Count, 1);
            var tr = e.Actions[0];
            t.True("it is a Try with one child, the Catch", tr.IsKind(ControlKind.Try) && tr.Children.Count == 1 && tr.Children[0].IsKind(ControlKind.Catch));

            // Add with the Try selected: the action goes in before the Catch
            e.Select(tr);
            e.Choose(ControlKind.Section);
            e.Call("AddAction");
            t.Eq("the Try has the Section and the Catch", tr.Children.Count, 2);
            t.True("the Section is before the Catch", tr.Children[0].IsKind(ControlKind.Section) && tr.Children[1].IsKind(ControlKind.Catch));

            // Break and Continue only inside a Loop
            e.Select(tr);
            e.Choose(ControlKind.Break);
            e.Call("AddAction");
            t.Eq("Break outside a Loop is refused", tr.Children.Count, 2);
            t.True("and the message says why", e.Status.Contains("inside a Loop") || e.Status.Contains("only works inside a Loop"));

            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 2 });
            var tryInLoop = ActionNode.NewTry();
            loop.Children.Add(tryInLoop);
            e.Show(loop);
            e.Select(tryInLoop);
            e.Choose(ControlKind.Break);
            e.Call("AddAction");
            t.True("Break inside a Try inside a Loop is accepted", tryInLoop.Children.Count == 2 && tryInLoop.Children[0].IsKind(ControlKind.Break));
            e.Select(tryInLoop.Children[0]);
            e.Choose(ControlKind.Return);
            e.Call("AddAction");
            t.True("an action added after a line goes after it, before the Catch", tryInLoop.Children.Count == 3 && tryInLoop.Children[1].IsKind(ControlKind.Return) && tryInLoop.Children[2].IsKind(ControlKind.Catch));

            // the Catch cannot be added by hand, deleted or moved
            e.Show(tr);
            e.Select(tr.Children[1]);
            e.Call("DeleteActions");
            t.True("the Catch cannot be deleted", tr.Children.Count == 2 && e.Status.Contains("catch line cannot be deleted"));
            e.Select(tr.Children[0]);
            e.Call("MoveUpDown", 1);
            t.True("a line cannot be moved below the Catch", tr.Children[0].IsKind(ControlKind.Section) && tr.Children[1].IsKind(ControlKind.Catch));
            e.Select(tr.Children[1]);
            e.Call("MoveUpDown", -1);
            t.True("the Catch cannot be moved up", tr.Children[1].IsKind(ControlKind.Catch));
            e.Select(tr.Children[0]);
            e.Call("MoveToEdge", false);
            t.True("Down to End keeps the Catch last", tr.Children[1].IsKind(ControlKind.Catch));

            // In puts an action into the Try before the Catch
            var tryB = ActionNode.NewTry();
            var after = ActionNode.NewSection();
            e.Show(tryB, after);
            e.Select(after);
            e.Call("MoveIn");
            t.True("In puts the action inside the Try, before the Catch", tryB.Children.Count == 2 && ReferenceEquals(tryB.Children[0], after) && tryB.Children[1].IsKind(ControlKind.Catch));

            // Update the Catch: only its ID changes; a Try cannot become something else
            var tryC = ActionNode.NewTry();
            e.Show(tryC);
            e.Select(tryC.Children[0]);
            e.Choose(ControlKind.Catch);
            ((TextBox)e.Field("_catchId")).Text = "oops";
            e.Call("UpdateAction");
            t.Eq("Update changes the ID of the Catch", tryC.Children[0].Catch.Id, "oops");
            t.True("it is still the last line of the Try", tryC.Children.Count == 1 && tryC.Children[0].IsKind(ControlKind.Catch));
            e.Select(tryC);
            e.Choose(ControlKind.Section);
            e.Call("UpdateAction");
            t.True("a Try cannot be changed into a Section", tryC.IsKind(ControlKind.Try) && e.Status.Contains("cannot be changed"));
        }

        /// <summary>Whether the control itself is set to be visible (the window is never shown in a test, so Control.Visible would be false for all).</summary>
        private static bool Shown(Editor e, string field)
        {
            var control = (Control)e.Field(field);
            var getState = typeof(Control).GetMethod("GetState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
            for (Control c = control; c != null && c.Tag == null; c = c.Parent) // a row with a "?" or button sits in a host (tagged) that is hidden as a whole
            {
                if (!(bool)getState.Invoke(c, new object[] { 2 })) return false;
                if (c.Parent != null && c.Parent.Tag != null && !(bool)getState.Invoke(c.Parent, new object[] { 2 })) return false;
                if (c is GroupBox || c.Parent is TableLayoutPanel || c.Parent is GroupBox) break;
            }
            return true;
        }

        [Suite("editor.email")]
        public static void EmailCategory(T t)
        {
            var e = new Editor();
            var category = (ComboBox)e.Field("_category");
            var mail = (Button)e.Field("_mailButton");
            var box = (GroupBox)e.Field("_emailBox");
            var kind = (ComboBox)e.Field("_emailKind");
            category.SelectedItem = ActionCategory.Mouse;
            t.False("the mail icon is hidden for other categories", Shown(e, "_mailButton"));
            category.SelectedItem = ActionCategory.Email;
            t.True("the mail icon shows for Email", Shown(e, "_mailButton"));
            t.True("the Email box shows", Shown(e, "_emailBox"));
            kind.SelectedItem = EmailKind.Send;
            t.True("Send shows To", Shown(e, "_emailTo"));
            t.False("Send hides the filters", Shown(e, "_emailSince"));
            kind.SelectedItem = EmailKind.Read;
            t.False("Read hides To", Shown(e, "_emailTo"));
            t.True("Read shows the filters", Shown(e, "_emailSince") && Shown(e, "_emailStatus"));
            category.SelectedItem = ActionCategory.Keyboard;
            t.False("the mail icon hides again", Shown(e, "_mailButton"));

            var read = e.Through(ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Read, Profile = "work", Subject = "Invoice", From = "boss", Since = "2026-01-01", Before = "2026-02-01", Status = EmailStatus.Unread, Max = 7, MarkRead = true, SaveTo = "C:/mail", Id = "mail", TimeMs = 5000, DelayMs = 300 })).Email;
            t.Eq("Read round trip", read.Kind + "|" + read.Profile + "|" + read.Subject + "|" + read.From + "|" + read.Since + "|" + read.Before + "|" + read.Status + "|" + read.Max + "|" + read.MarkRead + "|" + read.SaveTo + "|" + read.Id + "|" + read.TimeMs + "|" + read.DelayMs,
                "Read|work|Invoice|boss|2026-01-01|2026-02-01|Unread|7|True|C:/mail|mail|5000|300");
            var send = e.Through(ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Send, Profile = "work", To = "a@x.com", Cc = "b@x.com", Subject = "Hi", Body = "line1\nline2", BodyIsHtml = true, Attachments = "C:/a.txt" })).Email;
            t.Eq("Send round trip", send.Kind + "|" + send.To + "|" + send.Cc + "|" + send.Subject + "|" + send.Body.Replace("\r\n", "/").Replace("\n", "/") + "|" + send.BodyIsHtml + "|" + send.Attachments, "Send|a@x.com|b@x.com|Hi|line1/line2|True|C:/a.txt");
            var del = e.Through(ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Delete, Profile = "work", Subject = "x", Permanent = true })).Email;
            t.Eq("Delete round trip", del.Kind + "|" + del.Permanent, "Delete|True");
            var mark = e.Through(ActionNode.NewEmail(new EmailSettings { Kind = EmailKind.Mark, Profile = "work", Status = EmailStatus.Unread, MarkAs = EmailMarkAs.Read })).Email;
            t.Eq("Mark round trip", mark.Kind + "|" + mark.MarkAs, "Mark|Read");
        }

        [Suite("editor.program")]
        public static void ProgramCategory(T t)
        {
            var e = new Editor();
            var category = (ComboBox)e.Field("_category");
            var group = (ComboBox)e.Field("_cmdGroup");
            var kind = (ComboBox)e.Field("_cmdKind");

            // the Program category offers Program and Window, Utilities offers File/Folder and System
            category.SelectedItem = ActionCategory.Program;
            t.Eq("Program category groups", string.Join(",", group.Items.Cast<object>()), "Program,Window");
            t.Eq("Program first type", kind.SelectedItem, CommandKind.Open);
            group.SelectedItem = "Window";
            t.Eq("the Window group has six types", kind.Items.Count, 6);
            category.SelectedItem = ActionCategory.Utilities;
            t.Eq("Utilities category groups", string.Join(",", group.Items.Cast<object>()), "File/Folder,API,Script,System");
            category.SelectedItem = ActionCategory.Program;
            t.Eq("back to Program: its groups again", string.Join(",", group.Items.Cast<object>()), "Program,Window");

            // a node of each kind goes in and out in the right category
            foreach (CommandKind k in Enum.GetValues(typeof(CommandKind)))
            {
                var back = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = k, Value = "x", Id = "v", Match = ProgramMatch.Name }));
                t.Eq("category of " + k, back.Category, CommandKinds.CategoryOf(k));
                t.Eq("kind " + k, back.Command.Kind, k);
            }

            // the window fields
            var move = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.WindowMove, Match = ProgramMatch.Path, Value = "C:/a.exe", All = true, X = -30, Y = 40 })).Command;
            t.Eq("move x", move.X, -30);
            t.Eq("move y", move.Y, 40);
            t.Eq("move match", move.Match, ProgramMatch.Path);
            t.Eq("move all", move.All, true);
            var resize = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.WindowResize, Match = ProgramMatch.Title, Value = "w", Width = 800, Height = 600, X = 5, Y = 5 })).Command;
            t.Eq("resize width", resize.Width, 800);
            t.Eq("resize height", resize.Height, 600);
            t.Eq("resize drops the position of Move", resize.X + resize.Y, 0);
            var open = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.Open, Value = "{p}", ValueIsExpression = true, X = 9, Width = 9 })).Command;
            t.True("Open keeps its expression flag", open.ValueIsExpression);
            t.Eq("Open drops window fields", open.X + open.Width, 0);
        }

        [Suite("editor.group-box")]
        public static void GroupBox(T t)
        {
            var e = new Editor();
            var group = (ComboBox)e.Field("_xlGroup");
            var kind = (ComboBox)e.Field("_xlKind");
            ((ComboBox)e.Field("_category")).SelectedItem = ActionCategory.Excel;
            group.SelectedItem = "Sheet";
            t.Eq("changing the group selects its first type", kind.SelectedItem, ExcelKind.SelectSheet);
            t.Eq("the Sheet group has 7 types", kind.Items.Count, 7);
            group.SelectedItem = "Cell";
            t.Eq("Cell group has 2 types", kind.Items.Count, 2);
        }
    }
}
