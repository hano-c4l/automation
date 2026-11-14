using System.Runtime.Serialization;

namespace AutomationTest.Core.Model
{
    /// <summary>Settings of an Excel action. Which fields count depends on <see cref="Kind"/>.</summary>
    [DataContract]
    public sealed class ExcelSettings
    {
        public ExcelKind Kind { get; set; }

        /// <summary>Workbook: the file to open or switch to; empty creates a new workbook.</summary>
        [DataMember(Order = 2)] public string Path { get; set; } = "";

        /// <summary>Workbook: a regular expression for the name of a workbook that is open already (used when no Path is given; reaches unsaved workbooks such as Book1).</summary>
        [DataMember(Order = 3)] public string Name { get; set; } = "";

        /// <summary>Workbook: open the file read-only.</summary>
        [DataMember(Order = 4, EmitDefaultValue = false)] public bool ReadOnly { get; set; }

        /// <summary>Workbook: show Excel when the action has to start it.</summary>
        [DataMember(Order = 5)] public bool Visible { get; set; } = true;

        /// <summary>SelectSheet, CreateSheet, DeleteSheet: a regular expression for the name of the sheet (CreateSheet: the name of the new sheet).</summary>
        [DataMember(Order = 6)] public string SheetName { get; set; } = "";

        /// <summary>SelectSheet, DeleteSheet: position of the sheet (1 = first), among the matching ones when a name pattern is given; empty counts as 1 (DeleteSheet: empty = the current sheet).</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public int Index { get; set; }

        /// <summary>CreateSheet: where the new sheet goes.</summary>
        public ExcelSheetPosition Position { get; set; }

        /// <summary>CreateSheet: make the new sheet the current one.</summary>
        [DataMember(Order = 9)] public bool SelectNew { get; set; } = true;

        /// <summary>GetCell, SetCell: the cell (A1, $B$2, a name). Find: the area to search (empty = the whole sheet). GetInfo: a cell in the column or row to measure.</summary>
        [DataMember(Order = 10)] public string Cell { get; set; } = "";

        /// <summary>The Cell is an expression that gives the address, for example "B" + {row}.</summary>
        [DataMember(Order = 11, EmitDefaultValue = false)] public bool CellIsExpression { get; set; }

        /// <summary>GetCell: what to read.</summary>
        public ExcelGetKind Get { get; set; }

        /// <summary>GetInfo: what to read.</summary>
        public ExcelInfoKind Info { get; set; }

        /// <summary>SetCell: how the value is written.</summary>
        public ExcelSetKind As { get; set; }

        /// <summary>SetCell: the value; Find: what to look for; Evaluate: the formula.</summary>
        [DataMember(Order = 15)] public string Value { get; set; } = "";

        /// <summary>The Value is an expression (SetCell keeps its type: a number stays a number).</summary>
        [DataMember(Order = 16, EmitDefaultValue = false)] public bool ValueIsExpression { get; set; }

        /// <summary>Find: upper and lower case differ.</summary>
        [DataMember(Order = 17, EmitDefaultValue = false)] public bool MatchCase { get; set; }

        /// <summary>Find: the whole cell must equal the text (otherwise a part of it is enough).</summary>
        [DataMember(Order = 18, EmitDefaultValue = false)] public bool WholeCell { get; set; }

        /// <summary>Save: save under another name.</summary>
        [DataMember(Order = 19, EmitDefaultValue = false)] public bool SaveAs { get; set; }

        /// <summary>Save with SaveAs: the new file (xlsx, xlsm, xlsb, xls, csv or pdf, by its extension).</summary>
        [DataMember(Order = 20)] public string SavePath { get; set; } = "";

        /// <summary>CloseWorkbook, CloseExcel: save the changes first (otherwise they are dropped).</summary>
        [DataMember(Order = 21, EmitDefaultValue = false)] public bool SaveChanges { get; set; }

        /// <summary>GetCell, GetInfo, Find, Evaluate: the variable that receives the result.</summary>
        [DataMember(Order = 22)] public string Id { get; set; } = "";

        /// <summary>How long to wait for Excel (to start, to open a file, while it is busy), in milliseconds.</summary>
        [DataMember(Order = 23)] public long TimeMs { get; set; } = 30000;

        /// <summary>How long to wait after the action before the next one starts (milliseconds).</summary>
        [DataMember(Order = 24, EmitDefaultValue = false)] public long DelayMs { get; set; }

        /// <summary>InsertCells, DeleteCells: rows or columns.</summary>
        public ExcelTarget Target { get; set; }

        /// <summary>InsertCells, DeleteCells: how many rows or columns.</summary>
        [DataMember(Order = 30)] public int Count { get; set; } = 1;

        /// <summary>Clear: what to clear.</summary>
        public ExcelClearKind ClearWhat { get; set; }

        /// <summary>Format: the number format (for example 0.00, dd/mm/yyyy, @); empty leaves it.</summary>
        [DataMember(Order = 32)] public string NumberFormat { get; set; } = "";

        public ExcelFlag Bold { get; set; }
        public ExcelFlag Italic { get; set; }

        /// <summary>Format: colours as #RRGGBB; empty leaves them.</summary>
        [DataMember(Order = 35)] public string FontColor { get; set; } = "";
        [DataMember(Order = 36)] public string FillColor { get; set; } = "";

        /// <summary>Format: the width of the columns of the area: a number, or "auto" to fit the content; empty leaves it.</summary>
        [DataMember(Order = 37)] public string ColumnWidth { get; set; } = "";

        /// <summary>Sort: the column to sort by (letters, for example C).</summary>
        [DataMember(Order = 38)] public string SortBy { get; set; } = "";
        [DataMember(Order = 39, EmitDefaultValue = false)] public bool Descending { get; set; }

        /// <summary>Sort: the first row of the area is a header and stays on top.</summary>
        [DataMember(Order = 40, EmitDefaultValue = false)] public bool HasHeader { get; set; }

        /// <summary>Filter: the column of the area to filter, 1 = its first column.</summary>
        [DataMember(Order = 41)] public int FilterField { get; set; } = 1;

        /// <summary>Filter: the criterion, for example >5, =Hanoi, &lt;&gt;, *text*.</summary>
        [DataMember(Order = 42)] public string Criteria { get; set; } = "";

        /// <summary>Filter: remove the filter of the sheet instead.</summary>
        [DataMember(Order = 43, EmitDefaultValue = false)] public bool ClearFilter { get; set; }

        /// <summary>Copy: paste only the values.</summary>
        [DataMember(Order = 44, EmitDefaultValue = false)] public bool ValuesOnly { get; set; }

        /// <summary>RunMacro: the macro, for example Module1.Report or 'sales.xlsm'!Report.</summary>
        [DataMember(Order = 45)] public string Macro { get; set; } = "";

        /// <summary>Workbook: let the macros of the file run, as far as the Trust Center of Excel allows (otherwise they never run).</summary>
        [DataMember(Order = 46, EmitDefaultValue = false)] public bool AllowMacros { get; set; }

        [DataMember(Name = "Target", Order = 31)]
        private string TargetName
        {
            get { return Target.ToString(); }
            set { Target = ActionNode.Parse(value, ExcelTarget.Rows); }
        }

        [DataMember(Name = "ClearWhat", Order = 33)]
        private string ClearName
        {
            get { return ClearWhat.ToString(); }
            set { ClearWhat = ActionNode.Parse(value, ExcelClearKind.Contents); }
        }

        [DataMember(Name = "Bold", Order = 34)]
        private string BoldName
        {
            get { return Bold.ToString(); }
            set { Bold = ActionNode.Parse(value, ExcelFlag.Leave); }
        }

        [DataMember(Name = "Italic", Order = 47)]
        private string ItalicName
        {
            get { return Italic.ToString(); }
            set { Italic = ActionNode.Parse(value, ExcelFlag.Leave); }
        }

        [DataMember(Name = "Kind", Order = 1)]
        private string KindName
        {
            get { return Kind.ToString(); }
            set { Kind = ActionNode.Parse(value, ExcelKind.Workbook); }
        }

        [DataMember(Name = "Position", Order = 8)]
        private string PositionName
        {
            get { return Position.ToString(); }
            set { Position = ActionNode.Parse(value, ExcelSheetPosition.End); }
        }

        [DataMember(Name = "Get", Order = 12)]
        private string GetName
        {
            get { return Get.ToString(); }
            set { Get = ActionNode.Parse(value, ExcelGetKind.Value); }
        }

        [DataMember(Name = "Info", Order = 13)]
        private string InfoName
        {
            get { return Info.ToString(); }
            set { Info = ActionNode.Parse(value, ExcelInfoKind.LastRow); }
        }

        [DataMember(Name = "As", Order = 14)]
        private string AsName
        {
            get { return As.ToString(); }
            set { As = ActionNode.Parse(value, ExcelSetKind.Auto); }
        }

        [OnDeserializing]
        private void SetDefaults(StreamingContext context)
        {
            Path = "";
            Name = "";
            Visible = true;
            SheetName = "";
            SelectNew = true;
            Cell = "";
            Value = "";
            SavePath = "";
            Id = "";
            TimeMs = 30000;
            Count = 1;
            NumberFormat = "";
            FontColor = "";
            FillColor = "";
            ColumnWidth = "";
            SortBy = "";
            FilterField = 1;
            Criteria = "";
            Macro = "";
        }

        public ExcelSettings Clone()
        {
            return (ExcelSettings)MemberwiseClone();
        }

        internal void Normalize()
        {
            if (Path == null) Path = "";
            if (Name == null) Name = "";
            if (SheetName == null) SheetName = "";
            if (Cell == null) Cell = "";
            if (Value == null) Value = "";
            if (SavePath == null) SavePath = "";
            if (Id == null) Id = "";
            if (NumberFormat == null) NumberFormat = "";
            if (FontColor == null) FontColor = "";
            if (FillColor == null) FillColor = "";
            if (ColumnWidth == null) ColumnWidth = "";
            if (SortBy == null) SortBy = "";
            if (Criteria == null) Criteria = "";
            if (Macro == null) Macro = "";
            if (Count < 1) Count = 1;
            if (FilterField < 1) FilterField = 1;
            if (Index < 0) Index = 0;
            if (TimeMs < 0) TimeMs = 0;
            if (DelayMs < 0) DelayMs = 0;
        }
    }
}
