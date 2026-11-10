using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JsonRightClick
{
    /// <summary>文档片段：JSON 段或普通文本段（日志前缀等）</summary>
    public sealed class Piece
    {
        public bool IsJson;
        public string Text;
    }

    /// <summary>解析结果：把原文拆成 JSON 段和普通文本段，保持原顺序</summary>
    public sealed class ParseResult
    {
        public List<Piece> Pieces = new List<Piece>();
        public int Count;       // JSON 段数量
        public int Expanded;    // 展开的嵌套 JSON 字符串数量
        public string Error;    // 一段 JSON 都没找到时的原因
        public string Warning;  // 宽松解析 / 只提取了部分片段等提示
    }

    /// <summary>用于显示的文本 + JSON 段位置（着色用）</summary>
    public sealed class ViewText
    {
        public string Text = "";
        public List<int[]> JsonRanges = new List<int[]>(); // [start, length]
    }

    public sealed class FormatOptions
    {
        public bool Minify;   // 压缩为一行
        public bool Expand;   // 把值为 JSON 的字符串展开
        public bool Lenient;  // 严格解析失败时，尝试宽松规则（单引号、末尾逗号、True/None、注释等）
    }

    sealed class JsonFail : Exception
    {
        public readonly int Pos;
        public JsonFail(string msg, int pos) : base(msg) { Pos = pos; }
    }

    /// <summary>
    /// JSON 解析 + 格式化。保留原始键顺序、数字和字符串原文。
    /// 宽松模式下会把非标准写法转换成标准 JSON，并记录做了哪些修正。
    /// </summary>
    sealed class JsonFormatter
    {
        const int MaxDepth = 512;
        readonly string s;
        readonly bool minify, expand, lenient;
        readonly StringBuilder sb = new StringBuilder();
        readonly HashSet<string> fixes;
        int p, depth, expanded;

        JsonFormatter(string s, int start, FormatOptions o, bool lenient, int depth, HashSet<string> fixes)
        {
            this.s = s; p = start; minify = o.Minify; expand = o.Expand; this.lenient = lenient; this.depth = depth; this.fixes = fixes;
        }

        public sealed class Result
        {
            public bool Ok;
            public int End, ErrorPos, Expanded;
            public string Text, Error;
            public HashSet<string> Fixes = new HashSet<string>();
        }

        /// <summary>从 start 开始解析一个 JSON 值。先严格解析，失败且允许时再用宽松规则重试。</summary>
        public static Result Run(string s, int start, FormatOptions o, int depth)
        {
            var r = Attempt(s, start, o, false, depth);
            if (r.Ok || !o.Lenient) return r;
            var r2 = Attempt(s, start, o, true, depth);
            return r2.Ok ? r2 : r;
        }

        static Result Attempt(string s, int start, FormatOptions o, bool lenient, int depth)
        {
            var res = new Result();
            var f = new JsonFormatter(s, start, o, lenient, depth, res.Fixes);
            try
            {
                f.Value();
                res.Ok = true; res.End = f.p; res.Text = f.sb.ToString(); res.Expanded = f.expanded;
            }
            catch (JsonFail e) { res.Error = e.Message; res.ErrorPos = e.Pos; }
            return res;
        }

        void Fail(string m) { throw new JsonFail(m, p); }
        void Fix(string what) { fixes.Add(what); }

        void Ws()
        {
            while (p < s.Length)
            {
                char c = s[p];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { p++; continue; }
                if (!lenient) return;
                if (c == '\u00A0' || c == '\u3000' || c == '\uFEFF') { p++; Fix(L.T("全角/特殊空白")); continue; }
                if (c == '/' && p + 1 < s.Length && s[p + 1] == '/')
                {
                    while (p < s.Length && s[p] != '\n') p++;
                    Fix(L.T("注释")); continue;
                }
                if (c == '/' && p + 1 < s.Length && s[p + 1] == '*')
                {
                    int e = s.IndexOf("*/", p + 2, StringComparison.Ordinal);
                    if (e < 0) Fail(L.T("注释缺少 */"));
                    p = e + 2; Fix(L.T("注释")); continue;
                }
                return;
            }
        }

        void NewLine()
        {
            if (minify) return;
            sb.Append("\r\n").Append(' ', depth * 2);
        }

        void Value()
        {
            Ws();
            if (p >= s.Length) Fail(L.T("意外的结尾"));
            char c = s[p];
            if (c == '{') Obj();
            else if (c == '[') Arr();
            else if (c == '"' || (lenient && c == '\'')) Str(false);
            else if (c == '-' || (c >= '0' && c <= '9') || (lenient && c == '+')) Num();
            else if (Lit("true", "true") || Lit("false", "false") || Lit("null", "null")) { }
            else if (lenient && (Lit("True", "true") || Lit("False", "false") || Lit("None", "null")
                                 || Lit("undefined", "null") || Lit("NULL", "null")))
                Fix("True / False / None");
            else Fail(L.F("非法字符 '{0}'", c));
        }

        bool Lit(string lit, string output)
        {
            if (p + lit.Length <= s.Length && string.CompareOrdinal(s, p, lit, 0, lit.Length) == 0
                && (p + lit.Length == s.Length || !IsIdent(s[p + lit.Length])))
            {
                sb.Append(output); p += lit.Length; return true;
            }
            return false;
        }

        static bool IsIdent(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '$'; }

        void Obj()
        {
            if (++depth > MaxDepth) Fail(L.T("嵌套过深"));
            p++; Ws();
            if (p < s.Length && s[p] == '}') { p++; depth--; sb.Append("{}"); return; }
            sb.Append('{');
            while (true)
            {
                NewLine(); Ws();
                if (p < s.Length && (s[p] == '"' || (lenient && s[p] == '\''))) Str(true);
                else if (lenient && p < s.Length && (char.IsLetter(s[p]) || s[p] == '_' || s[p] == '$')) Ident();
                else Fail(L.T("应为属性名（双引号字符串）"));
                Ws();
                if (p >= s.Length || s[p] != ':') Fail(L.T("应为 ':'"));
                p++; sb.Append(minify ? ":" : ": ");
                Value(); Ws();
                if (p >= s.Length) Fail(L.T("对象缺少 '}'"));
                if (s[p] == ',')
                {
                    p++;
                    if (lenient) { Ws(); if (p < s.Length && s[p] == '}') { p++; depth--; NewLine(); sb.Append('}'); Fix(L.T("末尾多余的逗号")); return; } }
                    sb.Append(',');
                    continue;
                }
                if (s[p] == '}') { p++; depth--; NewLine(); sb.Append('}'); return; }
                Fail(L.T("应为 ',' 或 '}'"));
            }
        }

        void Ident()
        {
            int st = p;
            while (p < s.Length && (IsIdent(s[p]) || s[p] == '-')) p++;
            sb.Append('"').Append(s, st, p - st).Append('"');
            Fix(L.T("属性名缺少引号"));
        }

        void Arr()
        {
            if (++depth > MaxDepth) Fail(L.T("嵌套过深"));
            p++; Ws();
            if (p < s.Length && s[p] == ']') { p++; depth--; sb.Append("[]"); return; }
            sb.Append('[');
            while (true)
            {
                NewLine();
                Value(); Ws();
                if (p >= s.Length) Fail(L.T("数组缺少 ']'"));
                if (s[p] == ',')
                {
                    p++;
                    if (lenient) { Ws(); if (p < s.Length && s[p] == ']') { p++; depth--; NewLine(); sb.Append(']'); Fix(L.T("末尾多余的逗号")); return; } }
                    sb.Append(',');
                    continue;
                }
                if (s[p] == ']') { p++; depth--; NewLine(); sb.Append(']'); return; }
                Fail(L.T("应为 ',' 或 ']'"));
            }
        }

        void Str(bool isKey)
        {
            char q = s[p];
            int start = p++;
            bool changed = q == '\'';
            if (changed) Fix(L.T("单引号字符串"));
            StringBuilder o = changed ? new StringBuilder().Append('"') : null;
            while (true)
            {
                if (p >= s.Length) Fail(L.T("字符串缺少结束引号"));
                char c = s[p];
                if (c == q) { p++; break; }
                if (c == '\\')
                {
                    if (p + 1 >= s.Length) Fail(L.T("字符串缺少结束引号"));
                    char e = s[p + 1];
                    if (e == 'u')
                    {
                        bool hexOk = p + 5 < s.Length;
                        for (int k = 2; hexOk && k <= 5; k++) if (!IsHex(s[p + k])) hexOk = false;
                        if (!hexOk) Fail(L.T("非法的 \\u 转义"));
                        if (o != null) o.Append(s, p, 6);
                        p += 6; continue;
                    }
                    if ("\"\\/bfnrt".IndexOf(e) >= 0) { if (o != null) o.Append('\\').Append(e); p += 2; continue; }
                    if (lenient && e == '\'')
                    {
                        if (o == null) o = new StringBuilder().Append(s, start, p - start);
                        o.Append('\''); p += 2; changed = true; Fix(L.T("非标准转义")); continue;
                    }
                    if (!lenient) Fail(L.F("非法的转义 \\{0}", e));
                    if (o == null) o = new StringBuilder().Append(s, start, p - start);
                    o.Append("\\\\").Append(e); p += 2; changed = true; Fix(L.T("非标准转义")); continue;
                }
                if (c < 0x20)
                {
                    if (!lenient) Fail(L.T("字符串中含有换行或控制字符"));
                    if (o == null) o = new StringBuilder().Append(s, start, p - start);
                    o.Append(c == '\n' ? "\\n" : c == '\r' ? "\\r" : c == '\t' ? "\\t" : "\\u" + ((int)c).ToString("x4"));
                    p++; changed = true; Fix(L.T("字符串中的换行")); continue;
                }
                if (q == '\'' && c == '"') { o.Append("\\\""); p++; continue; } // 单引号字符串里的双引号要转义
                if (o != null) o.Append(c);
                p++;
            }
            string text;
            if (o != null) { o.Append('"'); text = o.ToString(); }
            else text = s.Substring(start, p - start);
            if (!isKey && expand && TryExpand(text)) return;
            sb.Append(text);
        }

        /// <summary>字符串的内容本身是 JSON 时，展开成真正的对象 / 数组</summary>
        bool TryExpand(string quoted)
        {
            if (quoted.Length < 4) return false;
            string inner = JsonTools.Unescape(quoted, 1, quoted.Length - 2).Trim();
            if (inner.Length < 2 || (inner[0] != '{' && inner[0] != '[')) return false;
            var r = Run(inner, 0, new FormatOptions { Minify = minify, Expand = true, Lenient = lenient }, depth);
            if (!r.Ok || inner.Substring(r.End).Trim().Length > 0 || r.Text == "{}" || r.Text == "[]") return false;
            sb.Append(r.Text);
            expanded += 1 + r.Expanded;
            foreach (var f in r.Fixes) fixes.Add(f);
            return true;
        }

        static bool IsHex(char c) { return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'); }
        static bool Dig(char c) { return c >= '0' && c <= '9'; }

        void Num()
        {
            int start = p;
            if (s[p] == '+') { p++; Fix(L.T("数字前的 +")); start = p; }
            else if (s[p] == '-') p++;
            if (p >= s.Length || !Dig(s[p])) Fail(L.T("非法数字"));
            if (s[p] == '0') p++; else while (p < s.Length && Dig(s[p])) p++;
            if (p < s.Length && s[p] == '.')
            {
                p++;
                if (p >= s.Length || !Dig(s[p])) Fail(L.T("非法数字"));
                while (p < s.Length && Dig(s[p])) p++;
            }
            if (p < s.Length && (s[p] == 'e' || s[p] == 'E'))
            {
                p++;
                if (p < s.Length && (s[p] == '+' || s[p] == '-')) p++;
                if (p >= s.Length || !Dig(s[p])) Fail(L.T("非法数字"));
                while (p < s.Length && Dig(s[p])) p++;
            }
            if (lenient && p < s.Length && IsIdent(s[p])) Fail(L.T("非法数字"));
            sb.Append(s, start, p - start);
        }
    }

    public static class JsonTools
    {
        static readonly FormatOptions Strict = new FormatOptions();
        static readonly FormatOptions StrictMin = new FormatOptions { Minify = true };

        public static ParseResult Process(string text, bool minify)
        {
            return Process(text, new FormatOptions { Minify = minify });
        }

        /// <summary>
        /// 处理任意文本：整段 JSON 直接格式化；日志等混合文本则提取其中的 JSON 片段逐个格式化，
        /// 非 JSON 文字原样保留为普通片段。
        /// </summary>
        public static ParseResult Process(string text, FormatOptions o)
        {
            var r = new ParseResult();
            text = text ?? "";
            var segs = new List<int[]>();      // [start, length]
            var formatted = new List<string>();
            var fixes = new HashSet<string>();

            string bestErr = null; int bestErrPos = -1, bestErrStart = -1;
            int lead = 0;
            while (lead < text.Length && SelectionSpace(text[lead])) lead++;

            // 1) 整段就是一个 JSON 值
            if (lead < text.Length && (text[lead] == '{' || text[lead] == '['))
            {
                var res = JsonFormatter.Run(text, lead, o, 0);
                if (res.Ok && OnlySelectionSpace(text, res.End))
                {
                    r.Pieces.Add(new Piece { IsJson = true, Text = res.Text });
                    r.Count = 1; r.Expanded = res.Expanded;
                    Finish(r, res.Fixes);
                    return r;
                }
                if (!res.Ok) { bestErr = res.Error; bestErrPos = res.ErrorPos; bestErrStart = lead; }
            }

            // 选区可能来自调试器/日志中的字符串字面量。只在逐层解码后整个内容仍是
            // 一个完整对象/数组时才采用，不能把原文里的普通反斜杠或字符串字段全局替换。
            JsonFormatter.Result decoded;
            int layers;
            if (TryDecodeSelection(text, lead, o, out decoded, out layers))
            {
                r.Pieces.Add(new Piece { IsJson = true, Text = decoded.Text });
                r.Count = 1; r.Expanded = decoded.Expanded;
                Finish(r, decoded.Fixes);
                string note = L.F(L.T("已解码外层 JSON 字符串/转义（{0} 层）", "Decoded outer JSON string/escaping ({0} layers)"), layers);
                r.Warning = r.Warning == null ? note : note + L.T("；", "; ") + r.Warning;
                return r;
            }

            // 2) 混合文本：扫描所有 { / [ 开头的合法 JSON 片段（超大日志设时间上限，避免卡死）
            int i = 0;
            var budget = System.Diagnostics.Stopwatch.StartNew();
            bool stopped = false;
            while (i < text.Length)
            {
                if ((i & 1023) == 0 && budget.ElapsedMilliseconds > 6000) { stopped = true; break; }
                char c = text[i];
                if (c == '{' || c == '[')
                {
                    var res = JsonFormatter.Run(text, i, o, 0);
                    if (res.Ok)
                    {
                        if (IsMeaningful(text, i, res.End))
                        {
                            segs.Add(new[] { i, res.End - i }); formatted.Add(res.Text);
                            r.Expanded += res.Expanded;
                            foreach (var f in res.Fixes) fixes.Add(f);
                            i = res.End; continue;
                        }
                    }
                    else if (bestErr == null || res.ErrorPos - i > bestErrPos - bestErrStart)
                    {
                        bestErr = res.Error; bestErrPos = res.ErrorPos; bestErrStart = i;
                    }
                }
                i++;
            }

            if (segs.Count == 0)
            {
                r.Pieces.Add(new Piece { Text = text });
                r.Error = bestErr == null ? L.T("文本中没有找到 { 或 [ 开头的 JSON") : Where(text, bestErrPos) + bestErr;
                return r;
            }
            if (bestErrStart == lead && bestErr != null)
                r.Warning = L.F("整体不是合法 JSON（{0}{1}），仅格式化了其中合法片段", Where(text, bestErrPos), bestErr);
            if (stopped)
                r.Warning = (r.Warning == null ? "" : r.Warning + L.T("；")) + L.F("内容太多，只处理了前 {0}% 的文本", (int)(i * 100L / Math.Max(1, text.Length)));

            // 3) 组装片段：保证每段 JSON 独占行，且重复格式化不会不断增加空行
            r.Count = segs.Count;
            int last = 0;
            for (int k = 0; k < segs.Count; k++)
            {
                string before = text.Substring(last, segs[k][0] - last);
                if (last > 0 && before.Length > 0 && !StartsWithNewline(before)) before = "\r\n" + before;
                before = before.TrimEnd(' ', '\t');
                if (before.Length > 0 && !EndsWithNewline(before)) before += "\r\n";
                if (before.Length > 0) r.Pieces.Add(new Piece { Text = before });
                r.Pieces.Add(new Piece { IsJson = true, Text = formatted[k] });
                last = segs[k][0] + segs[k][1];
            }
            string after = text.Substring(last);
            if (after.Trim().Length > 0)
                r.Pieces.Add(new Piece { Text = StartsWithNewline(after) ? after : "\r\n" + after });
            else if (after.Length > 0)
                r.Pieces.Add(new Piece { Text = after });
            Finish(r, fixes);
            return r;
        }

        static bool SelectionSpace(char c) { return char.IsWhiteSpace(c) || c == '\uFEFF'; }
        static bool OnlySelectionSpace(string text, int start)
        {
            for (int i = start; i < text.Length; i++) if (!SelectionSpace(text[i])) return false;
            return true;
        }
        static bool TryDecodeSelection(string text, int lead, FormatOptions options, out JsonFormatter.Result result, out int layers)
        {
            result = null; layers = 0;
            if (lead >= text.Length) return false;
            int end = text.Length;
            while (end > lead && SelectionSpace(text[end - 1])) end--;
            string current = text.Substring(lead, end - lead);
            var literalOptions = new FormatOptions { Minify = true, Lenient = options.Lenient };
            for (int layer = 1; layer <= 4 && current.Length > 0; layer++)
            {
                char first = current[0];
                string literal;
                if (first == '"' || (first == '\'' && options.Lenient)) literal = current;
                else if (first == '{' || first == '[') literal = "\"" + current + "\"";
                else return false;
                // 转义层必须严格有效；仅单引号字面量按用户启用的宽松规则读取。
                var outer = JsonFormatter.Run(literal, 0, first == '\'' ? literalOptions : StrictMin, 0);
                if (!outer.Ok || !OnlySelectionSpace(literal, outer.End) || outer.Text.Length < 2 || outer.Text[0] != '"') return false;
                current = Unescape(outer.Text, 1, outer.Text.Length - 2);
                int start = 0, stop = current.Length;
                while (start < stop && SelectionSpace(current[start])) start++;
                while (stop > start && SelectionSpace(current[stop - 1])) stop--;
                current = current.Substring(start, stop - start);
                if (current.Length == 0) return false;
                if (current[0] == '{' || current[0] == '[')
                {
                    var inner = JsonFormatter.Run(current, 0, options, 0);
                    if (inner.Ok && OnlySelectionSpace(current, inner.End))
                    { result = inner; layers = layer; return true; }
                }
            }
            return false;
        }

        static void Finish(ParseResult r, HashSet<string> fixes)
        {
            if (fixes.Count == 0) return;
            var list = new List<string>(fixes);
            list.Sort(StringComparer.Ordinal);
            for (int k = 0; k < list.Count; k++) list[k] = L.T(list[k]);
            string w = L.F("已按宽松规则解析并转换为标准 JSON（{0}）", string.Join(L.T("、", ", "), list.ToArray()));
            r.Warning = r.Warning == null ? w : r.Warning + L.T("；") + w;
        }

        /// <summary>在任意文本里定位合法 JSON 段（不改动文本，只返回位置），用于重新着色 / 树形视图 / 打码</summary>
        public static List<int[]> FindJsonRanges(string text)
        {
            var list = new List<int[]>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{' || c == '[')
                {
                    var res = JsonFormatter.Run(text, i, StrictMin, 0);
                    if (res.Ok && (list.Count == 0 && text.Substring(0, i).Trim().Length == 0 || IsMeaningful(text, i, res.End)))
                    {
                        list.Add(new[] { i, res.End - i });
                        i = res.End;
                        continue;
                    }
                }
                i++;
            }
            return list;
        }

        /// <summary>严格解析并格式化一段 JSON（失败返回 null）</summary>
        public static string Pretty(string json)
        {
            int lead = 0;
            while (lead < json.Length && char.IsWhiteSpace(json[lead])) lead++;
            var r = JsonFormatter.Run(json, lead, Strict, 0);
            return r.Ok && json.Substring(r.End).Trim().Length == 0 ? r.Text : null;
        }

        /// <summary>解码 JSON 字符串内容（不含两侧引号）</summary>
        public static string Unescape(string s, int start, int len)
        {
            var b = new StringBuilder(len);
            int end = start + len;
            for (int i = start; i < end; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= end) { b.Append(c); continue; }
                char e = s[++i];
                switch (e)
                {
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u':
                        int v;
                        if (i + 4 < end && int.TryParse(s.Substring(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v))
                        { b.Append((char)v); i += 4; }
                        else b.Append("\\u");
                        break;
                    default: b.Append(e); break;
                }
            }
            return b.ToString();
        }

        // ---------- 显示 / 还原 ----------
        static readonly Regex HeaderLine = new Regex(@"^// ===== (?:JSON 片段|JSON fragment) (\d+) / (\d+) =====[ \t]*\r?$", RegexOptions.Multiline);

        static string Header(int n, int total) { return L.F("// ===== JSON 片段 {0} / {1} =====", n, total); }

        /// <summary>生成显示文本。onlyJson 时只显示 JSON 段，多段之间用分隔标题行隔开。</summary>
        public static ViewText BuildView(List<Piece> pieces, bool onlyJson)
        {
            var v = new ViewText();
            var sb = new StringBuilder();
            int total = 0;
            foreach (var p in pieces) if (p.IsJson) total++;
            int n = 0;
            foreach (var p in pieces)
            {
                if (onlyJson)
                {
                    if (!p.IsJson) continue;
                    n++;
                    if (total > 1)
                    {
                        if (sb.Length > 0) sb.Append("\r\n\r\n");
                        sb.Append(Header(n, total)).Append("\r\n");
                    }
                }
                else if (!p.IsJson) { sb.Append(p.Text); continue; }
                v.JsonRanges.Add(new[] { sb.Length, p.Text.Length });
                sb.Append(p.Text);
            }
            v.Text = sb.ToString();
            return v;
        }

        /// <summary>
        /// “只看 JSON”模式下编辑后的文本，合并回完整原文（非 JSON 内容保持不变）。
        /// 分隔标题行被删改导致无法对应时返回 null。
        /// </summary>
        public static string MergeOnlyJson(List<Piece> pieces, string edited)
        {
            int total = 0;
            foreach (var p in pieces) if (p.IsJson) total++;
            var parts = new List<string>();
            if (total == 1) parts.Add(edited.Trim());
            else
            {
                var ms = HeaderLine.Matches(edited);
                if (ms.Count != total) return null;
                for (int k = 0; k < ms.Count; k++)
                {
                    if (int.Parse(ms[k].Groups[1].Value) != k + 1) return null;
                    int s = ms[k].Index + ms[k].Length;
                    int e = k + 1 < ms.Count ? ms[k + 1].Index : edited.Length;
                    if (k == 0 && edited.Substring(0, ms[0].Index).Trim().Length > 0) return null;
                    parts.Add(edited.Substring(s, e - s).Trim());
                }
            }
            var sb = new StringBuilder();
            int idx = 0;
            foreach (var p in pieces) sb.Append(p.IsJson ? parts[idx++] : p.Text);
            return sb.ToString();
        }

        /// <summary>去掉分隔标题行（用于复制 / 无法合并时的兜底）</summary>
        public static string StripHeaders(string text)
        {
            return Regex.Replace(HeaderLine.Replace(text, ""), @"^\s+", "");
        }

        // 混合文本中过滤掉 [INFO]、[1]、{} 这类不是数据的片段
        static bool IsMeaningful(string text, int start, int end)
        {
            if (end - start <= 2) return false;
            if (text[start] == '{') return true;
            for (int k = start + 1; k < end - 1; k++)
            {
                char c = text[k];
                if (c == '"' || c == '\'' || c == '{' || c == '[') return true;
            }
            return false;
        }

        static bool StartsWithNewline(string s) { return s.Length > 0 && (s[0] == '\r' || s[0] == '\n'); }
        static bool EndsWithNewline(string s) { char c = s[s.Length - 1]; return c == '\n' || c == '\r'; }

        static string Where(string text, int pos)
        {
            if (pos < 0) return "";
            int line = 1, col = 1;
            for (int k = 0; k < pos && k < text.Length; k++)
            {
                if (text[k] == '\n') { line++; col = 1; } else if (text[k] != '\r') col++;
            }
            return L.F("第 {0} 行第 {1} 列：", line, col);
        }
    }

    // ===================== JSON 结构（树形视图 / 路径 / 打码用） =====================
    public enum JKind { Object, Array, String, Number, Literal }

    public sealed class JNode
    {
        public JKind Kind;
        public string Key;           // 对象成员的键（已解码）
        public int Index = -1;       // 数组元素下标
        public JNode Parent;
        public List<JNode> Children;
        public string Src;           // 所在文本
        public int Start, VStart, End; // Start=成员起点（含键）；VStart=值起点；End=值终点（不含）
        public string Label;         // 虚拟节点名称，如 "JWT Payload"
        public bool Virtual;         // 来自解码（JWT / 嵌套 JSON），不在编辑框原文中
        List<JNode> virt; bool virtDone;

        public int Count { get { return Children == null ? 0 : Children.Count; } }
        public string Raw { get { return Src.Substring(VStart, End - VStart); } }
        public string StringValue { get { return Kind == JKind.String ? JsonTools.Unescape(Src, VStart + 1, End - VStart - 2) : Raw; } }

        /// <summary>字符串值本身是 JWT 或 JSON 时，返回解码后的子结构（延迟计算）</summary>
        public List<JNode> VirtualChildren
        {
            get
            {
                if (virtDone) return virt;
                virtDone = true;
                if (Kind != JKind.String || End - VStart < 6) return null;
                string v = StringValue;
                var jwt = Jwt.Decode(v);
                var list = new List<JNode>();
                if (jwt != null)
                {
                    AddGroup(list, "JWT Header", jwt.HeaderJson);
                    AddGroup(list, "JWT Payload", jwt.PayloadJson);
                }
                else
                {
                    string t = v.Trim();
                    if (t.Length >= 2 && (t[0] == '{' || t[0] == '['))
                    {
                        string pretty = JsonTools.Pretty(t);
                        if (pretty != null && pretty != "{}" && pretty != "[]") AddGroup(list, L.T("嵌套 JSON"), pretty);
                    }
                }
                virt = list.Count > 0 ? list : null;
                return virt;
            }
        }

        void AddGroup(List<JNode> list, string label, string json)
        {
            if (json == null) return;
            var n = Dom.Parse(json, 0);
            if (n == null) return;
            n.Label = label; n.Parent = this;
            MarkVirtual(n);
            list.Add(n);
        }

        static void MarkVirtual(JNode n)
        {
            n.Virtual = true;
            if (n.Children != null) foreach (var c in n.Children) MarkVirtual(c);
        }

        /// <summary>是否可能有虚拟子节点（只做快速判断，不真正解码）</summary>
        public bool MaybeVirtual
        {
            get
            {
                if (Kind != JKind.String || End - VStart < 8) return false;
                char c = Src[VStart + 1];
                if (c == '{' || c == '[' || c == ' ' || c == '\\') return VirtualChildren != null;
                return (c == 'e' || c == 'B') && Src.IndexOf('.', VStart, End - VStart) > 0 && VirtualChildren != null;
            }
        }
    }

    public static class Dom
    {
        /// <summary>严格解析 start 处的一个 JSON 值，返回带位置信息的结构；失败返回 null</summary>
        public static JNode Parse(string s, int start)
        {
            int p = start;
            while (p < s.Length && char.IsWhiteSpace(s[p])) p++;
            try { return Val(s, ref p, null, 0); }
            catch (JsonFail) { return null; }
            catch (IndexOutOfRangeException) { return null; }
            catch (ArgumentOutOfRangeException) { return null; }
        }

        static void Ws(string s, ref int p) { while (p < s.Length && (s[p] == ' ' || s[p] == '\t' || s[p] == '\r' || s[p] == '\n')) p++; }

        static JNode Val(string s, ref int p, JNode parent, int depth)
        {
            if (depth > 512) throw new JsonFail(L.T("嵌套过深"), p);
            Ws(s, ref p);
            if (p >= s.Length) throw new JsonFail(L.T("结尾"), p);
            var n = new JNode { Parent = parent, Src = s, Start = p, VStart = p };
            char c = s[p];
            if (c == '{')
            {
                n.Kind = JKind.Object; n.Children = new List<JNode>(); p++; Ws(s, ref p);
                if (s[p] == '}') { p++; n.End = p; return n; }
                while (true)
                {
                    Ws(s, ref p);
                    if (s[p] != '"') throw new JsonFail(L.T("键"), p);
                    int ks = p; SkipStr(s, ref p);
                    string key = JsonTools.Unescape(s, ks + 1, p - ks - 2);
                    Ws(s, ref p);
                    if (s[p] != ':') throw new JsonFail(":", p);
                    p++;
                    var child = Val(s, ref p, n, depth + 1);
                    child.Key = key; child.Start = ks;
                    n.Children.Add(child);
                    Ws(s, ref p);
                    if (s[p] == ',') { p++; continue; }
                    if (s[p] == '}') { p++; break; }
                    throw new JsonFail(",}", p);
                }
            }
            else if (c == '[')
            {
                n.Kind = JKind.Array; n.Children = new List<JNode>(); p++; Ws(s, ref p);
                if (s[p] == ']') { p++; n.End = p; return n; }
                while (true)
                {
                    var child = Val(s, ref p, n, depth + 1);
                    child.Index = n.Children.Count;
                    n.Children.Add(child);
                    Ws(s, ref p);
                    if (s[p] == ',') { p++; continue; }
                    if (s[p] == ']') { p++; break; }
                    throw new JsonFail(",]", p);
                }
            }
            else if (c == '"') { n.Kind = JKind.String; SkipStr(s, ref p); }
            else if (c == '-' || (c >= '0' && c <= '9'))
            {
                n.Kind = JKind.Number;
                while (p < s.Length && "0123456789+-.eE".IndexOf(s[p]) >= 0) p++;
            }
            else if (Lit(s, ref p, "true") || Lit(s, ref p, "false") || Lit(s, ref p, "null")) n.Kind = JKind.Literal;
            else throw new JsonFail(L.T("值"), p);
            n.End = p;
            return n;
        }

        static bool Lit(string s, ref int p, string lit)
        {
            if (p + lit.Length <= s.Length && string.CompareOrdinal(s, p, lit, 0, lit.Length) == 0) { p += lit.Length; return true; }
            return false;
        }

        static void SkipStr(string s, ref int p)
        {
            p++;
            while (true)
            {
                char c = s[p];
                if (c == '"') { p++; return; }
                if (c == '\\') { p += 2; continue; }
                if (c == '\n') throw new JsonFail(L.T("换行"), p);
                p++;
            }
        }

        static readonly Regex IdentKey = new Regex(@"^[A-Za-z_$][A-Za-z0-9_$]*$");

        /// <summary>节点路径，如 subscribe.topic[2]、token‹JWT Payload›.exp</summary>
        public static string PathOf(JNode n)
        {
            var parts = new List<string>();
            for (var x = n; x != null; x = x.Parent)
            {
                if (x.Label != null) parts.Add("‹" + x.Label + "›");
                else if (x.Key != null) parts.Add(IdentKey.IsMatch(x.Key) ? "." + x.Key : "[\"" + x.Key.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"]");
                else if (x.Index >= 0) parts.Add("[" + x.Index + "]");
            }
            parts.Reverse();
            string path = string.Concat(parts.ToArray());
            if (path.StartsWith(".")) path = path.Substring(1);
            return path.Length == 0 ? L.T("（根）") : path;
        }

        /// <summary>找到包含 idx 的最深节点（只在原文节点中查找）</summary>
        public static JNode NodeAt(JNode root, int idx)
        {
            if (root == null || idx < root.Start || idx > root.End) return null;
            var n = root;
            while (n.Children != null && n.Children.Count > 0)
            {
                var ch = n.Children;
                int lo = 0, hi = ch.Count - 1, found = -1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (idx < ch[mid].Start) hi = mid - 1;
                    else if (idx > ch[mid].End) lo = mid + 1;
                    else { found = mid; break; }
                }
                if (found < 0) break;
                n = ch[found];
            }
            return n;
        }
    }

    // ===================== JWT =====================
    public sealed class Jwt
    {
        public string HeaderJson, PayloadJson, Signature, Raw;

        static readonly Regex Shape = new Regex(@"^eyJ[A-Za-z0-9_-]+\.eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*$");

        /// <summary>识别并解码 JWT（不验证签名）；不是 JWT 返回 null</summary>
        public static Jwt Decode(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(7).Trim();
            if (s.Length < 20 || s.Length > 100000 || !Shape.IsMatch(s)) return null;
            string[] parts = s.Split('.');
            string h = B64(parts[0]), pl = B64(parts[1]);
            if (h == null || pl == null) return null;
            string hp = JsonTools.Pretty(h), pp = JsonTools.Pretty(pl);
            if (hp == null || pp == null) return null;
            return new Jwt { HeaderJson = hp, PayloadJson = pp, Signature = parts[2], Raw = s };
        }

        static string B64(string seg)
        {
            try
            {
                string t = seg.Replace('-', '+').Replace('_', '/');
                switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; case 1: return null; }
                return Encoding.UTF8.GetString(Convert.FromBase64String(t));
            }
            catch (FormatException) { return null; }
        }

        /// <summary>取 JSON 顶层某个键的值（字符串会解码）</summary>
        public static string Claim(string json, string key)
        {
            var root = Dom.Parse(json, 0);
            if (root == null || root.Children == null) return null;
            foreach (var c in root.Children) if (c.Key == key) return c.Kind == JKind.String ? c.StringValue : c.Raw;
            return null;
        }

        /// <summary>悬停提示用的简短说明</summary>
        public string Summary()
        {
            var lines = new List<string> { L.T("JWT（未验证签名）") };
            string alg = Claim(HeaderJson, "alg");
            if (alg != null) lines.Add(L.T("算法：") + alg);
            foreach (var k in new[] { "iat", "nbf", "exp" })
            {
                string v = Claim(PayloadJson, k);
                if (v == null) continue;
                string t = TimeHint.Format(v);
                lines.Add(ClaimName(k) + L.T("：") + (t ?? v));
            }
            string exp = Claim(PayloadJson, "exp");
            DateTimeOffset e;
            if (exp != null && TimeHint.TryParse(exp, out e)) lines.Add(e < DateTimeOffset.Now ? L.T("状态：已过期") : L.T("状态：未过期"));
            lines.Add(L.T("右键 →「解码 JWT」查看完整内容"));
            return string.Join("\n", lines.ToArray());
        }

        static string ClaimName(string k) { return k == "iat" ? L.T("签发时间 iat") : k == "nbf" ? L.T("生效时间 nbf") : L.T("过期时间 exp"); }

        /// <summary>解码结果全文（在新窗口中显示）</summary>
        public string Report()
        {
            var b = new StringBuilder();
            b.Append(L.T("// JWT 解码结果（仅 Base64 解码，未验证签名）", "// Decoded JWT (Base64 only, signature NOT verified)")).Append("\r\n");
            b.Append("// ----- Header -----\r\n").Append(HeaderJson).Append("\r\n");
            b.Append("// ----- Payload -----\r\n").Append(PayloadJson).Append("\r\n");
            foreach (var k in new[] { "iat", "nbf", "exp" })
            {
                string v = Claim(PayloadJson, k);
                string t = v == null ? null : TimeHint.Format(v);
                if (t != null) b.Append("// ").Append(ClaimName(k)).Append(L.T("：")).Append(t).Append("\r\n");
            }
            b.Append("// ----- Signature -----\r\n// ").Append(Signature).Append("\r\n");
            return b.ToString();
        }
    }

    // ===================== 时间戳 =====================
    public static class TimeHint
    {
        /// <summary>10 位（秒）或 13 位（毫秒）整数，且在 2000~2100 年之间，视为时间戳</summary>
        public static bool TryParse(string digits, out DateTimeOffset t)
        {
            t = default(DateTimeOffset);
            if (digits == null || (digits.Length != 10 && digits.Length != 13)) return false;
            foreach (char c in digits) if (c < '0' || c > '9') return false;
            long v = long.Parse(digits, CultureInfo.InvariantCulture);
            if (digits.Length == 13) v /= 1000;
            if (v < 946684800L || v > 4102444800L) return false;
            t = DateTimeOffset.FromUnixTimeSeconds(v).ToLocalTime();
            return true;
        }

        public static string Format(string digits)
        {
            DateTimeOffset t;
            if (!TryParse(digits, out t)) return null;
            return t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + L.F("（{0}）", Relative(t));
        }

        public static string Describe(string digits)
        {
            string f = Format(digits);
            if (f == null) return null;
            var off = DateTimeOffset.Now.Offset;
            return (digits.Length == 13 ? L.T("毫秒时间戳", "Millisecond timestamp") : L.T("秒级时间戳", "Unix timestamp (seconds)")) + "\n" + f + "\nUTC" + (off < TimeSpan.Zero ? "-" : "+") + off.ToString(@"hh\:mm");
        }

        public static string Relative(DateTimeOffset t)
        {
            var d = DateTimeOffset.Now - t;
            bool past = d >= TimeSpan.Zero;
            if (!past) d = -d;
            string v;
            if (d.TotalSeconds < 60) v = L.F("{0} 秒", (int)d.TotalSeconds);
            else if (d.TotalMinutes < 60) v = L.F("{0} 分钟", (int)d.TotalMinutes);
            else if (d.TotalHours < 24) v = L.F("{0} 小时", (int)d.TotalHours);
            else if (d.TotalDays < 365) v = L.F("{0} 天", (int)d.TotalDays);
            else v = L.F("{0} 年", (d.TotalDays / 365.25).ToString("0.#", CultureInfo.InvariantCulture));
            return past ? L.F("{0}前", v) : L.F("{0}后", v);
        }
    }

    // ===================== 敏感字段打码 =====================
    public static class Mask
    {
        public const string DefaultKeys = "password, passwd, pwd, secret, token, access_token, refresh_token, authorization, cookie, session, api_key, apikey, private_key, phone, mobile, email, mail, id_card, idcard, bank_card, sign, signature";
        public const string Stars = "******";

        public static List<string> ParseKeys(string text)
        {
            var list = new List<string>();
            foreach (var k in (text ?? "").Split(new[] { ',', '，', ';', '；', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = k.Trim().ToLowerInvariant();
                if (t.Length > 0 && !list.Contains(t)) list.Add(t);
            }
            return list;
        }

        /// <summary>键名是否敏感：4 个字母及以上的关键字按“包含”匹配（如 access_token 含 token），更短的要求完全相同</summary>
        public static bool IsSensitive(string key, List<string> keys)
        {
            string k = key.ToLowerInvariant();
            foreach (var w in keys)
                if (k == w || (w.Length >= 4 && k.Contains(w))) return true;
            return false;
        }

        /// <summary>把文本中 JSON 部分的敏感字段值替换为 "******"；值本身是 JWT 的也会打码。</summary>
        public static string Apply(string text, List<string> keys, out int count)
        {
            count = 0;
            var spans = new List<int[]>();
            foreach (var r in JsonTools.FindJsonRanges(text))
            {
                var root = Dom.Parse(text, r[0]);
                if (root != null) Collect(root, keys, spans);
            }
            if (spans.Count == 0) return text;
            spans.Sort((a, b) => a[0].CompareTo(b[0]));
            var sb = new StringBuilder(text.Length);
            int pos = 0;
            foreach (var sp in spans)
            {
                if (sp[0] < pos) continue;
                sb.Append(text, pos, sp[0] - pos).Append('"').Append(Stars).Append('"');
                pos = sp[1];
                count++;
            }
            sb.Append(text, pos, text.Length - pos);
            return sb.ToString();
        }

        static void Collect(JNode n, List<string> keys, List<int[]> spans)
        {
            bool scalar = n.Kind == JKind.String || n.Kind == JKind.Number;
            if (scalar && !(n.Kind == JKind.String && n.End - n.VStart <= 2)
                && ((n.Key != null && IsSensitive(n.Key, keys)) || (n.Kind == JKind.String && Jwt.Decode(n.StringValue) != null)))
            {
                spans.Add(new[] { n.VStart, n.End });
                return;
            }
            if (n.Children != null) foreach (var c in n.Children) Collect(c, keys, spans);
        }
    }
}
