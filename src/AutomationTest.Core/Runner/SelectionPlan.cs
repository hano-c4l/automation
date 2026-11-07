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
    /// - a selected Case or Else runs its actions, without testing its condition.
    /// </summary>
    public static class SelectionPlanner
    {
        public static SelectionPlan Build(AutomationScript script, IEnumerable<ActionNode> selected)
        {
            var chosen = new HashSet<ActionNode>(selected);
            var plan = new SelectionPlan();
            Collect(script.Actions, chosen, plan.RunNodes);
            FindNeeds(script, plan);
            return plan;
        }

        private static void Collect(List<ActionNode> nodes, HashSet<ActionNode> chosen, List<ActionNode> run)
        {
            foreach (var node in nodes)
            {
                if (chosen.Contains(node))
                {
                    if (node.IsKind(ControlKind.Case) || node.IsKind(ControlKind.Else)) run.AddRange(node.Children);
                    else run.Add(node);
                }
                else
                {
                    Collect(node.Children, chosen, run);
                }
            }
        }

        // ---------------------------------------------------------------- variables

        private static void FindNeeds(AutomationScript script, SelectionPlan plan)
        {
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in plan.RunNodes) Walk(node, defined, needed, plan.Needs);
            foreach (var need in plan.Needs) Describe(script.Actions, need);
        }

        /// <summary>Goes through the actions in the order they run: reads come before the node's own definition.</summary>
        private static void Walk(ActionNode node, HashSet<string> defined, HashSet<string> needed, List<VariableNeed> needs)
        {
            foreach (string name in Reads(node))
            {
                if (defined.Contains(name) || !needed.Add(name)) continue;
                needs.Add(new VariableNeed { Name = name });
            }
            string id = Defines(node);
            if (id != null) defined.Add(id);
            foreach (var child in node.Children) Walk(child, defined, needed, needs);
        }

        private static IEnumerable<string> Reads(ActionNode node)
        {
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
                case ActionCategory.Control:
                    if (node.IsKind(ControlKind.Variable) && node.Variable.UseExpression) text = node.Variable.Expression;
                    else if (node.IsKind(ControlKind.Case)) text = node.Case.Expression;
                    break;
            }
            if (string.IsNullOrWhiteSpace(text)) return new string[0];
            try { return Expression.ReferencedVariables(text, list); }
            catch (ExpressionException) { return new string[0]; } // a broken expression is reported when it runs
        }

        private static string Defines(ActionNode node)
        {
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
                        need.Type = node.Variable.Type;
                        if (!node.Variable.UseExpression)
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
