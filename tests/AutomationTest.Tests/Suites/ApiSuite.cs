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
                else if (path == "/endless")
                {
                    // a very big answer: 64 KB pieces for up to 10 s, until the client goes away
                    response.StatusCode = 200;
                    response.SendChunked = true;
                    var piece = new byte[64 * 1024];
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        while (watch.ElapsedMilliseconds < 10000)
                        {
                            response.OutputStream.Write(piece, 0, piece.Length);
                            Thread.Sleep(5);
                        }
                        response.Close();
                    }
                    catch (Exception) { try { response.Abort(); } catch (Exception) { } }
                    return;
                }
                else if (path == "/trickle")
                {
                    // the headers at once, then the body slowly: 4 pieces over about 2 seconds (a big download on a slow line)
                    response.StatusCode = 200;
                    response.SendChunked = true;
                    response.ContentType = "application/octet-stream";
                    for (int i = 0; i < 4; i++)
                    {
                        byte[] piece = Encoding.ASCII.GetBytes("part" + i + ";");
                        response.OutputStream.Write(piece, 0, piece.Length);
                        response.OutputStream.Flush();
                        if (i < 3) Thread.Sleep(650);
                    }
                    response.Close();
                    return;
                }
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

        [Suite("api.answer-size")]
        public static void AnswerSize(T t)
        {
            t.Eq("an answer kept in a variable can be 50 MB", ApiClient.DefaultMaxAnswerBytes, 50L * 1024 * 1024);
            using (var server = new TestServer())
            {
                // /file answers "file-content" (12 bytes)
                var fits = ApiClient.Send(new ApiRequest { Url = server.Root + "/file", MaxAnswerBytes = 12 }, CancellationToken.None);
                t.Eq("an answer of exactly the limit is read", fits.Body, "file-content");
                t.False("it is not marked too big", fits.TooBig);
                // one byte more: not an error; the status is real, the text is not kept and the answer says so
                var over = ApiClient.Send(new ApiRequest { Url = server.Root + "/file", MaxAnswerBytes = 11 }, CancellationToken.None);
                t.True("one byte over the limit is not an error and keeps the status", over.Ok && over.Status == 200);
                t.True("it is marked too big, with no text", over.TooBig && over.Body == "");
                t.True("the headers are still there", over.Headers.Rows > 0);
                var gzipless = ApiClient.Send(new ApiRequest { Url = server.Root + "/trickle", MaxAnswerBytes = 10, TimeoutMs = 5000 }, CancellationToken.None);
                t.True("an answer without a length that grows past the limit is cut, not read to the end", gzipless.TooBig);
                var vars = new VariableStore();
                vars.Set("r", over);
                t.Eq("{r.TooBig}", Expression.Evaluate("{r.TooBig}", vars), true);
                t.Eq("{r.Status}", Expression.Evaluate("{r.Status}", vars), 200.0);
                t.Throws<ExpressionException>("{r.Json} says why there is nothing", () => Expression.Evaluate("{r.Json.a}", vars), "bigger than the limit");
                vars.Set("f", fits);
                t.Eq("{r.TooBig} is false for an answer that fits", Expression.Evaluate("{f.TooBig}", vars), false);
                t.Eq("ApiClient names a size in MB", ApiClient.SizeText(50L * 1024 * 1024), "50 MB");
                t.Eq("HEAD has no body to measure", ApiClient.Send(new ApiRequest { Url = server.Root + "/file", Method = "HEAD", MaxAnswerBytes = 1 }, CancellationToken.None).Status, 200);

                // Download writes to the file and has no limit
                string folder = Path.Combine(Path.GetTempPath(), "at_api_" + Guid.NewGuid().ToString("N"));
                try
                {
                    string saved = Path.Combine(folder, "big.bin");
                    var download = ApiClient.Send(new ApiRequest { Url = server.Root + "/file", SaveTo = saved, MaxAnswerBytes = 1 }, CancellationToken.None);
                    t.True("Download is not limited", download.Ok && File.ReadAllText(saved) == "file-content");

                    // a download that takes longer than Timeout is not cut: Timeout is the wait for the answer to begin
                    string slow = Path.Combine(folder, "slow.bin");
                    var trickled = ApiClient.Send(new ApiRequest { Url = server.Root + "/trickle", SaveTo = slow, TimeoutMs = 1000 }, CancellationToken.None);
                    t.True("a download of 2 s with a Timeout of 1 s finishes", trickled.Ok && File.ReadAllText(slow) == "part0;part1;part2;part3;");
                    // while reading into a variable the Timeout still covers the whole answer
                    t.Throws("a slow answer read into a variable still times out",
                        () => ApiClient.Send(new ApiRequest { Url = server.Root + "/trickle", TimeoutMs = 1000 }, CancellationToken.None), "no answer");
                    // and Stop still ends a download that is going on
                    using (var stop = new CancellationTokenSource(900))
                    {
                        bool stopped = false;
                        try { ApiClient.Send(new ApiRequest { Url = server.Root + "/trickle", SaveTo = Path.Combine(folder, "stopped.bin"), TimeoutMs = 60000 }, stop.Token); }
                        catch (OperationCanceledException) { stopped = true; }
                        t.True("Stop ends a download", stopped);
                    }
                    t.False("a stopped download leaves no file", File.Exists(Path.Combine(folder, "stopped.bin")) || File.Exists(Path.Combine(folder, "stopped.bin.part")));
                }
                finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

                var r = Run(null, Api(CommandKind.ApiGet, server.Root + "/json"), Calc("name", "{r.Json.data[0].name}"));
                t.Eq("an ordinary answer is read as before", Get(r, "name"), "Ann");

                // in a script, with the limit of Settings: over it the run goes on (even with Fail on error), and a note says so
                var messages = new List<string>();
                var runner = new ScriptRunner(new MouseController()) { MaxApiAnswerBytes = 5 };
                runner.Message += messages.Add;
                runner.Run(new List<ActionNode> { Api(CommandKind.ApiGet, server.Root + "/file", failOnError: true), Calc("after", "\"went on\"") }, null, CancellationToken.None);
                object after;
                runner.Variables.TryGet("after", out after);
                t.Eq("the run goes on after an answer over the limit (Fail on error ON)", after, "went on");
                t.True("a note says it was not kept", messages.Any(m => m.Contains("not kept") && m.Contains("5 bytes")));
            }
        }

        [Suite("api.answer-size-endless")]
        public static void AnswerSizeEndless(T t)
        {
            using (var server = new TestServer())
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var answer = ApiClient.Send(new ApiRequest { Url = server.Root + "/endless", MaxAnswerBytes = 1024, TimeoutMs = 30000 }, CancellationToken.None);
                long ms = watch.ElapsedMilliseconds;
                t.True("a 10 s answer over the limit is marked too big", answer.TooBig && answer.Status == 200);
                t.True("and the call returns at once instead of fetching the rest (" + ms + " ms)", ms < 2500);
                // the connection is not reused half read: the next call to the same server is fine
                t.Eq("the next call works", ApiClient.Send(new ApiRequest { Url = server.Root + "/text", TimeoutMs = 15000 }, CancellationToken.None).Body, "plain text");
            }
        }

        private static ActionNode Saving(CommandKind kind, string url, string path, bool expression = false, bool overwrite = false, string body = "", bool failOnError = false)
        {
            return ActionNode.NewCommand(new CommandSettings { Kind = kind, Value = url, Id = "r", Target = body, SaveAnswer = true, SavePath = path, SavePathIsExpression = expression, Overwrite = overwrite, FailOnError = failOnError, TimeMs = 10000 });
        }

        [Suite("api.download-toggle")]
        public static void DownloadToggle(T t)
        {
            string folder = Path.Combine(Path.GetTempPath(), "at_dl_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                using (var server = new TestServer())
                {
                    // Get with Download ON: the answer goes to the file, the variable says where; the limit does not apply
                    string one = Path.Combine(folder, "sub", "one.txt");
                    var runner = new ScriptRunner(new MouseController()) { MaxApiAnswerBytes = 1 };
                    runner.Run(new List<ActionNode> { Saving(CommandKind.ApiGet, server.Root + "/file", one), Calc("where", "{r.SavedTo}"), Calc("big", "{r.TooBig}") }, null, CancellationToken.None);
                    t.Eq("the answer is in the file (folders made, no limit)", File.Exists(one) ? File.ReadAllText(one) : "(no file)", "file-content");
                    object where, big;
                    runner.Variables.TryGet("where", out where);
                    runner.Variables.TryGet("big", out big);
                    t.Eq("{r.SavedTo} is the file", where, one);
                    t.Eq("a saved answer is not too big", big, "false");

                    // in a Loop (For), with an expression path: one file per pass
                    var loop = ActionNode.NewLoop(new LoopSettings { Mode = LoopType.For, ForId = "i", ForFrom = "1", ForTo = "3", ForStep = "1" });
                    loop.Children.Add(Saving(CommandKind.ApiGet, server.Root + "/text", "\"" + folder.Replace("\\", "\\\\") + "\\\\report_\" + {i} + \".txt\"", expression: true));
                    Run(null, loop);
                    t.True("a Loop saves report_1, report_2, report_3", Enumerable.Range(1, 3).All(i => File.Exists(Path.Combine(folder, "report_" + i + ".txt"))));
                    t.Eq("with the answer in each", File.ReadAllText(Path.Combine(folder, "report_2.txt")), "plain text");

                    // the same name again: an error without Overwrite, replaced with it
                    t.Throws("a file that exists without Overwrite", () => Run(null, Saving(CommandKind.ApiGet, server.Root + "/text", one)), "exists already");
                    Run(null, Saving(CommandKind.ApiGet, server.Root + "/text", one, overwrite: true));
                    t.Eq("replaced with Overwrite", File.ReadAllText(one), "plain text");

                    // Post with a body and Download ON: the body is sent, the answer saved
                    string posted = Path.Combine(folder, "posted.json");
                    Run(null, Saving(CommandKind.ApiPost, server.Root + "/echo", posted, body: "{\"n\":7}"));
                    string echo = File.Exists(posted) ? File.ReadAllText(posted) : "";
                    t.True("Post sends its body and saves the answer", echo.Contains("\"method\":\"POST\"") && echo.Contains("{\\\"n\\\":7}"));

                    // not 2xx: nothing saved, the error text is kept, an error only with Fail on error
                    string missing = Path.Combine(folder, "missing.txt");
                    var failed = Run(null, Saving(CommandKind.ApiGet, server.Root + "/status/404", missing), Calc("text", "{r.Body}"), Calc("status", "{r.Status}"), Calc("saved", "{r.SavedTo}"));
                    t.False("a 404 is not saved", File.Exists(missing) || File.Exists(missing + ".part"));
                    t.Eq("its text is kept", Get(failed, "text"), "nope");
                    t.Eq("its status is kept", Get(failed, "status"), "404");
                    t.Eq("and {r.SavedTo} is empty", Get(failed, "saved"), "");
                    t.Throws("a 404 with Fail on error is an error", () => Run(null, Saving(CommandKind.ApiGet, server.Root + "/status/404", missing, failOnError: true)), "answered 404");

                    // a path without a drive starts at the folder of the script
                    var inFolder = new ScriptRunner(new MouseController()) { ScriptFolder = folder };
                    inFolder.Run(new List<ActionNode> { Saving(CommandKind.ApiGet, server.Root + "/text", "relative\\r.txt") }, null, CancellationToken.None);
                    t.True("a relative Download Path is in the script's folder", File.Exists(Path.Combine(folder, "relative", "r.txt")));
                    t.Throws("Download ON without a path", () => Run(null, Saving(CommandKind.ApiGet, server.Root + "/text", "  ")), "enter the Download Path");

                    // Download OFF keeps the old behaviour, and the toggle means nothing for Head
                    var head = Run(null, ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiHead, Value = server.Root + "/text", Id = "r", SaveAnswer = true, SavePath = Path.Combine(folder, "head.txt"), TimeMs = 10000 }), Calc("status", "{r.Status}"));
                    t.Eq("Head with the toggle set still only asks", Get(head, "status"), "200");
                    t.False("and saves nothing", File.Exists(Path.Combine(folder, "head.txt")));
                }

                // the file keeps the toggle; an old file has it off
                var script = new AutomationScript();
                script.Actions.Add(Saving(CommandKind.ApiGet, "https://x.test/a", "\"r_\" + {i}", expression: true, overwrite: true));
                var back = ScriptSerializer.FromJson(ScriptSerializer.ToJson(script)).Actions[0].Command;
                t.True("saved and read back: Download", back.SaveAnswer && back.SavePathIsExpression && back.Overwrite);
                t.Eq("saved and read back: Download Path", back.SavePath, "\"r_\" + {i}");
                var plain = new AutomationScript();
                plain.Actions.Add(Api(CommandKind.ApiGet, "https://x.test/a"));
                string json = ScriptSerializer.ToJson(plain);
                t.False("Download OFF writes nothing new in the file", json.Contains("SaveAnswer") || json.Contains("SavePath"));
                var old = ScriptSerializer.FromJson(json).Actions[0].Command;
                t.True("a file without the fields: Download OFF and no path", !old.SaveAnswer && old.SavePath == "");

                // Start Selected asks for the variables of the path
                var all = new AutomationScript();
                var call = Saving(CommandKind.ApiGet, "https://x.test/a", "\"r_\" + {n}", expression: true);
                all.Actions.Add(call);
                t.Eq("the path expression reads its variables", string.Join(",", SelectionPlanner.Build(all, new[] { call }).Needs.Select(x => x.Name)), "n");
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
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
            // what the Download toggle shares with any file save: no half file, a folder is not a file
            string folder = Path.Combine(Path.GetTempPath(), "at_api_" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var server = new TestServer())
                {
                    string file = Path.Combine(folder, "sub", "out.txt");
                    var r = Run(null, Saving(CommandKind.ApiGet, server.Root + "/file", file), Calc("code", "{r.Status}"));
                    t.Eq("the file is saved (and its folder made)", File.ReadAllText(file), "file-content");
                    t.Eq("with a status", Get(r, "code"), "200");
                    t.False("no half-finished file is left", File.Exists(file + ".part"));
                    t.Throws("a folder instead of a file", () => Run(null, Saving(CommandKind.ApiGet, server.Root + "/file", folder)), "is a folder");
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


            // the Download toggle and its path go in and out of the editor; OFF keeps no path
            var saving = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiGet, Value = "https://x/f", SaveAnswer = true, SavePath = "\"C:\\\\r_\" + {i} + \".xlsx\"", SavePathIsExpression = true, Overwrite = true, TimeMs = 30000 })).Command;
            t.True("Download ON", saving.SaveAnswer);
            t.Eq("Download Path", saving.SavePath, "\"C:\\\\r_\" + {i} + \".xlsx\"");
            t.True("Expr. (file)", saving.SavePathIsExpression);
            t.True("Overwrite with Download", saving.Overwrite);
            var postSaving = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiPost, Value = "https://x/f", Target = "{}", SaveAnswer = true, SavePath = "C:/out.json", TimeMs = 30000 })).Command;
            t.True("Post keeps both its body and the path", postSaving.SaveAnswer && postSaving.Target == "{}" && postSaving.SavePath == "C:/out.json");
            var off = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiGet, Value = "https://x/f", SaveAnswer = false, SavePath = "C:/left-over.txt", Overwrite = true, TimeMs = 30000 })).Command;
            t.True("Download OFF keeps no path and no Overwrite", !off.SaveAnswer && off.SavePath == "" && !off.Overwrite);
            var head = e.Through(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiHead, Value = "https://x/f", SaveAnswer = true, SavePath = "C:/h.txt", TimeMs = 30000 })).Command;
            t.True("Head has no Download", !head.SaveAnswer && head.SavePath == "");
            t.Eq("a path with { } but Expr. off is refused", e.Validate(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiGet, Value = "https://x/f", SaveAnswer = true, SavePath = "C:/r_{i}.xlsx", TimeMs = 30000 })) != null, true);
            t.Eq("Download ON without a path is refused", e.Validate(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiGet, Value = "https://x/f", SaveAnswer = true, TimeMs = 30000 })) != null, true);
            t.Eq("a good path passes", e.Validate(ActionNode.NewCommand(new CommandSettings { Kind = CommandKind.ApiGet, Value = "https://x/f", SaveAnswer = true, SavePath = "\"C:/r_\" + {i}", SavePathIsExpression = true, TimeMs = 30000 })), null);

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
