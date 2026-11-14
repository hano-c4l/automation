using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using AutomationTest.App;
using AutomationTest.Core.Excel;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Model;

namespace AutomationTest.Tests.Suites
{
    /// <summary>
    /// The Excel actions on a real Excel. Excel is started hidden as an instance of its own (the isolated host never attaches to an
    /// Excel the user has open) and is quit at the end; each suite checks that no EXCEL.EXE is left behind. Run with --excel.
    /// </summary>
    internal static class ExcelSuite
    {
        private sealed class Book : IDisposable
        {
            public readonly ExcelSession Session;
            private readonly int _before;
            private readonly T _t;

            public Book(T t)
            {
                _t = t;
                _before = Process.GetProcessesByName("EXCEL").Length;
                Session = new ExcelSession(new ExcelComHost(true));
            }

            public object Run(ExcelSettings x, string cell = "", object value = null)
            {
                return Session.Execute(x, new ExcelArgs { Cell = cell, Value = value ?? x.Value }, CancellationToken.None);
            }

            public object Get(string cell, string workbook = "", string sheet = "", ExcelGetKind kind = ExcelGetKind.Value)
            {
                return Run(new ExcelSettings { Kind = ExcelKind.GetCell, Get = kind, UseWorkbook = workbook, UseSheet = sheet }, cell);
            }

            public void Set(string cell, object value, ExcelSetKind how = ExcelSetKind.Auto)
            {
                Run(new ExcelSettings { Kind = ExcelKind.SetCell, As = how }, cell, value);
            }

            public object Eval(string formula)
            {
                return Run(new ExcelSettings { Kind = ExcelKind.Evaluate }, "", formula);
            }

            public object Info(ExcelInfoKind kind, string workbook = "")
            {
                return Run(new ExcelSettings { Kind = ExcelKind.GetInfo, Info = kind, UseWorkbook = workbook });
            }

            public void NewWorkbook()
            {
                Run(new ExcelSettings { Kind = ExcelKind.Workbook, Visible = false });
            }

            /// <summary>The COM workbook behind the session, for checks the actions cannot make.</summary>
            /// <summary>An open workbook by name (the one in use when no name is given).</summary>
            public dynamic Com(string name = null)
            {
                if (name != null)
                {
                    var current = Session.GetType().GetField("_workbook", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Session);
                    dynamic app = current.GetType().GetField("App").GetValue(current);
                    return app.Workbooks[name];
                }
                var wb = Session.GetType().GetField("_workbook", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Session);
                return wb.GetType().GetField("Workbook").GetValue(wb);
            }

            public void Dispose()
            {
                try { Session.EndRun(); } catch (Exception) { }
                try { Session.Dispose(); } catch (Exception) { }
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                for (int i = 0; i < 80 && Process.GetProcessesByName("EXCEL").Length > _before; i++) Thread.Sleep(250);
                int left = Process.GetProcessesByName("EXCEL").Length - _before;
                if (left > 0) _t.Fail("Excel was left running (" + left + " EXCEL.EXE process(es) more than before)");
            }
        }

        [Suite("excel.basics", NeedsExcel = true)]
        public static void Basics(T t)
        {
            using (var b = new Book(t))
            {
                b.NewWorkbook();

                // values keep their type; text stays text
                b.Set("A1", 12.5); b.Set("A2", "007", ExcelSetKind.Text); b.Set("A3", true); b.Set("A4", "=1+2", ExcelSetKind.Formula);
                t.Eq("number", b.Get("A1"), 12.5);
                t.Eq("Text keeps the leading zeros", b.Get("A2"), "007");
                t.Eq("boolean", b.Get("A3"), true);
                t.Eq("formula result", b.Get("A4"), 3.0);
                t.Eq("formula text", b.Get("A4", kind: ExcelGetKind.Formula), "=1+2");
                t.Eq("empty cell", b.Get("Z9", kind: ExcelGetKind.IsEmpty), true);
                t.Throws("a bad address", () => b.Get("not a cell"), "not a valid");

                // sheets
                b.Run(new ExcelSettings { Kind = ExcelKind.CreateSheet, SheetName = "Data" });
                t.Eq("the new sheet is the current one", b.Info(ExcelInfoKind.SheetName), "Data");
                t.Throws("a second sheet with the same name", () => b.Run(new ExcelSettings { Kind = ExcelKind.CreateSheet, SheetName = "data" }), "already");
                b.Run(new ExcelSettings { Kind = ExcelKind.RenameSheet }, "", "Renamed");
                t.Eq("rename", b.Info(ExcelInfoKind.SheetName), "Renamed");
                b.Run(new ExcelSettings { Kind = ExcelKind.SelectSheet, SheetName = "^Sheet1$" });
                t.Eq("select sheet", b.Info(ExcelInfoKind.SheetName), "Sheet1");
                b.Run(new ExcelSettings { Kind = ExcelKind.DeleteSheet, SheetName = "^Renamed$" });
                t.Eq("delete sheet", b.Info(ExcelInfoKind.SheetCount), 1.0);
                t.Throws("the last sheet cannot be deleted", () => b.Run(new ExcelSettings { Kind = ExcelKind.DeleteSheet }), "at least one sheet");

                // ranges and tables
                var table = (TableValue)b.Run(new ExcelSettings { Kind = ExcelKind.GetRange, Get = ExcelGetKind.Value }, "A1:A3");
                t.Eq("range rows", table.Rows, 3);
                t.Eq("range value", table.Get(1, 1), 12.5);
                b.Run(new ExcelSettings { Kind = ExcelKind.SetRange, As = ExcelSetKind.Text }, "C1", table);
                t.Eq("a table written back", b.Get("C2"), "007");
                t.Eq("last row of a column", b.Run(new ExcelSettings { Kind = ExcelKind.GetInfo, Info = ExcelInfoKind.LastRow }, "C1"), 3.0);
                t.Eq("used range", b.Info(ExcelInfoKind.UsedRange), "A1:C4");

                // find
                var found = (CellValue)b.Run(new ExcelSettings { Kind = ExcelKind.Find }, "A1:C4", "007");
                t.True("find: found", found.Found);
                t.Eq("find: address", found.Address, "A2");
                t.False("find: not found", ((CellValue)b.Run(new ExcelSettings { Kind = ExcelKind.Find }, "A1:C4", "zzz")).Found);

                // sort and clear
                b.Set("E1", 3.0); b.Set("E2", 1.0); b.Set("E3", 2.0);
                b.Run(new ExcelSettings { Kind = ExcelKind.Sort, SortBy = "E" }, "E1:E3");
                t.Eq("sorted", b.Get("E1"), 1.0);
                b.Run(new ExcelSettings { Kind = ExcelKind.Clear, ClearWhat = ExcelClearKind.Contents }, "E1:E3");
                t.Eq("cleared", b.Get("E1", kind: ExcelGetKind.IsEmpty), true);

                // formulas are evaluated in Excel's English syntax
                t.Eq("evaluate", b.Eval("SUM(A1:A1,2)"), 14.5);

                // save as csv into the temp folder
                string csv = Path.Combine(Path.GetTempPath(), "at_excel_" + Guid.NewGuid().ToString("N") + ".csv");
                try
                {
                    b.Run(new ExcelSettings { Kind = ExcelKind.Save, SaveAs = true, SavePath = csv });
                    t.True("Save As csv wrote the file", File.Exists(csv));
                }
                finally { try { File.Delete(csv); } catch (IOException) { } }
            }
        }

        // The checks that need Excel's own objects live in their own methods: a dynamic COM object that stays alive in the test
        // keeps Excel from quitting (the program itself lets go of everything).
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void MakeTable(Book b, string area)
        {
            dynamic sheet = b.Com().ActiveSheet;
            sheet.ListObjects.Add(1, sheet.Range[area], Type.Missing, 1);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool TableHasFilterButtons(Book b)
        {
            dynamic sheet = b.Com().ActiveSheet;
            return (bool)sheet.ListObjects[1].ShowAutoFilter;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int SheetCount(Book b) { dynamic book = b.Com("Book1"); return (int)book.Worksheets.Count; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string SheetNameAt(Book b, int index) { dynamic book = b.Com("Book1"); return (string)book.Worksheets[index].Name; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int SheetVisible(Book b, string name) { dynamic book = b.Com("Book1"); return (int)book.Worksheets[name].Visible; }

        [Suite("excel.filter-and-context", NeedsExcel = true)]
        public static void FilterAndContext(T t)
        {
            using (var b = new Book(t))
            {
                b.NewWorkbook();
                string firstName = (string)b.Info(ExcelInfoKind.WorkbookName);
                for (int r = 1; r <= 6; r++) { b.Set("A" + r, (double)r); b.Set("B" + r, r == 1 ? "h" : "v" + r); }
                MakeTable(b, "A1:B6");

                // an Excel Table has a filter of its own
                b.Run(new ExcelSettings { Kind = ExcelKind.Filter, FilterField = 1, Criteria = ">3" }, "A1:B6");
                t.Eq("table filtered", b.Eval("SUBTOTAL(103,A2:A6)"), 3.0);
                b.Run(new ExcelSettings { Kind = ExcelKind.Filter, ClearFilter = true }, "");
                t.Eq("table filter cleared", b.Eval("SUBTOTAL(103,A2:A6)"), 5.0);
                t.True("the table keeps its filter buttons", TableHasFilterButtons(b));

                b.Run(new ExcelSettings { Kind = ExcelKind.CreateSheet, SheetName = "Plain" });
                for (int r = 1; r <= 4; r++) b.Set("A" + r, (double)r);
                b.Run(new ExcelSettings { Kind = ExcelKind.Filter, FilterField = 1, Criteria = ">2" }, "A1:A4");
                t.Eq("sheet filtered", b.Eval("SUBTOTAL(103,A2:A4)"), 2.0);
                b.Run(new ExcelSettings { Kind = ExcelKind.Filter, ClearFilter = true }, "");
                t.Eq("sheet filter cleared", b.Eval("SUBTOTAL(103,A2:A4)"), 3.0);

                // In workbook / In sheet: one action elsewhere, the context in use does not move
                b.NewWorkbook();
                t.Eq("the second workbook is in use", b.Info(ExcelInfoKind.WorkbookName), "Book2");
                b.Run(new ExcelSettings { Kind = ExcelKind.SetCell, UseWorkbook = "^" + firstName + "$", UseSheet = "Plain" }, "D1", "hello");
                t.Eq("written in the other workbook and sheet", b.Get("D1", "Book1", "Plain"), "hello");
                t.Eq("the workbook in use is untouched", b.Get("D1", kind: ExcelGetKind.IsEmpty), true);
                t.Eq("the workbook in use is still the second", b.Info(ExcelInfoKind.WorkbookName), "Book2");
                t.Eq("information about another workbook", b.Info(ExcelInfoKind.WorkbookName, "Book1"), "Book1");
                t.Throws("a workbook that is not open names the open ones", () => b.Get("A1", "Nope"), "open: ");
                t.Throws("a sheet that is not there names the sheets", () => b.Get("A1", "Book1", "zzz"), "sheets: ");

                // Select with an override must not change the context in use, also when no Workbook action pins it
                b.Session.Reset();
                string before = (string)b.Info(ExcelInfoKind.SheetName);
                b.Session.Reset();
                b.Run(new ExcelSettings { Kind = ExcelKind.CreateSheet, SheetName = "Other", SelectNew = false });
                b.Session.Reset();
                b.Run(new ExcelSettings { Kind = ExcelKind.Select, UseSheet = "Other" }, "B2");
                t.Eq("Select elsewhere keeps the sheet in use", b.Info(ExcelInfoKind.SheetName), before);
                b.Session.Reset();
                b.Run(new ExcelSettings { Kind = ExcelKind.Select, UseWorkbook = "Book1", UseSheet = "Plain" }, "B2");
                t.Eq("Select elsewhere keeps the workbook in use", b.Info(ExcelInfoKind.WorkbookName), "Book2");
            }
        }

        [Suite("excel.copy-and-paste", NeedsExcel = true)]
        public static void CopyAndPaste(T t)
        {
            using (var b = new Book(t))
            {
                b.NewWorkbook();
                b.Set("A1", 1.0); b.Set("B1", 2.0); b.Set("C1", "=A1+B1", ExcelSetKind.Formula);
                b.Set("A2", 10.0); b.Set("B2", 20.0); b.Set("C2", "=A2+B2", ExcelSetKind.Formula);
                b.Run(new ExcelSettings { Kind = ExcelKind.Format, NumberFormat = "0.00", Bold = ExcelFlag.Yes }, "A1:C2");

                b.Run(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.Values }, "C1:C2", "E1");
                t.Eq("Values: the result", b.Get("E1"), 3.0);
                t.Eq("Values: no formula", b.Get("E1", kind: ExcelGetKind.Formula), "3");
                t.Eq("Values: the format stays General", b.Get("E1", kind: ExcelGetKind.NumberFormat), "General");

                b.Run(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.Formulas }, "C1:C2", "G5");
                t.Eq("Formulas: references shift like in a paste", b.Get("G5", kind: ExcelGetKind.Formula), "=E5+F5");
                t.Eq("Formulas: no format", b.Get("G5", kind: ExcelGetKind.NumberFormat), "General");

                b.Run(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.Formats }, "A1:C2", "A10");
                t.Eq("Formats: the format arrives", b.Get("A10", kind: ExcelGetKind.NumberFormat), "0.00");
                t.Eq("Formats: the value does not", b.Get("A10", kind: ExcelGetKind.IsEmpty), true);

                b.Run(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.All }, "A1:C2", "A20");
                t.Eq("All: value", b.Get("A20"), 1.0);
                t.Eq("All: formula", b.Get("C20", kind: ExcelGetKind.Formula), "=A20+B20");
                t.Eq("All: format", b.Get("A20", kind: ExcelGetKind.NumberFormat), "0.00");

                b.Run(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.Values, Transpose = true }, "A1:B2", "J1");
                t.Eq("Transpose: A2 lands at K1", b.Get("K1"), 10.0);
                t.Eq("Transpose: B1 lands at J2", b.Get("J2"), 2.0);

                b.Run(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.All }, "1:1", "A30");
                t.Eq("a whole row", b.Get("B30"), 2.0);
                b.Run(new ExcelSettings { Kind = ExcelKind.Copy, Paste = ExcelPasteKind.ColumnWidths }, "A:A", "N1");

                // cut and replace
                b.Run(new ExcelSettings { Kind = ExcelKind.CutCells }, "A20:C20", "A40");
                t.Eq("Cut: moved", b.Get("A40"), 1.0);
                t.Eq("Cut: the source is empty", b.Get("A20", kind: ExcelGetKind.IsEmpty), true);

                b.Set("A50", "apple pie"); b.Set("A51", "apple"); b.Set("A52", "Apple");
                b.Run(new ExcelSettings { Kind = ExcelKind.ReplaceText, ReplaceWith = "pear", MatchCase = true }, "A50:A52", "apple");
                t.Eq("Replace a part", b.Get("A50"), "pear pie");
                t.Eq("Replace: Match case leaves Apple", b.Get("A52"), "Apple");
                b.Run(new ExcelSettings { Kind = ExcelKind.ReplaceText, ReplaceWith = "X", WholeCell = true }, "", "pear");
                t.Eq("Replace a whole cell (the whole sheet)", b.Get("A51"), "X");
                b.Run(new ExcelSettings { Kind = ExcelKind.ReplaceText, ReplaceWith = "" }, "A50", " pie");
                t.Eq("Replace with nothing removes the text", b.Get("A50"), "pear");

                // hide and show rows and columns
                for (int r = 60; r <= 64; r++) b.Set("A" + r, (double)r);
                b.Run(new ExcelSettings { Kind = ExcelKind.HideCells, Target = ExcelTarget.Rows, Count = 2 }, "A61");
                t.Eq("hidden rows are not counted", b.Eval("SUBTOTAL(103,A60:A64)"), 3.0);
                b.Run(new ExcelSettings { Kind = ExcelKind.ShowCells, Target = ExcelTarget.Rows, Count = 2 }, "A61");
                t.Eq("shown again", b.Eval("SUBTOTAL(103,A60:A64)"), 5.0);
                b.Run(new ExcelSettings { Kind = ExcelKind.HideCells, Target = ExcelTarget.Columns, Count = 1 }, "P1");
                b.Run(new ExcelSettings { Kind = ExcelKind.ShowCells, Target = ExcelTarget.Columns, Count = 1 }, "P1");
            }
        }

        [Suite("excel.sheets", NeedsExcel = true)]
        public static void Sheets(T t)
        {
            using (var b = new Book(t))
            {
                b.NewWorkbook();
                b.Set("A1", 1.0); b.Set("B1", 2.0);

                b.Run(new ExcelSettings { Kind = ExcelKind.SheetCopy, Value = "Copy1", Position = ExcelSheetPosition.End });
                t.Eq("copy: count", SheetCount(b), 2);
                t.Eq("copy: at the end with its name", SheetNameAt(b, 2), "Copy1");
                t.Eq("copy: it is the sheet in use", b.Info(ExcelInfoKind.SheetName), "Copy1");
                t.Eq("copy: data came along", b.Get("B1", sheet: "^Copy1$"), 2.0);
                t.Throws("copy: a name that exists", () => b.Run(new ExcelSettings { Kind = ExcelKind.SheetCopy, Value = "copy1" }), "already");
                b.Run(new ExcelSettings { Kind = ExcelKind.SheetCopy, Value = "AtStart", Position = ExcelSheetPosition.Start, SelectNew = false });
                t.Eq("copy to the start", SheetNameAt(b, 1), "AtStart");
                t.Eq("Select it OFF keeps the current sheet", b.Info(ExcelInfoKind.SheetName), "Copy1");
                b.Run(new ExcelSettings { Kind = ExcelKind.SheetCopy, SheetName = "^Copy1$", Value = "AfterCur", Position = ExcelSheetPosition.AfterCurrent });
                t.Eq("copy after the current sheet", SheetNameAt(b, 4), "AfterCur");

                b.Run(new ExcelSettings { Kind = ExcelKind.SheetMove, SheetName = "^AtStart$", Position = ExcelSheetPosition.End });
                t.Eq("move to the end", SheetNameAt(b, SheetCount(b)), "AtStart");
                t.Throws("move next to itself", () => b.Run(new ExcelSettings { Kind = ExcelKind.SheetMove, Position = ExcelSheetPosition.BeforeCurrent }), "next to itself");

                // into another workbook
                b.NewWorkbook();
                b.Run(new ExcelSettings { Kind = ExcelKind.SheetCopy, UseWorkbook = "Book1", SheetName = "^Copy1$", DestWorkbook = "Book2", Value = "FromBook1", Position = ExcelSheetPosition.End });
                t.Eq("copy into another workbook", b.Get("B1", "Book2", "^FromBook1$"), 2.0);
                b.Run(new ExcelSettings { Kind = ExcelKind.SheetMove, UseWorkbook = "Book1", SheetName = "^AfterCur$", DestWorkbook = "Book2", Position = ExcelSheetPosition.Start, SelectNew = false });
                t.Eq("move into another workbook", b.Get("B1", "Book2", "^AfterCur$"), 2.0);
                t.True("it left the first workbook", SheetNameAt(b, SheetCount(b)) != "AfterCur");
                t.Eq("the workbook in use is still the second", b.Info(ExcelInfoKind.WorkbookName), "Book2");

                // a copy of cells with formatting between two workbooks of the same Excel
                b.Run(new ExcelSettings { Kind = ExcelKind.Format, NumberFormat = "0.00", UseWorkbook = "Book1", UseSheet = "^Sheet1$" }, "A1");
                b.Run(new ExcelSettings { Kind = ExcelKind.Copy, UseWorkbook = "Book1", UseSheet = "^Sheet1$", Paste = ExcelPasteKind.All, DestWorkbook = "Book2", DestSheet = "^FromBook1$" }, "A1:B2", "T1");
                t.Eq("Copy with formats across workbooks", b.Get("T1", "Book2", "^FromBook1$", ExcelGetKind.NumberFormat), "0.00");

                // visibility
                Action<ExcelVisibility> set = v => b.Run(new ExcelSettings { Kind = ExcelKind.SheetVisible, UseWorkbook = "Book1", SheetName = "^Copy1$", Visibility = v });
                set(ExcelVisibility.Hidden);
                t.Eq("hidden", SheetVisible(b, "Copy1"), 0);
                set(ExcelVisibility.VeryHidden);
                t.Eq("very hidden", SheetVisible(b, "Copy1"), 2);
                set(ExcelVisibility.Visible);
                t.Eq("visible again", SheetVisible(b, "Copy1"), -1);
                set(ExcelVisibility.Hidden);
                b.Run(new ExcelSettings { Kind = ExcelKind.SheetVisible, UseWorkbook = "Book1", SheetName = "^AtStart$", Visibility = ExcelVisibility.Hidden });
                t.Throws("the last visible sheet cannot be hidden", () => b.Run(new ExcelSettings { Kind = ExcelKind.SheetVisible, UseWorkbook = "Book1", SheetName = "^Sheet1$", Visibility = ExcelVisibility.Hidden }), "at least one visible");
            }
        }
    }
}
