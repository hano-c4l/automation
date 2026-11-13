using System.Collections.Generic;

namespace AutomationTest.Core.Model
{
    /// <summary>
    /// An action inside a New Tab or Switch Tab uses the browser of that action: its Browser value is copied down the tree
    /// (through every container, Loop and Condition included) and cannot be edited there. The copy is kept in the action itself,
    /// so a file, Start Selected on one action and the editor all see the same value. Run it after loading and after every change
    /// of the tree.
    /// </summary>
    public static class BrowserInheritance
    {
        public static void Propagate(IEnumerable<ActionNode> roots)
        {
            Walk(roots, null);
        }

        private static void Walk(IEnumerable<ActionNode> nodes, BrowserKind? inherited)
        {
            foreach (var node in nodes)
            {
                BrowserKind? forChildren = inherited;
                if (node.Category == ActionCategory.Web && node.Web != null)
                {
                    node.Web.BrowserInherited = inherited.HasValue;
                    if (inherited.HasValue) node.Web.Browser = inherited.Value;
                    if (node.IsContainer) forChildren = node.Web.Browser;
                }
                if (node.Children.Count > 0) Walk(node.Children, forChildren);
            }
        }
    }
}
