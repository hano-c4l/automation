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
    /// a window by position. Index is 1 for the first tab; 0 means "not set". The Title and URL patterns are regular expressions
    /// (not case sensitive) on the title and on the address of the tab; a tab has to match every pattern that is given.
    /// - Patterns only: the first tab that matches.
    /// - Index only: the Index-th tab of the window that was last in use.
    /// - Patterns and Index: the Index-th of the tabs that match.
    /// An Index beyond the end chooses the last one.
    /// </summary>
    public static class TabSelector
    {
        public const int RegexTimeoutMs = 1000;

        /// <returns>The tab, or null when nothing matches (no tab, or no tab matches the patterns).</returns>
        /// <exception cref="ArgumentException">A pattern is not a valid regular expression, or Title, URL and Index are all empty.</exception>
        public static TabInfo Choose(IEnumerable<TabInfo> tabs, int lastWindowId, string titlePattern, string urlPattern, int index)
        {
            bool hasTitle = !string.IsNullOrWhiteSpace(titlePattern);
            bool hasUrl = !string.IsNullOrWhiteSpace(urlPattern);
            if (!hasTitle && !hasUrl && index <= 0) throw new ArgumentException("Enter a Title pattern, a URL pattern, an Index, or a combination.");

            var ordered = tabs.OrderBy(t => t.WindowId == lastWindowId ? 0 : 1).ThenBy(t => t.WindowId).ThenBy(t => t.Index).ToList();
            if (!hasTitle && !hasUrl)
            {
                var window = ordered.Where(t => t.WindowId == lastWindowId).ToList();
                if (window.Count == 0) window = ordered; // the last window is not known
                return window.Count == 0 ? null : window[Math.Min(index, window.Count) - 1];
            }

            var timeout = TimeSpan.FromMilliseconds(RegexTimeoutMs);
            var titleRegex = hasTitle ? new Regex(titlePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, timeout) : null;
            var urlRegex = hasUrl ? new Regex(urlPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, timeout) : null;
            var matches = new List<TabInfo>();
            foreach (var tab in ordered)
            {
                try
                {
                    if (titleRegex != null && !titleRegex.IsMatch(tab.Title ?? "")) continue;
                    if (urlRegex != null && !urlRegex.IsMatch(tab.Url ?? "")) continue;
                    matches.Add(tab);
                }
                catch (RegexMatchTimeoutException) { /* a pattern that takes too long matches nothing */ }
            }
            if (matches.Count == 0) return null;
            return matches[index <= 0 ? 0 : Math.Min(index, matches.Count) - 1];
        }
    }
}
