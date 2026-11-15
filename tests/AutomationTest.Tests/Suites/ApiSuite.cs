using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using AutomationTest.Core.Expressions;
using AutomationTest.Core.Input;
using AutomationTest.Core.Model;
using AutomationTest.Core.Runner;

namespace AutomationTest.Tests.Suites
{
    /// <summary>The API actions against a small web server of the test's own (on this computer only), and JSON in expressions.</summary>
    internal static class ApiSuite
    {
        /// <summary>A web server on 127.0.0.1 with a few addresses, closed at the end of the test.</summary>
        private sealed class TestServer : IDisposable
        {
            private readonly HttpListener _listener = new HttpListener();
            private readonly Thread _thread;
            public readonly string Root;
            public readonly List<string> Seen = new List<string>();

            public TestServer()
            {
                var random = new Random();
                for (int attempt = 0; ; attempt++)
                {
                    int port = random.Next(20000, 60000);
                    var candidate = new HttpListener();
                    candidate.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    try { candidate.Start(); _listener = candidate; Root = "http://127.0.0.1:" + port; break; }
                    catch (HttpListenerException) when (attempt < 20) { candidate.Close(); }
                }
                _thread = new Thread(Serve) { IsBackground = true };
                _thread.Start();
            }

            private void Serve()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = _listener.GetContext(); }
                    catch (Exception) { return; }
                    try { Handle(context); }
                    catch (Exception) { try { context.Response.Abort(); } catch (Exception) { } }
                }
            }

            private void Handle(HttpListenerContext context)
            {
                var request = context.Request;
                var response = context.Response;
                string body;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding)) body = reader.ReadToEnd();
                lock (Seen) Seen.Add(request.HttpMethod + " " + request.Url.PathAndQuery);
                string path = request.Url.AbsolutePath;
                string reply = "";
                string type = "text/plain";
                int status = 200;
                if (path == "/json")
                {
                    reply = "{\"data\":[{\"name\":\"Ann\",\"age\":30},{\"name\":\"Bob\",\"age\":25}],\"ok\":true,\"nothing\":null,\"first name\":\"Ann Lee\",\"pi\":3.5,\"text\":\"a\\u00e9\\n\\\"q\\\"\"}";
                    type = "application/json";
                }
                else if (path == "/echo")
                {
                    reply = "{\"method\":\"" + request.HttpMethod + "\",\"type\":\"" + request.ContentType + "\",\"body\":" + Quote(body) + ",\"token\":" + Quote(request.Headers["X-Token"] ?? "") + ",\"agent\":" + Quote(request.UserAgent ?? "") + "}";
                    type = "application/json";
                }
                else if (path == "/status/404") { status = 404; reply = "nope"; }
                else if (path == "/status/500") { status = 500; reply = "{\"error\":\"broken\"}"; type = "application/json"; }
                else if (path == "/slow") { Thread.Sleep(3000); reply = "late"; }
                else if (path == "/file") { reply = "file-content"; }
                else if (path == "/redirect") { response.StatusCode = 302; response.RedirectLocation = "/json"; response.Close(); return; }
                else if (path == "/text") { reply = "plain text"; }
                else { status = 404; reply = "unknown"; }

                byte[] bytes = Encoding.UTF8.GetBytes(reply);
                response.StatusCode = status;
                response.ContentType = type;
                response.Headers["X-Answer"] = "yes";
                if (request.HttpMethod != "HEAD") { response.ContentLength64 = bytes.Length; response.OutputStream.Write(bytes, 0, bytes.Length); }
                response.Close();
            }

            private static string Quote(string text)
            {
                return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
            }

            public void Dispose()
            {
                try { _listener.Stop(); _listener.Close(); } catch (Exception) { }
            }
        }

        private static ActionNode Api(CommandKind kind, string url, string id = "r", string headers = "", string body = "", bool failOnError = false, long timeoutMs = 10000)
        {
            return ActionNode.NewCommand(new CommandSettings { Kind = kind, Value = url, Id = id, Params = headers, Target = body, FailOnError = failOnError, TimeMs = timeoutMs });
        }

        private static ActionNode Calc(string id, string expression, VariableType type = VariableType.String)
        {
            return ActionNode.NewVariable(new VariableSettings { Id = id, Type = type, UseExpression = true, Expression = expression });
        }

        private static ScriptRunner Run(List<string> messages, params ActionNode[] actions)
        {
            var runner = new ScriptRunner(new MouseController());
            if (messages != null) runner.Message += messages.Add;
            runner.Run(new List<ActionNode>(actions), null, CancellationToken.None);
            return runner;
        }

        private static object Get(ScriptRunner runner, string name)
        {
            object value;
            return runner.Variables.TryGet(name, out value) ? value : "(not set)";
        }

        [Suite("api.json")]
        public static void Json(T t)
        {
            JsonValue json;
            t.True("an object", JsonValue.TryParse("{\"a\":1,\"b\":[true,null,\"x\"]}", out json));
            t.Eq("it reads back as compact JSON", json.ToString(), "{\"a\":1,\"b\":[true,null,\"x\"]}");
            t.False("not JSON", JsonValue.TryParse("hello", out json));
            t.False("empty", JsonValue.TryParse("  ", out json));
            t.False("cut short", JsonValue.TryParse("{\"a\":", out json));
            t.False("text after the JSON", JsonValue.TryParse("{} x", out json));
            t.False("a comma too many is not accepted", JsonValue.TryParse("[1,]", out json));
            t.True("a number alone", JsonValue.TryParse("-12.5e1", out json) && (double)json.ToExpressionValue() == -125.0);
            t.True("escapes", JsonValue.TryParse("\"a\\u0041\\n\\t\\\\\"", out json) && (string)json.ToExpressionValue() == "aA\n\t\\");
            t.True("a byte order mark is ignored", JsonValue.TryParse("\uFEFF[1]", out json));
            t.False("nested too deep", JsonValue.TryParse(new string('[', 300) + new string(']', 300), out json));

            // looked into by expressions
            JsonValue.TryParse("{\"data\":[{\"name\":\"Ann\",\"n\":2},{\"name\":\"Bob\"}],\"flag\":true,\"none\":null,\"first name\":\"Ann Lee\",\"Mixed\":\"m\"}", out json);
            var vars = new VariableStore();
            vars.Set("j", json);
            vars.Set("i", 1.0);
            t.Eq("a name of an item", Expression.Evaluate("{j.data[1].name}", vars), "Bob");
            t.Eq("an index from an expression", Expression.Evaluate("{j.data[{i} - 1].name}", vars), "Ann");
            t.Eq("a number", Expression.Evaluate("{j.data[0].n} + 1", vars), 3.0);
            t.Eq("true or false", Expression.Evaluate("{j.flag}", vars), true);
            t.Eq("null is an empty text", Expression.Evaluate("{j.none}", vars), "");
            t.Eq("a name with a space", Expression.Evaluate("{j[\"first name\"]}", vars), "Ann Lee");
            t.Eq("how many items", Expression.Evaluate("{j.data.Count}", vars), 2.0);
            t.Eq("letter case of a name does not matter", Expression.Evaluate("{j.mixed}", vars), "m");
            t.True("a part is JSON text when shown", Expression.EvaluateString("{j.data[0]}", vars).StartsWith("{\"name\":\"Ann\""));
            t.Throws<ExpressionException>("a name that is not there lists the ones that are", () => Expression.Evaluate("{j.missing}", vars), "has: data");
            t.Throws<ExpressionException>("an index too far says how many", () => Expression.Evaluate("{j.data[5]}", vars), "2 item(s)");
            t.Throws<ExpressionException>("a table index on JSON", () => Expression.Evaluate("{j.data[0, 1]}", vars), "reads a table");
            t.Throws<ExpressionException>("[ ] on a number", () => Expression.Evaluate("{i[0]}", vars), "reads a table or JSON");
            t.Eq("the names a JSON expression reads", string.Join(",", Expression.ReferencedVariables("{j.data[{i}].name}", false).OrderBy(n => n)), "i,j");
        }

        [Suite("api.calls")]
        public static void Calls(T t)
        {
            using (var server = new TestServer())
            {
                // GET and JSON
                var r = Run(null, Api(CommandKind.ApiGet, server.Root + "/json"),
                    Calc("name", "{r.Json.data[1].name}"), Calc("status", "{r.Status}"), Calc("ok", "{r.Ok}"), Calc("count", "{r.Json.data.Count}"), Calc("text", "{r.Json.text}"));
                t.Eq("status", Get(r, "status"), "200");
                t.Eq("Ok", Get(r, "ok"), "true");
                t.Eq("a name in the JSON", Get(r, "name"), "Bob");
                t.Eq("a count in the JSON", Get(r, "count"), "2");
                t.Eq("escapes in the JSON text", Get(r, "text"), "a\u00e9\n\"q\"");
                var answer = (ApiValue)Get(r, "r");
                t.True("the body is the text", answer.Body.StartsWith("{\"data\""));
                t.True("the content type", answer.ContentType.StartsWith("application/json"));
                t.True("the headers are a table with the one of the server", Enumerable.Range(1, answer.Headers.Rows).Any(i => (string)answer.Headers.Get(i, 1) == "X-Answer" && (string)answer.Headers.Get(i, 2) == "yes"));

                // not JSON
                r = Run(null, Api(CommandKind.ApiGet, server.Root + "/text"), Calc("body", "{r.Body}"));
                t.Eq("the body as text", Get(r, "body"), "plain text");
                t.Throws("Json of something that is not JSON says so", () => Run(null, Api(CommandKind.ApiGet, server.Root + "/text"), Calc("x", "{r.Json}")), "not JSON");
            }
        }

        [Suite("api.methods")]
        public static void Methods(T t)
        {
            using (var server = new TestServer())
            {
                var start = new Dictionary<string, object> { { "n", "42" } };
                var runner = new ScriptRunner(new MouseController());
                runner.Run(new List<ActionNode>
                {
                    Api(CommandKind.ApiPost, server.Root + "/echo", "p", "X-Token: = \"tok-\" + {n}", "{\"a\":1}"),
                    Api(CommandKind.ApiPut, server.Root + "/echo", "u", "", "text body"),
                    Api(CommandKind.ApiPatch, server.Root + "/echo", "a", "Content-Type: application/x-test", "x=1"),
                    Api(CommandKind.ApiDelete, server.Root + "/echo", "d"),
                    Calc("pm", "{p.Json.method}"), Calc("pt", "{p.Json.type}"), Calc("pb", "{p.Json.body}"), Calc("ptoken", "{p.Json.token}"), Calc("pagent", "{p.Json.agent}"),
                    Calc("um", "{u.Json.method}"), Calc("ut", "{u.Json.type}"),
                    Calc("am", "{a.Json.method}"), Calc("at", "{a.Json.type}"),
                    Calc("dm", "{d.Json.method}"),
                }, start, CancellationToken.None);
                t.Eq("POST", Get(runner, "pm"), "POST");
                t.True("JSON text goes as JSON", ((string)Get(runner, "pt")).StartsWith("application/json"));
                t.Eq("the body arrives", Get(runner, "pb"), "{\"a\":1}");
                t.Eq("a header from an expression", Get(runner, "ptoken"), "tok-42");
                t.Eq("the program names itself", Get(runner, "pagent"), "AutomationTest");
                t.Eq("PUT", Get(runner, "um"), "PUT");
                t.True("other text goes as plain text", ((string)Get(runner, "ut")).StartsWith("text/plain"));
                t.Eq("PATCH", Get(runner, "am"), "PATCH");
                t.Eq("a Content-Type header wins", Get(runner, "at"), "application/x-test");
                t.Eq("DELETE", Get(runner, "dm"), "DELETE");

                // HEAD: headers only
                var head = Run(null, Api(CommandKind.ApiHead, server.Root + "/json"), Calc("b", "{r.Body}"), Calc("s", "{r.Status}"));
                t.Eq("HEAD has no body", Get(head, "b"), "");
                t.Eq("but a status", Get(head, "s"), "200");

                // a redirect is followed
                var red = Run(null, Api(CommandKind.ApiGet, server.Root + "/redirect"), Calc("n", "{r.Json.data[0].name}"));
                t.Eq("a redirect is followed", Get(red, "n"), "Ann");
            }
        }

        [Suite("api.errors")]
        public static void Errors(T t)
        {
            using (var server = new TestServer())
            {
                // a status that is not 2xx is an answer
                var r = Run(null, Api(CommandKind.ApiGet, server.Root + "/status/404"), Calc("ok", "{r.Ok}"), Calc("code", "{r.Status}"), Calc("body", "{r.Body}"));
                t.Eq("404 is not Ok", Get(r, "ok"), "false");
                t.Eq("404 is the status", Get(r, "code"), "404");
                t.Eq("and the body is kept", Get(r, "body"), "nope");
                t.Eq("a 500 with JSON", Get(Run(null, Api(CommandKind.ApiGet, server.Root + "/status/500"), Calc("e", "{r.Json.error}")), "e"), "broken");

                // Fail on error makes it an error, naming the address without the query
                var messages = new List<string>();
                string failure = null;
                try { Run(messages, Api(CommandKind.ApiGet, server.Root + "/status/404?key=SECRET123", failOnError: true)); }
                catch (InvalidOperationException ex) { failure = ex.Message; }
                t.True("Fail on error: an error that names the status", failure != null && failure.Contains("404 Not Found"));
                t.False("the query (a key) is not in the error", failure != null && failure.Contains("SECRET123"));
                t.False("nor in the notes", messages.Any(m => m.Contains("SECRET123")));
                t.True("the notes say the address and the status", messages.Any(m => m.Contains("/status/404") && m.Contains("404")));
                t.True("the server really got the query", server.Seen.Any(s => s.Contains("key=SECRET123")));

                // no answer in time
                t.Throws("a slow server", () => Run(null, Api(CommandKind.ApiGet, server.Root + "/slow", timeoutMs: 700)), "no answer");

                // Stop goes through
                using (var cts = new CancellationTokenSource())
                {
                    cts.CancelAfter(300);
                    t.Throws<OperationCanceledException>("Stop ends the wait", () =>
                        new ScriptRunner(new MouseController()).Run(new List<ActionNode> { Api(CommandKind.ApiGet, server.Root + "/slow") }, null, cts.Token), "");
                }

                // addresses that are wrong
                t.Throws("not a web address", () => Run(null, Api(CommandKind.ApiGet, "ftp://example.com/x")), "not a web address");
                t.Throws("no address", () => Run(null, Api(CommandKind.ApiGet, "")), "enter the web address");
                t.Throws("nothing listens there", () => Run(null, Api(CommandKind.ApiGet, "http://127.0.0.1:9/never", timeoutMs: 5000)), "cannot reach");
                t.Throws("a bad header", () => Run(null, Api(CommandKind.ApiGet, server.Root + "/json", headers: "no colon here")), "Name: value");
                t.Throws("a bad ID", () => Run(null, Api(CommandKind.ApiGet, server.Root + "/json", id: "1x")), "not a valid ID");

                // no ID: the call is made and the answer is dropped
                var noId = Run(null, Api(CommandKind.ApiGet, server.Root + "/json", id: ""));
                t.True("without an ID nothing is stored", !noId.Variables.TryGet("r", out _));

                // inside a Try
                var tr = ActionNode.NewTry();
                tr.Children.Insert(0, Api(CommandKind.ApiGet, server.Root + "/status/500", failOnError: true));
                tr.Children[1].Catch.Id = "err";
                var caught = Run(null, tr);
                t.True("a Try catches a failed call", ((string)Get(caught, "err")).Contains("500"));
            }
        }

        [Suite("api.download")]
        public static void Download(T t)
        {
            string folder = Path.Combine(Path.GetTempPath(), "at_api_" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var server = new TestServer())
                {
                    string file = Path.Combine(folder, "sub", "out.txt");
                    var r = Run(null, Api(CommandKind.ApiDownload, server.Root + "/file", "r", "", file), Calc("code", "{r.Status}"));
                    t.Eq("the file is saved (and its folder made)", File.ReadAllText(file), "file-content");
                    t.Eq("with a status", Get(r, "code"), "200");
                    t.False("no half-finished file is left", File.Exists(file + ".part"));

                    t.Throws("a file that exists is not replaced", () => Run(null, Api(CommandKind.ApiDownload, server.Root + "/file", "r", "", file)), "exists already");
                    File.WriteAllText(file, "old");
                    var again = ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiDownload, Value = server.Root + "/file", Target = file, Overwrite = true, TimeMs = 10000 });
                    Run(null, again);
                    t.Eq("Overwrite replaces it", File.ReadAllText(file), "file-content");

                    string other = Path.Combine(folder, "nothing.txt");
                    t.Throws("a download that fails saves nothing", () => Run(null, Api(CommandKind.ApiDownload, server.Root + "/status/404", "r", "", other)), "nothing was saved");
                    t.False("the file is not there", File.Exists(other));
                    t.Throws("a folder instead of a file", () => Run(null, Api(CommandKind.ApiDownload, server.Root + "/file", "r", "", folder)), "is a folder");
                }
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
        }

        [Suite("api.in-the-editor")]
        public static void InTheEditor(T t)
        {
            var e = new EditorSuite.Editor();
            var get = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiGet, Value = "https://x.test/a?b=1", Id = "resp", Params = "Accept: application/json\r\nX-Key: = {k}", FailOnError = true, TimeMs = 5000, ValueIsExpression = false })).Command;
            t.Eq("GET kind", get.Kind, CommandKind.ApiGet);
            t.Eq("url", get.Value, "https://x.test/a?b=1");
            t.Eq("id", get.Id, "resp");
            t.Eq("headers", get.Params.Replace("\r\n", "\n"), "Accept: application/json\nX-Key: = {k}");
            t.True("fail on error", get.FailOnError);
            t.Eq("timeout", get.TimeMs, 5000L);

            var post = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiPost, Value = "\"https://x/\" + {id}", ValueIsExpression = true, Target = "{\"a\": 1}", TargetIsExpression = false, Id = "p" })).Command;
            t.True("an expression address", post.ValueIsExpression);
            t.Eq("the body keeps its braces and spaces", post.Target, "{\"a\": 1}");
            t.Eq("the timeout is 30 s when none was given", post.TimeMs, 30000L);

            var download = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiDownload, Value = "https://x/f.zip", Target = "C:/t/f.zip", Overwrite = true })).Command;
            t.Eq("download target", download.Target, "C:/t/f.zip");
            t.True("download overwrite", download.Overwrite);

            var other = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.FsCopy, Value = "a", Target = "b", FailOnError = true, Params = "X: 1" })).Command;
            t.Eq("other kinds drop the API fields", (other.FailOnError ? "y" : "n") + other.Params, "n");

            // what Start Selected needs and gives
            var script = new AutomationScript();
            var call = ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiGet, Value = "\"https://x/\" + {path}", ValueIsExpression = true, Params = "Authorization: = \"Bearer \" + {token}", Id = "resp" });
            var use = Calc("z", "{resp.Status}");
            script.Actions.Add(call);
            script.Actions.Add(use);
            var plan = SelectionPlanner.Build(script, new[] { call, use });
            t.Eq("the address and header expressions read variables, the ID is set", string.Join(",", plan.Needs.Select(n => n.Name).OrderBy(n => n)), "path,token");
        }
    }
}
