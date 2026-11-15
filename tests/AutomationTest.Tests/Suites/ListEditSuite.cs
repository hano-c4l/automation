using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutomationTest.App;
using AutomationTest.Core.Model;

namespace AutomationTest.Tests.Suites
{
    /// <summary>Undo, redo, copy, cut, paste and moving actions by drag and drop in the list of actions.</summary>
    internal static class ListEditSuite
    {
        private sealed class MemoryStore : ActionClipboard.IActionStore
        {
            public string Text;
            public void Put(string json) { Text = json; }
            public string Get() { return Text; }
        }

        private static ActionNode Mouse(string name, int x = 1)
        {
            var m = ActionNode.NewMouse(new MouseSettings { X = x });
            m.Name = name;
            return m;
        }

        private static ActionNode Loop(string name, params ActionNode[] children)
        {
            var l = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.Times, Count = 2 });
            l.Name = name;
            l.Children.AddRange(children);
            return l;
        }

        private static string Names(IEnumerable<ActionNode> nodes)
        {
            return string.Join(",", nodes.Select(n => n.Name ?? n.Category.ToString()));
        }

        private static string MoveTo(EditorSuite.Editor e, ActionNode[] nodes, ActionNode target, string where, bool copy = false)
        {
            var enumType = typeof(MainForm).GetNestedType("DropWhere", BindingFlags.NonPublic);
            object w = Enum.Parse(enumType, where);
            return (string)typeof(MainForm).GetMethod("MoveNodesTo", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e.Form, new object[] { new List<ActionNode>(nodes), target, w, copy });
        }

        [Suite("list.undo")]
        public static void Undo(T t)
        {
            var e = new EditorSuite.Editor();
            e.Show();
            e.Call("NewScript");
            t.Eq("a new script is empty", e.Actions.Count, 0);

            // Add by the editor (a Section, which needs no fields), then undo and redo
            e.Choose(ControlKind.Section);
            e.Call("AddAction");
            e.Select(); // the new action is selected: without this the next one would go inside it
            e.Call("AddAction");
            t.Eq("two added", e.Actions.Count, 2);
            e.Call("UndoAction");
            t.Eq("undo takes the last one back", e.Actions.Count, 1);
            e.Call("UndoAction");
            t.Eq("undo again", e.Actions.Count, 0);
            e.Call("UndoAction");
            t.True("nothing more to undo", e.Actions.Count == 0 && e.Status.Contains("Nothing to undo"));
            e.Call("RedoAction");
            t.Eq("redo brings one back", e.Actions.Count, 1);
            e.Call("RedoAction");
            t.Eq("and the other", e.Actions.Count, 2);
            e.Call("RedoAction");
            t.True("nothing more to redo", e.Actions.Count == 2 && e.Status.Contains("Nothing to redo"));

            // a new change after undo forgets what could be redone
            e.Call("UndoAction");
            e.Choose(ControlKind.Return);
            e.Call("AddAction");
            e.Call("RedoAction");
            t.True("a new change clears the redo", e.Status.Contains("Nothing to redo"));

            // delete, move and update can be undone
            e.Show(Mouse("a"), Mouse("b"), Mouse("c"));
            e.Select(e.Actions[0]);
            e.Call("DeleteActions");
            t.Eq("deleted", Names(e.Actions), "b,c");
            e.Call("UndoAction");
            t.Eq("a delete is undone", Names(e.Actions), "a,b,c");
            e.Select(e.Actions[0]);
            e.Call("MoveUpDown", 1);
            t.Eq("moved down", Names(e.Actions), "b,a,c");
            e.Call("UndoAction");
            t.Eq("a move is undone", Names(e.Actions), "a,b,c");

            // a change that did not change anything is not an undo step
            e.Show(Mouse("only"));
            e.Call("NewScript");
            e.Show(Mouse("x"));
            e.Select(e.Actions[0]);
            e.Call("MoveUpDown", -1); // already first: nothing happens
            e.Call("UndoAction");
            t.True("an empty change is not remembered", e.Status.Contains("Nothing to undo"));

            // New clears the history
            e.Choose(ControlKind.Section);
            e.Call("AddAction");
            e.Call("NewScript");
            e.Call("UndoAction");
            t.True("New clears the history", e.Status.Contains("Nothing to undo"));
        }

        [Suite("list.copy-paste")]
        public static void CopyPaste(T t)
        {
            var saved = ActionClipboard.Store;
            var store = new MemoryStore();
            ActionClipboard.Store = store;
            try
            {
                var e = new EditorSuite.Editor();
                var child = Mouse("child");
                var loop = Loop("loop", child);
                var other = Mouse("other");
                e.Show(loop, other);

                // copying a Loop takes what is inside it, and a pasted copy is a different object
                e.Select(loop);
                e.Call("CopyActions", false);
                t.True("the clipboard has the actions", !string.IsNullOrEmpty(store.Text));
                e.Select();
                e.Call("PasteActions");
                t.Eq("pasted at the end when nothing is selected", Names(e.Actions), "loop,other,loop");
                t.Eq("with what was inside", Names(e.Actions[2].Children), "child");
                t.False("the copy is not the same object", ReferenceEquals(e.Actions[2], loop) || ReferenceEquals(e.Actions[2].Children[0], child));

                // selected parent and child: only the parent is copied (the child comes along)
                e.Select(loop, child);
                e.Call("CopyActions", false);
                e.Select(other);
                e.Call("PasteActions");
                t.Eq("a parent and its child are copied once", Names(e.Actions), "loop,other,loop,loop");

                // paste after the selected action; inside a selected container
                e.Show(Mouse("a"), Mouse("b"));
                e.Select(e.Actions[0]);
                e.Call("CopyActions", false);
                e.Select(e.Actions[0]);
                e.Call("PasteActions");
                t.Eq("paste goes after the selected action", Names(e.Actions), "a,a,b");
                var target = Loop("target");
                e.Show(Mouse("m"), target);
                e.Select(e.Actions[0]);
                e.Call("CopyActions", false);
                e.Select(target);
                e.Call("PasteActions");
                t.Eq("paste goes inside a selected Loop", Names(target.Children), "m");

                // cut removes and paste brings it back; undo puts it back where it was
                var x = Mouse("x"); var y = Mouse("y");
                e.Show(x, y);
                e.Select(x);
                e.Call("CopyActions", true);
                t.Eq("cut removes", Names(e.Actions), "y");
                e.Select(e.Actions[0]);
                e.Call("PasteActions");
                t.Eq("paste after cut", Names(e.Actions), "y,x");
                e.Call("UndoAction");
                e.Call("UndoAction");
                t.Eq("two undos bring back the cut action", Names(e.Actions), "x,y");

                // names, State and settings travel with the copy
                var disabled = Mouse("keep", 77);
                disabled.Disabled = true;
                e.Show(disabled);
                e.Select(disabled);
                e.Call("CopyActions", false);
                e.Call("PasteActions");
                t.True("a pasted action keeps its name, State and settings", e.Actions.Count == 2 && e.Actions[1].Name == "keep" && e.Actions[1].Disabled && e.Actions[1].Mouse.X == 77);

                // a Try: pasted into a selected Try goes before the Catch
                var tr = ActionNode.NewTry();
                e.Show(Mouse("p"), tr);
                e.Select(e.Actions[0]);
                e.Call("CopyActions", false);
                e.Select(tr);
                e.Call("PasteActions");
                t.True("into a Try, before the Catch", tr.Children.Count == 2 && tr.Children[0].Name == "p" && tr.Children[1].IsKind(ControlKind.Catch));

                // not allowed: lines of a Condition or Try, pasting into a Condition
                var cond = ActionNode.NewCondition();
                e.Show(cond);
                e.Select(cond.Children[0]);
                e.Call("CopyActions", false);
                t.True("a case line cannot be copied by itself", e.Status.Contains("whole Condition or Try"));
                e.Select(cond);
                e.Call("PasteActions");
                t.True("nothing goes straight into a Condition", cond.Children.Count == 2);

                // nothing on the clipboard
                store.Text = null;
                e.Show(Mouse("z"));
                e.Call("PasteActions");
                t.True("an empty clipboard pastes nothing", e.Actions.Count == 1 && e.Status.Contains("no copied actions"));
                store.Text = "this is not a script";
                e.Call("PasteActions");
                t.True("something else on the clipboard pastes nothing", e.Actions.Count == 1);
            }
            finally { ActionClipboard.Store = saved; }
        }

        [Suite("list.drop")]
        public static void Drop(T t)
        {
            var e = new EditorSuite.Editor();
            var a = Mouse("a"); var b = Mouse("b"); var c = Mouse("c");
            e.Show(a, b, c);
            t.Eq("move a after c", MoveTo(e, new[] { a }, c, "After"), null);
            t.Eq("order", Names(e.Actions), "b,c,a");
            t.Eq("move a before b", MoveTo(e, new[] { a }, b, "Before"), null);
            t.Eq("order again", Names(e.Actions), "a,b,c");
            t.Eq("two together keep their order", MoveTo(e, new[] { a, b }, c, "After"), null);
            t.Eq("order of two", Names(e.Actions), "c,a,b");

            // into a container, and out of it
            var inner = Mouse("inner");
            var loop = Loop("loop");
            e.Show(inner, loop, Mouse("tail"));
            t.Eq("into a Loop", MoveTo(e, new[] { inner }, loop, "Into"), null);
            t.Eq("the Loop has it", Names(loop.Children), "inner");
            t.Eq("top level", Names(e.Actions), "loop,tail");
            var tail = e.Actions[1];
            t.Eq("out again, before tail", MoveTo(e, new[] { inner }, tail, "Before"), null);
            t.Eq("top level after", Names(e.Actions), "loop,inner,tail");

            // not into itself or its own children
            var deep = Mouse("deep");
            var outer = Loop("outer", Loop("middle", deep));
            e.Show(outer);
            t.True("not into itself", MoveTo(e, new[] { outer }, outer, "Into").Contains("into itself"));
            t.True("not into its own child", MoveTo(e, new[] { outer }, deep, "After").Contains("into itself"));
            t.Eq("the tree is as it was", Names(outer.Children), "middle");

            // a copy (Ctrl held) leaves the original
            var one = Mouse("one");
            e.Show(one, Mouse("two"));
            t.Eq("copy after two", MoveTo(e, new[] { one }, e.Actions[1], "After", true), null);
            t.Eq("original stays, copy added", Names(e.Actions), "one,two,one");
            t.False("the copy is another object", ReferenceEquals(e.Actions[0], e.Actions[2]));

            // Condition and Try rules
            var cond = ActionNode.NewCondition();
            var movable = Mouse("movable");
            e.Show(movable, cond);
            t.True("not into a Condition", MoveTo(e, new[] { movable }, cond, "Into").Contains("holds cases"));
            t.True("not beside the case lines", MoveTo(e, new[] { movable }, cond.Children[0], "Before").Contains("holds cases"));
            t.Eq("into a case", MoveTo(e, new[] { movable }, cond.Children[0], "Into"), null);
            t.Eq("the case has it", Names(cond.Children[0].Children), "movable");
            t.True("a case line cannot be dragged", MoveTo(e, new[] { cond.Children[0] }, cond, "After").Contains("stay inside"));

            var tr = ActionNode.NewTry();
            var item = Mouse("item");
            e.Show(item, tr);
            t.Eq("before the Catch is fine", MoveTo(e, new[] { item }, tr.Children[0], "Before"), null);
            t.True("in the Try, before the Catch", tr.Children.Count == 2 && tr.Children[0].Name == "item" && tr.Children[1].IsKind(ControlKind.Catch));
            var late = Mouse("late");
            e.Show(late, tr);
            t.True("not after the Catch", MoveTo(e, new[] { late }, tr.Children[tr.Children.Count - 1], "After").Contains("stay last"));
            t.Eq("into a Try puts it before the Catch", MoveTo(e, new[] { late }, tr, "Into"), null);
            t.True("Catch still last", tr.Children[tr.Children.Count - 1].IsKind(ControlKind.Catch));

            // something that cannot hold actions
            var leaf = Mouse("leaf"); var mover = Mouse("mover");
            e.Show(leaf, mover);
            t.True("a Mouse action cannot hold actions", MoveTo(e, new[] { mover }, leaf, "Into").Contains("cannot hold"));
        }
    }
}
