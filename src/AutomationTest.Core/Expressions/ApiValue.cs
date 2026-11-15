using System;
using System.Collections.Generic;

namespace AutomationTest.Core.Expressions
{
    /// <summary>
    /// What an API action stores in its variable: {r.Status} (a number), {r.Ok} (true for 200 to 299), {r.Reason}, {r.Body} (the text
    /// of the answer), {r.ContentType}, {r.Headers} (a table of names and values) and {r.Json} (the body read as JSON).
    /// </summary>
    public sealed class ApiValue
    {
        private JsonValue _json;
        private bool _jsonTried;

        public ApiValue(int status, string reason, string contentType, string body, IList<KeyValuePair<string, string>> headers)
        {
            Status = status;
            Reason = reason ?? "";
            ContentType = contentType ?? "";
            Body = body ?? "";
            var cells = new object[headers.Count, 2];
            for (int i = 0; i < headers.Count; i++) { cells[i, 0] = headers[i].Key; cells[i, 1] = headers[i].Value; }
            Headers = new TableValue(cells);
        }

        public int Status { get; }
        public string Reason { get; }
        public string ContentType { get; }
        public string Body { get; }
        public TableValue Headers { get; }
        public bool Ok { get { return Status >= 200 && Status <= 299; } }

        public object GetMember(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "status": return (double)Status;
                case "ok": return Ok;
                case "reason": return Reason;
                case "body":
                case "text": return Body;
                case "contenttype": return ContentType;
                case "headers": return Headers;
                case "json":
                    if (!_jsonTried) { _jsonTried = true; JsonValue.TryParse(Body, out _json); }
                    if (_json == null) throw new ExpressionException("The answer is not JSON (it is " + (Body.Length == 0 ? "empty" : "\"" + (Body.Length > 40 ? Body.Substring(0, 40) + "..." : Body).Replace("\n", " ") + "\"") + "); use .Body to read it as text.");
                    return _json.ToExpressionValue();
            }
            throw new ExpressionException("A response has no \"" + name + "\" (it has Status, Ok, Reason, Body, ContentType, Headers and Json).");
        }

        public override string ToString()
        {
            return Status + (Reason.Length > 0 ? " " + Reason : "");
        }
    }
}
