using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace AutomationTest.Tests
{
    /// <summary>
    /// Runs every suite. Options: --excel also runs the suites that start Excel, --only NAME runs the suites whose name contains
    /// NAME, --list prints the names. Exit code 0 = all passed, 1 = something failed.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            bool excel = args.Contains("--excel");
            string only = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--only") only = args[i + 1];

            var suites = Assembly.GetExecutingAssembly().GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Select(m => new { Method = m, Attribute = m.GetCustomAttribute<SuiteAttribute>() })
                .Where(x => x.Attribute != null)
                .OrderBy(x => x.Attribute.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (args.Contains("--list"))
            {
                foreach (var s in suites) Console.WriteLine(s.Attribute.Name + (s.Attribute.NeedsExcel ? "  (needs Excel: --excel)" : ""));
                return 0;
            }

            int passed = 0, failed = 0, skipped = 0;
            var failedSuites = new List<string>();
            var watch = Stopwatch.StartNew();
            foreach (var s in suites)
            {
                string name = s.Attribute.Name;
                if (only != null && name.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (s.Attribute.NeedsExcel && !excel) { skipped++; Console.WriteLine("skip  " + name + " (add --excel)"); continue; }

                var t = new T(name);
                try { s.Method.Invoke(null, new object[] { t }); }
                catch (TargetInvocationException ex)
                {
                    t.Fail("the suite crashed: " + (ex.InnerException ?? ex));
                }
                catch (Exception ex)
                {
                    t.Fail("the suite crashed: " + ex);
                }
                passed += t.Passed;
                failed += t.Failed;
                if (t.Failed > 0) failedSuites.Add(name);
                Console.WriteLine((t.Failed == 0 ? "ok    " : "FAIL  ") + name + " (" + t.Passed + " passed" + (t.Failed > 0 ? ", " + t.Failed + " failed" : "") + ")");
            }

            Console.WriteLine();
            Console.WriteLine((failed == 0 ? "ALL PASSED: " : "FAILED: ") + passed + " checks passed, " + failed + " failed" + (skipped > 0 ? ", " + skipped + " suite(s) skipped" : "") + " in " + watch.Elapsed.TotalSeconds.ToString("0.0") + " s");
            if (failedSuites.Count > 0) Console.WriteLine("Failed suites: " + string.Join(", ", failedSuites));
            return failed == 0 ? 0 : 1;
        }
    }
}
