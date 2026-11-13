using System;
using System.Linq;

namespace AutomationTest.Core.Model
{
    /// <summary>How long a group of actions takes, as the sum of the Time and Delay settings of the actions in it.</summary>
    public static class ActionTiming
    {
        /// <summary>
        /// The time one pass over the actions inside a Loop takes (null when it cannot be known: a nested Loop that has no fixed
        /// count). Disabled actions are left out. A Condition counts its longest branch; a Wait's own time-out is not counted.
        /// </summary>
        public static long? PerIterationMs(ActionNode loop)
        {
            return SumChildren(loop);
        }

        private static long? SumChildren(ActionNode parent)
        {
            long total = 0;
            foreach (var child in parent.Children)
            {
                if (child.Disabled) continue;
                long? one = Own(child);
                if (!one.HasValue) return null;
                total += one.Value;
            }
            return total;
        }

        private static long? Own(ActionNode node)
        {
            switch (node.Category)
            {
                case ActionCategory.Mouse:
                    return node.Mouse.TimeMs + node.Mouse.DelayMs;
                case ActionCategory.Keyboard:
                    var k = node.Keyboard;
                    bool held = k.Action == KeyboardAction.KeyPress || k.Action == KeyboardAction.ComboKey || k.Action == KeyboardAction.Text;
                    return (held ? k.TimeMs : 0) + k.DelayMs;
                case ActionCategory.Utilities:
                    return node.Command.Kind == CommandKind.Shutdown ? node.Command.TimeMs : node.Command.DelayMs;
                case ActionCategory.Web:
                    return node.Web.DelayMs;
                default:
                    break;
            }

            switch (node.Control)
            {
                case ControlKind.Loop:
                    long? inner = SumChildren(node);
                    if (!inner.HasValue) return null;
                    var loop = node.Loop;
                    if (loop.Mode == LoopType.Times && loop.Count > 0) return inner.Value * loop.Count;
                    if (loop.Mode == LoopType.Duration) return loop.DurationMs;
                    return null; // forever, or until a time of day
                case ControlKind.Condition:
                    long longest = 0;
                    foreach (var branch in node.Children.Where(b => !b.Disabled))
                    {
                        long? branchTime = SumChildren(branch);
                        if (!branchTime.HasValue) return null;
                        longest = Math.Max(longest, branchTime.Value);
                    }
                    return longest;
                case ControlKind.Variable:
                    return 0;
                default: // Wait, Image: what is inside them
                    return SumChildren(node);
            }
        }
    }
}
