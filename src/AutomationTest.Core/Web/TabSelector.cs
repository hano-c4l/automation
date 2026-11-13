using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AutomationTest.Core.Web
{
    /// <summary>One open browser tab.</summary>
    public sealed class TabInfo
    {
        public int Id;
        public int WindowId;
        /// <summary>Position of the tab in its window, from 0.</summary>
        public int Index;
        public string Title = "";
        public string Url = "";
    }

    /// <summary>
    /// Picks the tab for a Switch Tab action. Tab order: the window that was last in use first, then the other windows by id; inside
    /// a window by position. Index is 1 for the first tab; 0 means "not set".
    /// - Title only: the first tab whose title matches the pattern.
    /// - Index only: the Index-th tab of the window that was last in use.
    /// - Both: the Index-th of the tabs whose title matches.
    /// An Index beyond the end chooses the last one.
    /// </summary>
    public static class TabSelector
    {
        public const int RegexTimeoutMs = 1000;

        /// <returns>The tab, or null when nothing matches (no tab, or no title matches).</returns>
        /// <exception cref="ArgumentException">The Title pattern is not a valid regular expression, or Title and Index are both empty.</exception>
        public static TabInfo Choose(IEnumerable<TabInfo> tabs, int lastWindowId, string titlePattern, int index)
        {
            bool hasTitle = !string.IsNullOrWhiteSpace(titlePattern);
            if (!hasTitle && index <= 0) throw new ArgumentException("Enter a Title pattern, an Index, or both.");

            var ordered = tabs.OrderBy(t => t.WindowId == lastWindowId ? 0 : 1).ThenBy(t => t.WindowId).ThenBy(t => t.Index).ToList();
            if (!hasTitle)
            {
                var window = ordered.Where(t => t.WindowId == lastWindowId).ToList();
                if (window.Count == 0) window = ordered; // the last window is not known
                return window.Count == 0 ? null : window[Math.Min(index, window.Count) - 1];
            }

            var regex = new Regex(titlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(RegexTimeoutMs));
            var matches = new List<TabInfo>();
            foreach (var tab in ordered)
            {
                try { if (regex.IsMatch(tab.Title ?? "")) matches.Add(tab); }
                catch (RegexMatchTimeoutException) { /* a pattern that takes too long matches nothing */ }
            }
            if (matches.Count == 0) return null;
            return matches[index <= 0 ? 0 : Math.Min(index, matches.Count) - 1];
        }
    }
}
