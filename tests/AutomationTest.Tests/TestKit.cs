using System;
using System.Collections.Generic;

namespace AutomationTest.Tests
{
    /// <summary>Marks a static method <c>void Name(T t)</c> as a suite of checks.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class SuiteAttribute : Attribute
    {
        public SuiteAttribute(string name) { Name = name; }
        public string Name { get; }

        /// <summary>The suite starts Microsoft Excel (a hidden instance of its own); it runs only with --excel.</summary>
        public bool NeedsExcel { get; set; }
    }

    /// <summary>The checks of one suite: counts what passes, prints what fails.</summary>
    internal sealed class T
    {
        private readonly List<string> _failures = new List<string>();

        public T(string suite) { Suite = suite; }

        public string Suite { get; }
        public int Passed { get; private set; }
        public int Failed => _failures.Count;

        public void Eq(string what, object got, object want)
        {
            if (Equals(got, want)) { Passed++; return; }
            Fail(what + ": got <" + Show(got) + "> but wanted <" + Show(want) + ">");
        }

        public void True(string what, bool condition)
        {
            if (condition) { Passed++; return; }
            Fail(what + ": was false");
        }

        public void False(string what, bool condition)
        {
            True(what, !condition);
        }

        /// <summary>The action must fail with <see cref="InvalidOperationException"/> whose message contains the text.</summary>
        public void Throws(string what, Action action, string contains)
        {
            Throws<InvalidOperationException>(what, action, contains);
        }

        public void Throws<TException>(string what, Action action, string contains) where TException : Exception
        {
            try { action(); }
            catch (TException ex)
            {
                if (ex.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0) { Passed++; return; }
                Fail(what + ": the message <" + ex.Message + "> does not contain <" + contains + ">");
                return;
            }
            catch (Exception ex)
            {
                Fail(what + ": expected " + typeof(TException).Name + " but got " + ex.GetType().Name + ": " + ex.Message);
                return;
            }
            Fail(what + ": nothing was thrown");
        }

        public void Fail(string message)
        {
            _failures.Add(message);
            Console.WriteLine("  FAIL [" + Suite + "] " + message);
        }

        private static string Show(object value)
        {
            return value == null ? "null" : value.ToString();
        }
    }
}
