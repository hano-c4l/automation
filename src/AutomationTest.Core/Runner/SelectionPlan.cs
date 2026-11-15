using System;
using System.Collections.Generic;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Model;

namespace AutomationTest.Core.Runner
{
    /// <summary>A variable the selected actions read but nothing in the selection gives a value to.</summary>
    public sealed class VariableNeed
    {
        public string Name;
        /// <summary>The type declared by a Variable action of this name elsewhere in the script; null when unknown.</summary>
        public VariableType? Type;
        /// <summary>True when an Image action of this name exists elsewhere (its value is Found, Position and Size).</summary>
        public bool IsImage;
        /// <summary>The value that Variable action sets (when it is a plain value, not an expression); null when there is none.</summary>
        public object Default;
    }

    /// <summary>What to run for a selection of actions and which variables the user has to provide first.</summary>
    public sealed class SelectionPlan
    {
        /// <summary>The actions to run, in script order. They are the live nodes; clone them before running.</summary>
        public List<ActionNode> RunNodes = new List<ActionNode>();
        public List<VariableNeed> Needs = new List<VariableNeed>();
    }

    /// <summary>
    /// Works out how to run only some actions of a script, to try them out quickly:
    /// - every selected action runs as it is, wherever it sits (an action inside a Loop or Wait runs once, without the Loop or Wait);
    /// - a selected Loop, Wait, Image or Condition runs together with everything inside it;
    /// - an action whose parent is selected too is part of the parent and is not run a second time;
    /// - a selected Case or Else runs its actions, without testing its condition;
/// - a disabled action inside what runs is skipped as usual, but an action that is selected itself runs even if it is disabled
///   (the caller enables the selected copies), because choosing it is an explicit request.
    /// </summary>
    public static class SelectionPlanner
    {
        public static SelectionPlan Build(AutomationScript script, IEnumerable<ActionNode> selected)
        {
            var chosen = new HashSet<ActionNode>(selected);
            var plan = new SelectionPlan();
            Collect(script.Actions, chosen, plan.RunNodes);
            FindNeeds(script, plan, chosen);
            return plan;
        }

        private static void Collect(List<ActionNode> nodes, HashSet<ActionNode> chosen, List<ActionNode> run)
        {
            foreach (var node in nodes)
            {
                if (chosen.Contains(node))
                {
                    if (node.IsKind(ControlKind.Case) || node.IsKind(ControlKind.Else) || node.IsKind(ControlKind.Catch)) run.AddRange(node.Children);
                    else run.Add(node);
                }
                else
                {
                    Collect(node.Children, chosen, run);
                }
            }
        }

        // ---------------------------------------------------------------- variables

        private static void FindNeeds(AutomationScript script, SelectionPlan plan, HashSet<ActionNode> chosen)
        {
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in plan.RunNodes) Walk(node, chosen.Contains(node), defined, needed, plan.Needs);
            foreach (var need in plan.Needs) Describe(script.Actions, need);
        }

        /// <summary>Goes through the actions in the order they run: reads come before the node's own definition.</summary>
        private static void Walk(ActionNode node, bool chosen, HashSet<string> defined, HashSet<string> needed, List<VariableNeed> needs)
        {
            if (!chosen && node.Disabled) return; // a disabled action inside the selection does not run, so it reads and sets nothing
            foreach (string name in Reads(node))
            {
                if (defined.Contains(name) || !needed.Add(name)) continue;
                needs.Add(new VariableNeed { Name = name });
            }
            foreach (string id in DefinesAll(node)) defined.Add(id);
            foreach (var child in node.Children) Walk(child, false, defined, needed, needs);
        }

        private static IEnumerable<string> ReadsOf(params string[] texts)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in texts)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                try { foreach (string name in Expression.ReferencedVariables(text, false)) names.Add(name); }
                catch (ExpressionException) { } // a broken expression is reported when it runs
            }
            return names;
        }

        private static IEnumerable<string> Reads(ActionNode node)
        {
            if (node.Category == ActionCategory.Excel)
            {
                var x = node.Excel;
                bool valueKind = ScriptRunner.ExcelValueKind(x.Kind);
                return ReadsOf(x.CellIsExpression ? x.Cell : null, x.Kind == ExcelKind.SetRange || (valueKind && x.ValueIsExpression) ? x.Value : null);
            }
            if (node.Category == ActionCategory.Email)
            {
                var m = node.Email;
                var mailTexts = new List<string>();
                if (m.Kind == EmailKind.Send)
                {
                    if (m.ToIsExpression) { mailTexts.Add(m.To); mailTexts.Add(m.Cc); mailTexts.Add(m.Bcc); }
                    if (m.BodyIsExpression) mailTexts.Add(m.Body);
                    if (m.AttachmentsIsExpression) mailTexts.Add(m.Attachments);
                }
                if (m.SubjectIsExpression) mailTexts.Add(m.Subject);
                if (m.DatesAreExpressions) { mailTexts.Add(m.Since); mailTexts.Add(m.Before); }
                return ReadsOf(mailTexts.ToArray());
            }
            if (node.Category == ActionCategory.Utilities || node.Category == ActionCategory.Program)
            {
                var c = node.Command;
                if (CommandKinds.IsApi(c.Kind))
                {
                    var texts = new List<string> { c.ValueIsExpression ? c.Value : null, c.TargetIsExpression ? c.Target : null };
                    string ignored;
                    foreach (var header in ApiClient.ParseHeaders(c.Params, out ignored)) if (header.Item3) texts.Add(header.Item2);
                    return ReadsOf(texts.ToArray());
                }
                if (c.Kind == CommandKind.AutoScript)
                {
                    var texts = new List<string> { c.ValueIsExpression ? c.Value : null };
                    foreach (var pair in ScriptRunner.ParseScriptInputs(c.Params, out _)) texts.Add(pair.Value);
                    return ReadsOf(texts.ToArray());
                }
                if (CommandKinds.IsProgram(c.Kind)) return ReadsOf(c.ValueIsExpression ? c.Value : null);
                return ScriptRunner.CommandIsFileSystem(c.Kind) ? ReadsOf(c.ValueIsExpression ? c.Value : null, c.TargetIsExpression ? c.Target : null) : new string[0];
            }
            if (node.IsKind(ControlKind.Loop) && node.Loop.Mode == LoopType.For) return ReadsOf(node.Loop.ForFrom, node.Loop.ForTo, node.Loop.ForStep);

            string text = null;
            bool list = false;
            switch (node.Category)
            {
                case ActionCategory.Mouse:
                    if (node.Mouse.UseVariable) { text = node.Mouse.PositionExpression; list = true; }
                    break;
                case ActionCategory.Keyboard:
                    if (node.Keyboard.Action == KeyboardAction.Text && node.Keyboard.ValueIsExpression) text = node.Keyboard.Value;
                    break;
                case ActionCategory.Web:
                    if (ScriptRunner.WebTextKind(node.Web) && node.Web.TextIsExpression) text = node.Web.Text;
                    else if (node.Web.Kind == WebKind.Script && node.Web.TextIsExpression) text = node.Web.Script;
                    break;
                case ActionCategory.Control:
                    if (node.IsKind(ControlKind.Variable) && node.Variable.UseExpression) text = node.Variable.Expression;
                    else if (node.IsKind(ControlKind.Case)) text = node.Case.Expression;
                    break;
            }
            if (string.IsNullOrWhiteSpace(text)) return new string[0];
            try { return Expression.ReferencedVariables(text, list); }
            catch (ExpressionException) { return new string[0]; } // a broken expression is reported when it runs
        }

        /// <summary>Everything the action sets: one variable for most actions, the returned ones for an Auto Script.</summary>
        private static IEnumerable<string> DefinesAll(ActionNode node)
        {
            if ((node.Category == ActionCategory.Utilities) && node.Command.Kind == CommandKind.AutoScript)
                return ScriptRunner.ParseScriptReturns(node.Command.Returns, out _);
            string one = Defines(node);
            return one == null ? new string[0] : new[] { one };
        }

        private static string Defines(ActionNode node)
        {
            if (node.Category == ActionCategory.Excel)
            {
                var x = node.Excel;
                bool keeps = x.Kind == ExcelKind.GetCell || x.Kind == ExcelKind.GetRange || x.Kind == ExcelKind.GetInfo || x.Kind == ExcelKind.Find || x.Kind == ExcelKind.Evaluate || x.Kind == ExcelKind.RunMacro;
                return keeps && !string.IsNullOrWhiteSpace(x.Id) ? x.Id.Trim() : null;
            }
            if (node.Category == ActionCategory.Email)
                return (node.Email.Kind == EmailKind.Send || string.IsNullOrWhiteSpace(node.Email.Id)) ? null : node.Email.Id.Trim();
            if (node.IsKind(ControlKind.Catch)) return string.IsNullOrWhiteSpace(node.Catch?.Id) ? null : node.Catch.Id.Trim(); // the error message
            if (node.Category == ActionCategory.Utilities || node.Category == ActionCategory.Program)
                return ScriptRunner.CommandKeeps(node.Command.Kind) && !string.IsNullOrWhiteSpace(node.Command.Id) ? node.Command.Id.Trim() : null;
            if (node.IsKind(ControlKind.Loop) && node.Loop.Mode == LoopType.For)
                return string.IsNullOrWhiteSpace(node.Loop.ForId) ? null : node.Loop.ForId.Trim();
            if (node.Category == ActionCategory.Web)
                return (node.Web.Kind == WebKind.GetText || node.Web.Kind == WebKind.Script) && !string.IsNullOrWhiteSpace(node.Web.Id) ? node.Web.Id.Trim() : null;
            if (node.Category != ActionCategory.Control) return null;
            string id = node.IsKind(ControlKind.Variable) ? node.Variable.Id : node.IsKind(ControlKind.Image) ? node.Image.Id : null;
            return string.IsNullOrWhiteSpace(id) ? null : id.Trim();
        }

        /// <summary>Looks for a Variable or Image action of that name anywhere in the script to learn the type and a starting value.</summary>
        private static void Describe(List<ActionNode> nodes, VariableNeed need)
        {
            foreach (var node in nodes)
            {
                if (node.Category == ActionCategory.Control)
                {
                    if (node.IsKind(ControlKind.Variable) && string.Equals(node.Variable.Id?.Trim(), need.Name, StringComparison.OrdinalIgnoreCase) && !need.Type.HasValue)
                    {
                        if (node.Variable.Type == VariableType.Image) need.IsImage = true; // its value is Found, Position and Size
                        else need.Type = node.Variable.Type == VariableType.OCR ? VariableType.String : node.Variable.Type;
                        if (!node.Variable.UseExpression && !node.Variable.HasNoStoredValue)
                        {
                            try { need.Default = node.Variable.GetLiteral(); }
                            catch (ExpressionException) { }
                        }
                    }
                    else if (node.IsKind(ControlKind.Image) && string.Equals(node.Image.Id?.Trim(), need.Name, StringComparison.OrdinalIgnoreCase) && !need.Type.HasValue)
                    {
                        need.IsImage = true;
                    }
                }
                Describe(node.Children, need);
                if (need.Type.HasValue || need.IsImage) return;
            }
        }
    }
}
