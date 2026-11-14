using System;
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

        private sealed class Editor
        {
            public MainForm Form;
            private readonly System.Collections.IList _actions;
            private readonly ListView _list;
            private readonly MethodInfo _load, _build, _refresh;

            public Editor()
            {
                Form = new MainForm { HotKeysEnabled = false }; // never take the hotkeys of a program the user may have open
                var h = Form.Handle; // the list needs a window handle to select rows
                foreach (Control c in Form.Controls) { var h2 = c.Handle; }
                var script = Field("_script");
                _actions = (System.Collections.IList)script.GetType().GetProperty("Actions").GetValue(script);
                _list = (ListView)Field("_list");
                _load = typeof(MainForm).GetMethod("LoadSelectedIntoEditor", Private);
                _build = typeof(MainForm).GetMethod("BuildNode", Private);
                _refresh = typeof(MainForm).GetMethod("RefreshList", Private, null, Type.EmptyTypes, null);
            }

            public object Field(string name) { return typeof(MainForm).GetField(name, Private).GetValue(Form); }

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
