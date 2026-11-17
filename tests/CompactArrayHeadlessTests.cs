using System;
using System.Diagnostics;
using System.Text;

namespace JsonRightClick
{
    // Pure parser/formatter tests. Never constructs controls, starts the app,
    // reads selected text, touches the clipboard, or changes user settings.
    static class CompactArrayHeadlessTests
    {
        static int checks;
        static void Check(bool ok, string message)
        { checks++; if (!ok) throw new Exception(message); }
        static void Equal(string actual, string expected, string message)
        { Check(actual == expected, message + "\nEXPECTED: " + expected + "\nACTUAL: " + actual); }
        static string Format(string text, FormatOptions options)
        {
            var result = JsonTools.Process(text, options);
            Check(result.Count == 1 && result.Error == null && result.Pieces.Count == 1 && result.Pieces[0].IsJson,
                "a whole JSON document stays one complete structured piece");
            return result.Pieces[0].Text;
        }
        static string Minify(string text)
        {
            var result = JsonFormatter.Run(text, 0, new FormatOptions { Minify = true }, 0);
            Check(result.Ok && result.End == text.Length, "comparison input is entirely valid strict JSON");
            return result.Text;
        }
        static string Compact(string text)
        { return Format(text, new FormatOptions { CompactArrays = true }); }
        static void CheckInlineArrays(string text)
        {
            bool quoted = false, escape = false; int arrays = 0;
            foreach (char c in text)
            {
                if (quoted)
                {
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') quoted = true;
                else if (c == '[') arrays++;
                else if (c == ']') arrays--;
                else if (arrays > 0) Check(!char.IsWhiteSpace(c), "structural whitespace never remains inside any []");
                Check(arrays >= 0, "closing array brackets stay balanced");
            }
            Check(!quoted && arrays == 0, "string and array boundaries remain balanced");
        }
        static void Examples()
        {
            Equal(Compact("{\"ip\":[\n\"aaa\"\n]}"), "{\r\n  \"ip\": [\"aaa\"]\r\n}", "user example: only [] becomes a single line");
            Equal(Compact("[\r\n \"aaa\",\r\n \"bbb\"\r\n]"), "[\"aaa\",\"bbb\"]", "root array uses one physical line too");
            Equal(Compact("{\"a\":[1,{\"b\":[2,3],\"c\":{\"x\":4}}],\"outside\":{\"x\":1,\"y\":[5]},\"empty\":[],\"end\":{}}"),
                "{\r\n  \"a\": [1,{\"b\":[2,3],\"c\":{\"x\":4}}],\r\n  \"outside\": {\r\n    \"x\": 1,\r\n    \"y\": [5]\r\n  },\r\n  \"empty\": [],\r\n  \"end\": {}\r\n}",
                "nested arrays/objects inline inside []; surrounding object indentation resumes after each array");
            Equal(Compact("[[],{},[[],[1,2]],null,true,false]"), "[[],{},[[],[1,2]],null,true,false]", "empty and nested containers keep all elements");
            string noArray = "{\"a\":1,\"object\":{\"x\":2},\"literal\":\"[not an array]\"}";
            Equal(Compact(noArray), Format(noArray, new FormatOptions()), "documents without arrays use the existing object formatter unchanged");
            string input = "{\"a\":[1,2],\"b\":{\"x\":3}}";
            string original = Format(input, new FormatOptions());
            Equal(Format(input, new FormatOptions()), original, "normal formatting still expands arrays by default");
            Check(original.Contains("[\r\n    1,\r\n    2\r\n  ]"), "compact mode is opt-in, not a global default change");
            Equal(Format(Compact(input), new FormatOptions()), original, "Format restores the ordinary multi-line array layout");
            Equal(Format(input, new FormatOptions { CompactArrays = true, Minify = true }), Minify(input), "full Minify still takes precedence over array-only layout");
        }
        static void StringsAndTypes()
        {
            string[] inputs = {
                "{\"values\":[\" space  inside \",\"[braces] {text}\",\"line\\nnext\",\"quote\\\"x\",\"path\\\\new\"],\"plain\":\"[\\nnot structural]\"}",
                "{\"types\":[-0,0,1e+99,-1.200,123456789012345678901234567890,true,false,null,\"001\",\"true\",\"中文 😀\",\"\\u005b\\u005d\"]}",
                "[\"\\\\\",\"\\\"\",\"\\t\",\"\\r\\n\",{\"[key]\":\"] literal [\"}]",
                "{\"nested\":\"{\\\"ip\\\":[\\\"aaa\\\"]}\",\"a\":[1,2]}"
            };
            foreach (string input in inputs)
            {
                string compact = Compact(input);
                Equal(Minify(compact), Minify(input), "all string contents, escape lexemes, scalar types and numeric precision are preserved");
                CheckInlineArrays(compact);
                Equal(Compact(compact), compact, "compact formatting is idempotent");
            }
        }
        static void LenientAndExpansion()
        {
            var options = new FormatOptions { CompactArrays = true, Lenient = true };
            var result = JsonTools.Process("{ip: ['aaa', 'bbb',], node: {ports: [5678,], active: True}}", options);
            Check(result.Count == 1 && result.Pieces.Count == 1 && result.Warning != null, "existing lenient repairs remain explicit");
            Equal(result.Pieces[0].Text, "{\r\n  \"ip\": [\"aaa\",\"bbb\"],\r\n  \"node\": {\r\n    \"ports\": [5678],\r\n    \"active\": true\r\n  }\r\n}", "single quotes, trailing commas, keys and scalar types work with array-only layout");
            result = JsonTools.Process("{ip:[192.0.2.1,198.51.100.2], /* synthetic */ rate: 4Mbps, next:{enabled: true,}}", options);
            Check(result.Count == 1 && result.Pieces.Count == 1 && result.Warning != null, "map string inference and comments still warn rather than silently changing interpretation");
            Check(result.Pieces[0].Text.Contains("\"ip\": [\"192.0.2.1\",\"198.51.100.2\"]") && result.Pieces[0].Text.Contains("\r\n    \"enabled\": true\r\n  }"), "lenient array exit restores surrounding object layout");
            string input = "{\"a\":[\"{\\\"x\\\":[1,2],\\\"object\\\":{\\\"k\\\":3}}\"],\"outside\":\"{\\\"x\\\":[4,5],\\\"object\\\":{\\\"k\\\":6}}\"}";
            result = JsonTools.Process(input, new FormatOptions { CompactArrays = true, Expand = true });
            Check(result.Count == 1 && result.Expanded == 2, "nested JSON strings still recursively expand when requested");
            string expanded = result.Pieces[0].Text;
            Check(expanded.Contains("\"a\": [{\"x\":[1,2],\"object\":{\"k\":3}}]"), "expanded objects inherit single-line layout when inside an array");
            Check(expanded.Contains("\"outside\": {\r\n    \"x\": [4,5],\r\n    \"object\": {\r\n      \"k\": 6\r\n    }\r\n  }"), "expanded object outside arrays retains multiline layout and compacts only its arrays");
            CheckInlineArrays(expanded);
            Equal(Minify(expanded), Format(input, new FormatOptions { Minify = true, Expand = true }), "array layout does not change expansion semantics");
            string quoted = "\"{\\\"ip\\\":[\\\"aaa\\\"]}\"";
            result = JsonTools.Process(quoted, new FormatOptions { CompactArrays = true });
            Check(result.Count == 1 && result.Warning != null, "quoted full selection decoding still preserves its existing warning");
            Equal(result.Pieces[0].Text, "{\r\n  \"ip\": [\"aaa\"]\r\n}", "quoted full JSON selections inherit the requested array layout");
        }
        static void LogsAndFailures()
        {
            string input = "prefix [INFO] {\"ip\":[\"aaa\",\"bbb\"]} middle {\"port\":[80,443]} suffix";
            var result = JsonTools.Process(input, new FormatOptions { CompactArrays = true });
            Check(result.Count == 2 && result.Error == null, "all valid JSON pieces in mixed log text are processed");
            string full = JsonTools.BuildView(result.Pieces, false).Text;
            Check(full.Contains("prefix [INFO]") && full.Contains("middle") && full.Contains("suffix"), "log text outside JSON is retained, never minified as if it were an array");
            Check(full.Contains("\"ip\": [\"aaa\",\"bbb\"]") && full.Contains("\"port\": [80,443]"), "each JSON piece compacts its own arrays");
            string only = JsonTools.BuildView(result.Pieces, true).Text;
            string merged = JsonTools.MergeOnlyJson(result.Pieces, only.Replace("\"aaa\"", "\"edited\""));
            Check(merged != null && merged.Contains("prefix [INFO]") && merged.Contains("suffix") && merged.Contains("[\"edited\",\"bbb\"]"), "JSON-only headers still roundtrip edits into full log contents");
            foreach (string invalid in new[] { "plain text", "[INFO]", "[1,,2]", "[1", "[\"unterminated]", "{\"ip\": [\"aaa\",]}", "{ip:[aaa]}" })
            {
                result = JsonTools.Process(invalid, new FormatOptions { CompactArrays = true });
                Check(result.Count == 0 && result.Error != null && JsonTools.BuildView(result.Pieces, false).Text == invalid,
                    "invalid/unsupported selections keep their entire original text, without invented values or broken escapes");
            }
            result = JsonTools.Process(null, new FormatOptions { CompactArrays = true });
            Check(result.Count == 0 && JsonTools.BuildView(result.Pieces, false).Text == "", "empty/null input stays safe");
            string partial = "{\"broken\": ???, \"valid\": {\"ip\":[\"aaa\"]}}";
            result = JsonTools.Process(partial, new FormatOptions { CompactArrays = true });
            Check(result.Count > 0 && result.Warning != null && result.Pieces.Exists(piece => !piece.IsJson && piece.Text.Contains("???")), "partial extraction still warns and preserves unparsed source text");
        }
        static string RandomValue(Random random, int depth)
        {
            if (depth <= 0 || random.Next(4) == 0)
            {
                string[] values = { "null", "true", "false", "-0", "1.25e+20", "12345678901234567890123456789", "\"keep  spaces\"", "\"[literal]\\n\\\"quote\\\"\"", "\"中文😀\"", "\"\\\\path\"" };
                return values[random.Next(values.Length)];
            }
            bool array = random.Next(2) == 0; int count = random.Next(5);
            var text = new StringBuilder(array ? "[" : "{");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) text.Append(',');
                if (!array) text.Append('"').Append("key_").Append(i).Append("\":");
                text.Append(RandomValue(random, depth - 1));
            }
            return text.Append(array ? ']' : '}').ToString();
        }
        static void StressAndRoundtrip()
        {
            var random = new Random(17112026);
            for (int i = 0; i < 350; i++)
            {
                string input = "{\"value\":" + RandomValue(random, 5) + ",\"outside\":{\"tail\":[1,2],\"keep\":true}}";
                string compact = Compact(input);
                CheckInlineArrays(compact);
                Equal(Minify(compact), Minify(input), "random mixed JSON retains identical values, order and lexemes");
                Equal(Compact(compact), compact, "random compact layout is stable on repeated clicks");
                Equal(Format(compact, new FormatOptions()), Format(input, new FormatOptions()), "ordinary Format restores the same canonical multiline document");
            }
            string deep = new string('[', 512) + "0" + new string(']', 512);
            Equal(Compact(deep), deep, "the supported 512-level nesting limit still works without extra post-processing passes");
            var text = new StringBuilder("{\"items\":[");
            for (int i = 0; i < 10000; i++) { if (i > 0) text.Append(','); text.Append("{\"id\":").Append(i).Append(",\"ports\":[80,443],\"enabled\":true}"); }
            text.Append("],\"outside\":{\"keep\":true}}");
            var watch = Stopwatch.StartNew();
            string large = Compact(text.ToString()); watch.Stop();
            Equal(Minify(large), Minify(text.ToString()), "large object arrays preserve all values, not an abbreviated folding placeholder");
            CheckInlineArrays(large);
            Check(large.Contains("],\r\n  \"outside\": {\r\n    \"keep\": true\r\n  }"), "large-array exit still restores multiline sibling objects");
            Console.WriteLine("  Formatted 10,000-element array: " + watch.ElapsedMilliseconds + " ms; " + large.Length + " characters. Not a UI scrolling benchmark.");
        }
        static string Smart(string input)
        { return Format(input, new FormatOptions { SmartArrays = true }); }
        static void SmartExamples()
        {
            string routes = "{\"router\":[{\"name\":\"global\",\"chains\":[\"line1\"],\"fallback\":\"direct\"},{\"name\":\"game.exe\",\"chains\":[\"line1\"],\"fallback\":\"direct\"}]}";
            Equal(Smart(routes), "{\r\n  \"router\": [\r\n    {\"name\": \"global\", \"chains\": [\"line1\"], \"fallback\": \"direct\"},\r\n    {\"name\": \"game.exe\", \"chains\": [\"line1\"], \"fallback\": \"direct\"}\r\n  ]\r\n}",
                "user example: object arrays have one readable object per line, short scalar arrays stay inline");
            Equal(Smart("{\"ip\":[\"aaa\",\"bbb\"],\"ports\":[80,443],\"outside\":{\"enabled\":true}}"),
                "{\r\n  \"ip\": [\"aaa\",\"bbb\"],\r\n  \"ports\": [80,443],\r\n  \"outside\": {\r\n    \"enabled\": true\r\n  }\r\n}", "ordinary small arrays keep the original compact behavior while surrounding objects stay multiline");
            Equal(Smart("[1,{\"x\":1},[2,3],null]"), "[\r\n  1,\r\n  {\"x\": 1},\r\n  [2,3],\r\n  null\r\n]", "mixed arrays split per element without inventing separators or omitting values");
            Equal(Smart("[[1,2],[3,4]]"), "[\r\n  [1,2],\r\n  [3,4]\r\n]", "matrix arrays remain easy to compare row by row");
            Equal(Smart("[{},[],{\"x\":{}}]"), "[\r\n  {},\r\n  [],\r\n  {\"x\": {}}\r\n]", "empty containers remain compact even when they are individual array items");
            Equal(Smart("{\"items\":[{\"name\":\"nested\",\"child\":{\"x\":1},\"ports\":[80,443]}]}"),
                "{\r\n  \"items\": [\r\n    {\r\n      \"name\": \"nested\",\r\n      \"child\": {\r\n        \"x\": 1\r\n      },\r\n      \"ports\": [80,443]\r\n    }\r\n  ]\r\n}", "objects with nested nonempty objects expand instead of becoming a dense unreadable row");
            string many = "[{\"a\":1,\"b\":2,\"c\":3,\"d\":4,\"e\":5,\"f\":6,\"g\":7}]";
            Check(Smart(many).Contains("{\r\n    \"a\": 1,"), "objects with more than six fields expand even if all values are short");
            string longValue = "[{\"description\":\"" + new string('x', 150) + "\",\"id\":1}]";
            Check(Smart(longValue).Contains("{\r\n    \"description\": "), "long objects expand; a long string is not truncated or rewritten");
            var longArray = new StringBuilder("[");
            for (int i = 0; i < 70; i++) { if (i > 0) longArray.Append(','); longArray.Append(i); }
            longArray.Append(']');
            Check(Smart(longArray.ToString()).StartsWith("[\r\n  0,\r\n  1,"), "long primitive arrays also split by item rather than creating an indefinitely long row");
            Equal(Smart("[]"), "[]", "empty root arrays stay compact");
            Equal(Smart("{}"), "{}", "empty root objects stay compact");
            Equal(Smart("[1,2,true,null]"), "[1,2,true,null]", "short primitive root arrays are not needlessly expanded");
            Equal(Format(routes, new FormatOptions { SmartArrays = true, Minify = true }), Minify(routes), "full Minify takes precedence over smart layout");
            Equal(Format(routes, new FormatOptions { SmartArrays = true, CompactArrays = true }), Compact(routes), "explicit all-arrays-one-line takes precedence over smart layout");
        }
        static void SmartCompatibility()
        {
            foreach (string input in new[] {
                "{\"items\":[{\"text\":\"braces [ ] { } : ,\\n\\\"quoted\\\"\\\\path\",\"n\":123456789012345678901234567890,\"f\":-1.200e+99}],\"unicode\":[\"中文😀\",\"\\u005b\\u005d\"]}",
                "{\"items\":[{\"same\":1,\"same\":2}],\"literal\":\"[{\\\"x\\\":1}]\"}",
                "[\"spaces   stay\",false,-0,null,{\"array\":[\"\\r\\n\",\"\\t\"]}]"
            })
            {
                string smart = Smart(input);
                Equal(Minify(smart), Minify(input), "smart layout preserves numeric lexemes, duplicate properties, order, strings, Unicode and all escapes");
                Equal(Smart(smart), smart, "smart layout is idempotent");
                Equal(Format(smart, new FormatOptions()), Format(input, new FormatOptions()), "expanded formatting is unchanged after smart layout");
            }
            var lenient = JsonTools.Process("{router:[{name: global, chains:[line1], fallback:direct}, {name:'game.exe', chains:['line1',], fallback:'direct',}], /* note */ ip:[192.0.2.1]}", new FormatOptions { Lenient = true, SmartArrays = true });
            Check(lenient.Count == 1 && lenient.Pieces.Count == 1 && lenient.Warning != null, "lenient map repairs and inference warnings still accompany smart layout");
            Check(lenient.Pieces[0].Text.Contains("{\"name\": \"global\", \"chains\": [\"line1\"], \"fallback\": \"direct\"}"), "lenient object arrays use the same readable per-item rows");
            var expanded = JsonTools.Process("{\"items\":[\"{\\\"name\\\":\\\"nested\\\",\\\"ports\\\":[80,443]}\"],\"outside\":\"{\\\"values\\\":[1,2]}\"}", new FormatOptions { SmartArrays = true, Expand = true });
            Check(expanded.Count == 1 && expanded.Expanded == 2, "nested JSON expansion runs before smart layout, including deeply encoded values");
            Check(expanded.Pieces[0].Text.Contains("[\r\n    {\"name\": \"nested\", \"ports\": [80,443]}\r\n  ]"), "expanded string objects become separate readable array rows");
            Check(expanded.Pieces[0].Text.Contains("\"outside\": {\r\n    \"values\": [1,2]\r\n  }"), "expanded objects outside arrays retain ordinary indentation");
            var quoted = JsonTools.Process("\"{\\\"a\\\":[{\\\"x\\\":1}]}\"", new FormatOptions { SmartArrays = true });
            Check(quoted.Count == 1 && quoted.Warning != null && quoted.Pieces[0].Text.Contains("[\r\n    {\"x\": 1}\r\n  ]"), "quoted full selections decode and use smart layout without dropping the existing warning");
            var logs = JsonTools.Process("prefix {\"a\":[{\"x\":1}]} middle {\"b\":[2,3]} suffix", new FormatOptions { SmartArrays = true });
            string full = JsonTools.BuildView(logs.Pieces, false).Text;
            Check(logs.Count == 2 && full.Contains("prefix") && full.Contains("suffix") && full.Contains("[\r\n    {\"x\": 1}"), "mixed logs preserve their non-JSON text in smart mode");
            string only = JsonTools.BuildView(logs.Pieces, true).Text;
            string merged = JsonTools.MergeOnlyJson(logs.Pieces, only.Replace("\"x\": 1", "\"x\": 9"));
            Check(merged != null && merged.Contains("prefix") && merged.Contains("suffix") && merged.Contains("\"x\": 9"), "JSON-only edits still merge through smart-formatted fragments");
            foreach (string invalid in new[] { "ordinary text", "[INFO]", "[1,,2]", "{\"x\":[\"broken]", "[1" })
            {
                var failed = JsonTools.Process(invalid, new FormatOptions { SmartArrays = true });
                Check(failed.Count == 0 && JsonTools.BuildView(failed.Pieces, false).Text == invalid, "invalid text is preserved and never sent to the smart layout renderer");
            }
        }
        static void SmartStress()
        {
            var random = new Random(29112026);
            for (int i = 0; i < 400; i++)
            {
                string input = "{\"value\":" + RandomValue(random, 5) + ",\"rows\":[{\"name\":\"global\",\"ports\":[80,443]}]}";
                string smart = Smart(input);
                Equal(Minify(smart), Minify(input), "random smart layout preserves every value and literal lexeme");
                Equal(Smart(smart), smart, "random smart layout is stable on repeated clicks");
                Equal(Compact(smart), Compact(input), "smart formatting can still switch to the legacy all-arrays-one-line mode");
            }
            string deep = new string('[', 512) + "0" + new string(']', 512);
            Equal(Minify(Smart(deep)), deep, "the existing nesting limit remains supported without DOM limits or repeated recursive reparsing");
            var inputRows = new StringBuilder("{\"items\":[");
            for (int i = 0; i < 10000; i++) { if (i > 0) inputRows.Append(','); inputRows.Append("{\"id\":").Append(i).Append(",\"ports\":[80,443],\"enabled\":true}"); }
            inputRows.Append("]}");
            var watch = Stopwatch.StartNew(); string output = Smart(inputRows.ToString()); watch.Stop();
            Equal(Minify(output), Minify(inputRows.ToString()), "large object arrays retain all 10,000 objects and exact values");
            Check(output.Split(new[] { "\r\n" }, StringSplitOptions.None).Length == 10004, "large simple object arrays use one object per row, not one row per property");
            Console.WriteLine("  Smart layout of 10,000 objects: " + watch.ElapsedMilliseconds + " ms; " + output.Length + " characters. Not a UI frame-rate benchmark.");
        }
        internal static int RunAll()
        {
            checks = 0;
            Examples(); StringsAndTypes(); LenientAndExpansion(); LogsAndFailures(); StressAndRoundtrip();
            SmartExamples(); SmartCompatibility(); SmartStress();
            Console.WriteLine("PASS compact and smart arrays: " + checks + " assertions; pure formatting, no windows, input or clipboard access.");
            return checks;
        }
        static int Main()
        {
            try { RunAll(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
