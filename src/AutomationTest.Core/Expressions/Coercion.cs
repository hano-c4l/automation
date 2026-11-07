using System;
using System.Globalization;
using AutomationTest.Core.Model;

namespace AutomationTest.Core.Expressions
{
    /// <summary>Converts a value to the type a Variable action declares.</summary>
    public static class Coercion
    {
        private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm" };

        /// <exception cref="ExpressionException">The value cannot be used as that type.</exception>
        public static object To(VariableType type, object value)
        {
            switch (type)
            {
                case VariableType.Number:
                    if (value is double) return value;
                    if (value is string n && double.TryParse(n.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
                    throw Fail(type, value);

                case VariableType.String:
                    return Format.Display(value);

                case VariableType.Boolean:
                    if (value is bool) return value;
                    if (value is string b && bool.TryParse(b.Trim(), out bool parsed)) return parsed;
                    throw Fail(type, value);

                case VariableType.Date:
                    return ToDateTime(type, value).Date;

                case VariableType.DateTime:
                    return ToDateTime(type, value);

                case VariableType.Position:
                    if (value is PointValue) return value;
                    if (value is ImageValue image && image.Found) return image.Position;
                    throw Fail(type, value);

                case VariableType.Range:
                    if (value is RangeValue) return value;
                    throw Fail(type, value);
            }
            throw Fail(type, value);
        }

        private static DateTime ToDateTime(VariableType type, object value)
        {
            if (value is DateTime dt) return dt;
            if (value is string s && DateTime.TryParseExact(s.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return parsed;
            throw Fail(type, value);
        }

        private static ExpressionException Fail(VariableType type, object value)
        {
            return new ExpressionException("Cannot use " + Format.TypeName(value) + (value is string ? " \"" + value + "\"" : "") + " as a " + type + " value.");
        }
    }
}
