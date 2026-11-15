using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using AutomationTest.Core.Model;

namespace AutomationTest.Tests.Suites
{
    /// <summary>
    /// The script file: saving and loading, and what happens to files from other versions. The "unknown name" checks pin down the
    /// fallback table in docs/ROADMAP.md (what an older build makes of a kind it does not know).
    /// </summary>
    internal static class ModelSuite
    {
        private static string TempFile(string text)
        {
            string path = Path.Combine(Path.GetTempPath(), "at_model_" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

        /// <summary>Loads a script from JSON text.</summary>
        private static AutomationScript Load(string json)
        {
            string path = TempFile(json);
            try { return ScriptSerializer.Load(path); }
            finally { File.Delete(path); }
        }

        private static ActionNode First(string actionJson)
        {
            return Load("{\"Version\":2,\"Actions\":[" + actionJson + "]}").Actions[0];
        }

        [Suite("model.roundtrip")]
        public static void RoundTrip(T t)
        {
            var script = new AutomationScript();
            script.Actions.Add(ActionNode.NewMouse(new MouseSettings { X = 5, Y = 6, TimeMs = 700, DelayMs = 300 }));
            script.Actions.Add(ActionNode.NewKeyboard(new KeyboardSettings { Action = KeyboardAction.Text, Value = "xin chào" }));
            script.Actions.Add(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsCopy, Value = "a", Target = "b", Overwrite = true }));
            script.Actions.Add(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.Close, Match = ProgramMatch.Id, Value = "12", All = true, Force = true, TimeMs = 4000 }));
            script.Actions.Add(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.WindowResize, Match = ProgramMatch.Name, Value = "notepad", Width = 400, Height = 300 }));
            script.Actions.Add(ActionNode.NewExcel(new ExcelSettings { Kind = ExcelKind.Copy, Cell = "A1", Value = "B2", Paste = ExcelPasteKind.Formulas, Transpose = true, UseWorkbook = "x", DestSheet = "y" }));
            script.Actions.Add(ActionNode.NewWeb(new WebSettings { Kind = WebKind.Click, Selector = "#go" }));
            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.For, ForId = "i", ForFrom = "1", ForTo = "3", ForStep = "1" });
            loop.Children.Add(ActionNode.NewVariable(new VariableSettings { Id = "v", Type = VariableType.String, Text = "t" }));
            script.Actions.Add(loop);
            var condition = ActionNode.NewCondition();
            condition.Children[0].Case.Expression = "{v} == \"t\"";
            condition.Children[0].Children.Add(ActionNode.NewVariable(new VariableSettings { Id = "w", Text = "1" }));
            script.Actions.Add(condition);
            script.Actions.Add(ActionNode.NewMouse(new MouseSettings { X = 1 }));
            script.Actions[script.Actions.Count - 1].Disabled = true;
            script.Actions[script.Actions.Count - 1].Name = "off one";

            string path = Path.Combine(Path.GetTempPath(), "at_model_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                ScriptSerializer.Save(script, path);
                var back = ScriptSerializer.Load(path);
                t.Eq("saving what was loaded gives the same text", ScriptSerializer.ToJson(back), ScriptSerializer.ToJson(script));
                t.Eq("action count", back.Actions.Count, script.Actions.Count);
                t.Eq("name kept", back.Actions[back.Actions.Count - 1].Name, "off one");
                t.True("State kept", back.Actions[back.Actions.Count - 1].Disabled);
                t.Eq("a Close action keeps its fields", back.Actions[3].Command.Match, ProgramMatch.Id);
                t.Eq("a Copy action keeps its paste kind", back.Actions[5].Excel.Paste, ExcelPasteKind.Formulas);
                t.Eq("children kept", back.Actions[7].Children.Count, 1);
            }
            finally { File.Delete(path); }
        }

        [Suite("model.flow-kinds")]
        public static void FlowKinds(T t)
        {
            // Try, Section and the jumps survive saving and loading
            var script = new AutomationScript();
            var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 2 });
            var tr = ActionNode.NewTry();
            tr.Children[0].Catch.Id = "err";
            tr.Children.Insert(0, ActionNode.NewJump(ControlKind.Break));
            var section = ActionNode.NewSection();
            section.Name = "group";
            section.Children.Add(ActionNode.NewJump(ControlKind.Continue));
            loop.Children.Add(tr);
            loop.Children.Add(section);
            script.Actions.Add(loop);
            script.Actions.Add(ActionNode.NewJump(ControlKind.Return));

            string path = Path.Combine(Path.GetTempPath(), "at_model_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                ScriptSerializer.Save(script, path);
                var back = ScriptSerializer.Load(path);
                t.Eq("the same text after a round trip", ScriptSerializer.ToJson(back), ScriptSerializer.ToJson(script));
                var backTry = back.Actions[0].Children[0];
                t.True("the Try keeps its body and its Catch last", backTry.Children.Count == 2 && backTry.Children[0].IsKind(ControlKind.Break) && backTry.Children[1].IsKind(ControlKind.Catch));
                t.Eq("the Catch keeps its ID", backTry.Children[1].Catch.Id, "err");
                t.Eq("the Section keeps its name", back.Actions[0].Children[1].Name, "group");
            }
            finally { File.Delete(path); }

            // a Try from a hand-edited file is repaired: one Catch, last
            var noCatch = First("{\"Category\":\"Control\",\"Control\":\"Try\",\"Children\":[{\"Category\":\"Mouse\",\"Mouse\":{}}]}");
            t.True("a Try without a Catch gets one", noCatch.Children.Count == 2 && noCatch.Children[1].IsKind(ControlKind.Catch));
            var twoCatch = First("{\"Category\":\"Control\",\"Control\":\"Try\",\"Children\":[{\"Category\":\"Control\",\"Control\":\"Catch\",\"Catch\":{\"Id\":\"a\"}},{\"Category\":\"Mouse\",\"Mouse\":{}},{\"Category\":\"Control\",\"Control\":\"Catch\",\"Catch\":{\"Id\":\"b\"}}]}");
            t.True("a Catch in the middle moves to the end and only one is kept", twoCatch.Children.Count == 2 && twoCatch.Children[0].Category == ActionCategory.Mouse && twoCatch.Children[1].IsKind(ControlKind.Catch));
            t.Eq("it is the first Catch that is kept", twoCatch.Children[1].Catch.Id, "a");

            // the jumps have nothing inside them
            var jump = First("{\"Category\":\"Control\",\"Control\":\"Break\",\"Children\":[{\"Category\":\"Mouse\",\"Mouse\":{}}]}");
            t.Eq("Break is not a container", jump.Children.Count, 0);
            t.False("IsContainer for Return", ActionNode.NewJump(ControlKind.Return).IsContainer);
            t.True("IsContainer for Try, Catch and Section", ActionNode.NewTry().IsContainer && ActionNode.NewCatch(null).IsContainer && ActionNode.NewSection().IsContainer);
        }

        [Suite("model.compatibility")]
        public static void Compatibility(T t)
        {
            // files from the past
            t.Throws<InvalidDataException>("version 1 is refused with a clear message", () => Load("{\"Version\":1,\"Actions\":[]}"), "old step format");
            t.Throws<InvalidDataException>("a newer version is refused", () => Load("{\"Version\":99,\"Actions\":[]}"), "newer version");
            t.Eq("a category saved as Command with Open is read as Program", First("{\"Category\":\"Command\",\"Command\":{\"Kind\":\"Open\",\"Value\":\"x\"}}").Category, ActionCategory.Program);
            t.Eq("an old Utilities Open is read as Program", First("{\"Category\":\"Utilities\",\"Command\":{\"Kind\":\"Open\",\"Value\":\"x\"}}").Category, ActionCategory.Program);
            t.Eq("an old Utilities Close is read as Program", First("{\"Category\":\"Utilities\",\"Command\":{\"Kind\":\"Close\",\"Value\":\"x\"}}").Category, ActionCategory.Program);
            t.Eq("File/Folder stays in Utilities", First("{\"Category\":\"Utilities\",\"Command\":{\"Kind\":\"FsCopy\",\"Value\":\"x\"}}").Category, ActionCategory.Utilities);
            t.Eq("a Program category with a Utilities kind is put right", First("{\"Category\":\"Program\",\"Command\":{\"Kind\":\"Shutdown\"}}").Category, ActionCategory.Utilities);
            t.Eq("a Program action keeps its settings", First("{\"Category\":\"Program\",\"Command\":{\"Kind\":\"WindowMove\",\"Value\":\"w\",\"X\":5,\"Y\":6}}").Command.Y, 6);
            t.Eq("a file without State is enabled", First("{\"Category\":\"Mouse\",\"Mouse\":{}}").Disabled, false);

            // Excel Copy: ValuesOnly of older files means Paste = Values
            t.Eq("ValuesOnly true reads as Paste = Values", First("{\"Category\":\"Excel\",\"Excel\":{\"Kind\":\"Copy\",\"ValuesOnly\":true}}").Excel.Paste, ExcelPasteKind.Values);
            t.Eq("no ValuesOnly reads as Paste = All", First("{\"Category\":\"Excel\",\"Excel\":{\"Kind\":\"Copy\"}}").Excel.Paste, ExcelPasteKind.All);
            var json = new DataContractJsonSerializer(typeof(ExcelSettings));
            Func<ExcelSettings, string> write = x => { var ms = new MemoryStream(); json.WriteObject(ms, x); return Encoding.UTF8.GetString(ms.ToArray()); };
            string text = write(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.Values });
            t.True("Paste = Values also writes ValuesOnly for older builds", text.Contains("\"ValuesOnly\":true") && text.Contains("\"Paste\":\"Values\""));
            text = write(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.Formulas, Transpose = true });
            t.True("other paste kinds are written by name", text.Contains("\"Paste\":\"Formulas\"") && text.Contains("\"Transpose\":true") && !text.Contains("ValuesOnly"));
            text = write(new ExcelSettings { Kind = ExcelKind.GetCell });
            t.False("empty options are not written", text.Contains("Paste") || text.Contains("ValuesOnly") || text.Contains("UseWorkbook") || text.Contains("DestSheet"));

            // fields added later read as empty from old files, never as null
            var old = First("{\"Category\":\"Excel\",\"Excel\":{\"Kind\":\"GetCell\",\"Cell\":\"A1\"}}").Excel;
            t.Eq("old Excel action: In workbook / In sheet / destination are empty", old.UseWorkbook + old.UseSheet + old.DestWorkbook + old.DestSheet + old.ReplaceWith, "");
            var command = First("{\"Category\":\"Utilities\",\"Command\":{\"Kind\":\"Open\",\"Value\":\"x\"}}").Command;
            t.Eq("old Utilities action: new text fields are empty", command.Target + command.Id, "");
            t.Eq("old Utilities action: Match is Title", command.Match, ProgramMatch.Title);

            // what a build makes of a name it does not know (documented in ROADMAP section 3)
            t.Eq("unknown category -> Mouse", First("{\"Category\":\"Teleport\"}").Category, ActionCategory.Mouse);
            t.Eq("unknown Control kind -> Loop", First("{\"Category\":\"Control\",\"Control\":\"Teleport\"}").Control, ControlKind.Loop);
            t.Eq("a Loop made from an unknown Control runs once", First("{\"Category\":\"Control\",\"Control\":\"Teleport\"}").Loop.Count, 1L);
            t.Eq("unknown Utilities kind -> Open (which is a Program action)", First("{\"Category\":\"Utilities\",\"Command\":{\"Kind\":\"Teleport\"}}").Command.Kind, CommandKind.Open);
            t.Eq("unknown Excel kind -> Workbook", First("{\"Category\":\"Excel\",\"Excel\":{\"Kind\":\"Teleport\"}}").Excel.Kind, ExcelKind.Workbook);
            t.Eq("unknown Web kind -> NewTab", First("{\"Category\":\"Web\",\"Web\":{\"Kind\":\"Teleport\"}}").Web.Kind, WebKind.NewTab);
            t.Eq("unknown Variable type -> Number", First("{\"Category\":\"Control\",\"Control\":\"Variable\",\"Variable\":{\"Id\":\"a\",\"Type\":\"Teleport\"}}").Variable.Type, VariableType.Number);
        }
    }
}
