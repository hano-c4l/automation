using System;
using System.Collections.Generic;
using System.Globalization;

namespace Automation.Core.Expressions
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
                default: return value == null ? "nothing" : value.GetType().Name;
            }
        }
    }
}
