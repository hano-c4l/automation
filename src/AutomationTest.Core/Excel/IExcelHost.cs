using System;
using System.Collections.Generic;
using System.Threading;
using AutomationTest.Core.Model;

namespace AutomationTest.Core.Excel
{
    /// <summary>Where Find found the text.</summary>
    public sealed class CellMatch
    {
        public CellMatch(int row, int column, string address)
        {
            Row = row;
            Column = column;
            Address = address;
        }

        public int Row { get; }
        public int Column { get; }
        /// <summary>For example "B7" (no dollar signs).</summary>
        public string Address { get; }
    }

    /// <summary>
    /// What the Excel actions need from Excel. The real one (COM, in the program) drives Microsoft Excel; a fake one stands in for it
    /// in tests. Every method fails with an <see cref="InvalidOperationException"/> that says what went wrong (Excel busy, a missing
    /// sheet...), so a message can be shown as it is.
    /// </summary>
    public interface IExcelHost : IDisposable
    {
        /// <summary>How long a call may wait for Excel while it is busy (a cell being edited, a dialog open) before it fails; set before each action.</summary>
        int BusyTimeoutMs { get; set; }

        /// <summary>Lets the Stop key interrupt a wait for Excel; set before each action.</summary>
        CancellationToken Token { get; set; }

        /// <summary>
        /// Switches to the workbook of that file when it is open in Excel (in any Excel window), otherwise opens it. An empty path
        /// makes a new workbook; with an empty path and a name pattern an open workbook with a matching name is switched to.
        /// Starts Excel first when it is not running.
        /// </summary>
        IExcelWorkbook OpenWorkbook(string path, string namePattern, bool readOnly, bool visible, int timeoutMs, CancellationToken token);

        /// <summary>The workbook that is active in Excel now; null when Excel is not running or has no workbook.</summary>
        IExcelWorkbook ActiveWorkbook(int timeoutMs, CancellationToken token);

        /// <summary>Quits the Excel that holds the workbook (null = the one that is active); true when something was closed.</summary>
        bool CloseExcel(IExcelWorkbook workbook, bool saveChanges, int timeoutMs, CancellationToken token);

        /// <summary>The cell or area that is selected in the active workbook (null when there is none).</summary>
        bool TryGetSelection(out string sheetName, out string address);

        /// <summary>Called when a run ends: quits the Excel instances the program started hidden, and lets go of everything else.</summary>
        void EndRun();
    }

    public interface IExcelWorkbook
    {
        string Name { get; }
        /// <summary>The path of the file; the name when the workbook has not been saved yet.</summary>
        string FullPath { get; }
        bool HasFile { get; }
        IReadOnlyList<string> SheetNames { get; }
        IExcelSheet ActiveSheet { get; }
        /// <summary>The sheet at that position, 1 = first.</summary>
        IExcelSheet Sheet(int index);
        IExcelSheet AddSheet(string name, ExcelSheetPosition position, IExcelSheet current, bool select);
        void DeleteSheet(int index);
        void Activate();
        void Save();
        void SaveAs(string path);
        void Close(bool save);
    }

    public interface IExcelSheet
    {
        string Name { get; }
        /// <summary>Position in the workbook, 1 = first.</summary>
        int Index { get; }
        void Activate();

        /// <summary>The value, typed (double, string, bool, DateTime; an error cell as its text such as #N/A; an empty cell as null).</summary>
        object GetValue(string address);
        /// <summary>The text of the cell as Excel shows it (also when the column is too narrow to show it).</summary>
        string GetText(string address);
        string GetFormula(string address);
        string GetNumberFormat(string address);
        bool IsEmpty(string address);

        /// <summary>Writes a typed value (a string is read like typed into the cell: "5" is a number).</summary>
        void SetValue(string address, object value);
        /// <summary>Writes text that stays text ("007" stays 007).</summary>
        void SetText(string address, string text);
        void SetFormula(string address, string formula);

        /// <param name="area">Where to look; empty = the whole sheet.</param>
        CellMatch Find(string area, string what, bool matchCase, bool wholeCell);
        /// <summary>The last row that has something in the column of the cell (0 when the column is empty).</summary>
        int LastRow(string column);
        /// <summary>The last column that has something in the row of the cell (0 when the row is empty).</summary>
        int LastColumn(string row);
        /// <summary>For example "A1:D20"; empty when the sheet is empty.</summary>
        string UsedRange();
        /// <summary>Calculates an Excel formula such as SUM(A1:A10); same value types as GetValue.</summary>
        object Evaluate(string formula);
    }
}
