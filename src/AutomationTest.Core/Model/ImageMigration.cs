using System;
using System.Collections.Generic;
using System.Linq;
using AutomationTest.Core.Expressions;

namespace AutomationTest.Core.Model
{
    /// <summary>
    /// The Image action no longer exists: it is a Variable of the type Image now. A script saved with the old action is read as a
    /// Variable (Image) followed, when the old action had actions inside it, by a Condition on {id.Found} that holds them.
    /// </summary>
    internal static class ImageMigration
    {
        public static void Apply(List<ActionNode> actions)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Collect(actions, used);
            Convert(actions, used);
        }

        /// <summary>Every ID that something in the script already uses, so a made-up ID never clashes.</summary>
        private static void Collect(List<ActionNode> nodes, HashSet<string> used)
        {
            foreach (var node in nodes)
            {
                if (node.Variable != null && !string.IsNullOrWhiteSpace(node.Variable.Id)) used.Add(node.Variable.Id.Trim());
                if (node.Image != null && !string.IsNullOrWhiteSpace(node.Image.Id)) used.Add(node.Image.Id.Trim());
                if (node.Loop != null && !string.IsNullOrWhiteSpace(node.Loop.ForId)) used.Add(node.Loop.ForId.Trim());
                if (node.Catch != null && !string.IsNullOrWhiteSpace(node.Catch.Id)) used.Add(node.Catch.Id.Trim());
                if (node.Command != null && !string.IsNullOrWhiteSpace(node.Command.Id)) used.Add(node.Command.Id.Trim());
                if (node.Excel != null && !string.IsNullOrWhiteSpace(node.Excel.Id)) used.Add(node.Excel.Id.Trim());
                if (node.Web != null && !string.IsNullOrWhiteSpace(node.Web.Id)) used.Add(node.Web.Id.Trim());
                Collect(node.Children, used);
            }
        }

        private static void Convert(List<ActionNode> nodes, HashSet<string> used)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                Convert(node.Children, used);
                if (!node.IsKind(ControlKind.Image)) continue;

                string id = (node.Image.Id ?? "").Trim();
                if (!Expression.IsValidId(id))
                {
                    int number = 1;
                    while (used.Contains("image" + number)) number++;
                    id = "image" + number;
                    used.Add(id);
                }
                node.Image.Id = id;

                var variable = ActionNode.NewVariable(new VariableSettings { Id = id, Type = VariableType.Image });
                variable.Image = node.Image;
                variable.Name = node.Name;
                variable.Disabled = node.Disabled;
                nodes[i] = variable;

                if (node.Children.Count > 0)
                {
                    var condition = ActionNode.NewCondition();
                    condition.Children[0].Case.Expression = "{" + id + ".Found}";
                    condition.Children[0].Children.AddRange(node.Children);
                    condition.Name = node.Name;
                    condition.Disabled = node.Disabled;
                    nodes.Insert(i + 1, condition);
                    i++;
                }
            }
        }
    }
}
