using System;
using System.Collections.Generic;
using System.Globalization;

namespace AutomationTest.Core.Expressions
{
    /// <summary>A point; X and Y are numbers so arithmetic like {a} + {1,1} works.</summary>
    public struct PointValue
    {
        public PointValue(double x, double y) { X = x; Y = y; }
        public double X;
        public double Y;
    }

    public struct SizeValue
    {
        public SizeValue(double width, double height) { Width = width; Height = height; }
        public double Width;
        public double Height;
    }

    public struct RangeValue
    {
        public RangeValue(double x, double y, double width, double height) { X = x; Y = y; Width = width; Height = height; }
        public double X;
        public double Y;
        public double Width;
        public double Height;
        public PointValue Position { get { return new PointValue(X, Y); } }
        public SizeValue Size { get { return new SizeValue(Width, Height); } }
    }

    /// <summary>
    /// What an Image action stores in its variable. Position and Size are only available when the picture was found
    /// (check Found first); reading them otherwise is an error.
    /// </summary>
    public sealed class ImageValue
    {
        public ImageValue(bool found, PointValue position, SizeValue size)
        {
            Found = found;
            Position = position;
            Size = size;
        }

        public bool Found { get; }
        public PointValue Position { get; }
        public SizeValue Size { get; }
    }

    /// <summary>
    /// A block of values (rows and columns), such as the cells of an Excel area. In expressions {t.Rows}, {t.Columns} and {t[row, column]}
    /// (both counted from 1) read it. It cannot be changed: a new table is made by reading the cells again.
    /// </summary>
    public sealed class TableValue
    {
        private readonly object[,] _cells; // typed values (double, string, bool, DateTime), 0-based

        public TableValue(object[,] cells)
        {
            _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public int Rows => _cells.GetLength(0);
        public int Columns => _cells.GetLength(1);

        /// <summary>The value at that row and column, counted from 1.</summary>
        public object Get(int row, int column)
        {
            if (row < 1 || row > Rows || column < 1 || column > Columns)
                throw new ExpressionException("The table has " + Rows + " row(s) and " + Columns + " column(s), so there is no row " + row + ", column " + column + ".");
            return _cells[row - 1, column - 1];
        }

        /// <summary>A copy of the values (0-based), for writing them somewhere.</summary>
        public object[,] ToArray()
        {
            return (object[,])_cells.Clone();
        }
    }

    /// <summary>What a Find action of Excel stores: whether the text was found and where (Row, Column, Address such as B7).</summary>
    public sealed class CellValue
    {
        public CellValue(bool found, int row, int column, string address)
        {
            Found = found;
            Row = row;
            Column = column;
            Address = address;
        }

        public bool Found { get; }
        public int Row { get; }
        public int Column { get; }
        public string Address { get; }
    }

    /// <summary>The variables of one run. Names are case-insensitive. Types: double, string, bool, DateTime, PointValue, RangeValue, ImageValue.</summary>
    public sealed class VariableStore
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public void Set(string name, object value)
        {
            _values[name] = value;
        }

        public bool TryGet(string name, out object value)
        {
            return _values.TryGetValue(name, out value);
        }

        public void Clear()
        {
            _values.Clear();
        }
    }

    public class ExpressionException : Exception
    {
        public ExpressionException(string message) : base(message) { }
    }

    internal static class Format
    {
        public static string Number(double value)
        {
            if (Math.Abs(value) < 1e15 && Math.Abs(value - Math.Round(value)) < 1e-9)
                return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
            return value.ToString("G15", CultureInfo.InvariantCulture);
        }

        /// <summary>The text form used when a value is joined into a string or typed by a Text action.</summary>
        public static string Display(object value)
        {
            switch (value)
            {
                case null: return "";
                case string s: return s;
                case double d: return Number(d);
                case bool b: return b ? "true" : "false";
                case DateTime dt: return dt.TimeOfDay == TimeSpan.Zero ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                case PointValue p: return Number(p.X) + "," + Number(p.Y);
                case SizeValue z: return Number(z.Width) + "," + Number(z.Height);
                case RangeValue r: return Number(r.X) + "," + Number(r.Y) + "," + Number(r.Width) + "," + Number(r.Height);
                case ImageValue i: return i.Found ? Number(i.Position.X) + "," + Number(i.Position.Y) : "not found";
                case CellValue c: return c.Found ? c.Address : "not found";
                case TableValue tv: return "table " + tv.Rows + "x" + tv.Columns;
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        public static string TypeName(object value)
        {
            switch (value)
            {
                case double _: return "Number";
                case string _: return "String";
                case bool _: return "Boolean";
                case DateTime _: return "Date/DateTime";
                case PointValue _: return "Position";
                case RangeValue _: return "Range";
                case SizeValue _: return "Size";
                case ImageValue _: return "Image";
                case CellValue _: return "Cell";
                case TableValue _: return "Table";
                default: return value == null ? "nothing" : value.GetType().Name;
            }
        }
    }
}
