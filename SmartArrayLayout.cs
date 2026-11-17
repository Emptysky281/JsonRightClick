using System;
using System.Collections.Generic;
using System.Text;

namespace JsonRightClick
{
    /// <summary>
    /// 对已经验证并规范化的紧凑 JSON 做智能排版：只索引容器区间，不构造/转换值对象。
    /// 短普通数组单行；对象数组逐项分行；简单对象每项一行，复杂/过长对象展开。
    /// </summary>
    sealed class SmartArrayLayout
    {
        internal const int LineLimit = 120, ObjectFieldLimit = 6;
        sealed class Container
        {
            internal int Start, End, Count, Width;
            internal char Kind;
            internal bool HasContainers, ComplexChild;
            internal bool ShortArray { get { return Kind == '[' && !HasContainers && Width <= LineLimit; } }
            internal bool SimpleObject { get { return Kind == '{' && Count <= ObjectFieldLimit && !ComplexChild && Width <= LineLimit; } }
        }
        readonly string text;
        readonly Dictionary<int, Container> containers = new Dictionary<int, Container>();
        readonly StringBuilder output;
        int at, column;

        SmartArrayLayout(string text)
        { this.text = text; output = new StringBuilder(text.Length); }

        internal static string Format(string canonical, int depth)
        {
            if (canonical == null) throw new ArgumentNullException("canonical");
            if (canonical.Length == 0) return canonical;
            var layout = new SmartArrayLayout(canonical);
            layout.Index();
            layout.Value(depth, false);
            return layout.output.ToString();
        }
        int StringEnd(int start)
        {
            int end = start + 1;
            while (end < text.Length)
            {
                if (text[end] == '\\') { end += 2; continue; }
                if (text[end++] == '"') return end;
            }
            throw new FormatException("Expected a complete validated JSON string.");
        }
        void Index()
        {
            var stack = new Stack<Container>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') { i = StringEnd(i) - 1; continue; }
                if (c == '{' || c == '[')
                {
                    if (stack.Count > 0) stack.Peek().HasContainers = true;
                    var node = new Container { Start = i, Kind = c };
                    containers.Add(i, node); stack.Push(node);
                }
                else if (c == '}' || c == ']')
                {
                    var node = stack.Pop(); node.End = i + 1;
                    if (node.Kind == '[' && node.End - node.Start > 2) node.Count++;
                    node.Width = node.End - node.Start;
                    if (node.Kind == '{') node.Width += node.Count + Math.Max(0, node.Count - 1); // ": " and ", "
                    if (stack.Count > 0 && node.End - node.Start > 2 && !node.ShortArray)
                        stack.Peek().ComplexChild = true;
                }
                else if (stack.Count > 0 && ((c == ':' && stack.Peek().Kind == '{') || (c == ',' && stack.Peek().Kind == '[')))
                    stack.Peek().Count++;
            }
        }
        void Raw(int start, int length)
        { output.Append(text, start, length); column += length; }
        void Add(char c) { output.Append(c); column++; }
        void Space() { Add(' '); }
        void NewLine(int depth)
        { output.Append("\r\n").Append(' ', depth * 2); column = depth * 2; }
        void String()
        { int end = StringEnd(at); Raw(at, end - at); at = end; }

        void Value(int depth, bool arrayItem)
        {
            char c = text[at];
            if (c == '"') { String(); return; }
            if (c == '{') { Object(depth, arrayItem); return; }
            if (c == '[') { Array(depth); return; }
            int start = at;
            while (at < text.Length && text[at] != ',' && text[at] != ']' && text[at] != '}') at++;
            Raw(start, at - start); // 不经 double/DOM 转换，保留大整数、小数精度及字符串转义原文。
        }
        void Object(int depth, bool arrayItem)
        {
            Container node = containers[at];
            if (node.Count == 0) { Raw(at, node.End - at); at = node.End; return; }
            bool inline = arrayItem && node.SimpleObject && column + node.Width <= LineLimit;
            Add('{'); at++;
            for (int item = 0; item < node.Count; item++)
            {
                if (item > 0) { Add(','); at++; if (inline) Space(); }
                if (!inline) NewLine(depth + 1);
                String(); // property name
                Add(':'); Space(); at++;
                Value(depth + 1, false);
            }
            if (!inline) NewLine(depth);
            Add('}'); at++;
        }
        void Array(int depth)
        {
            Container node = containers[at];
            if (node.Count == 0 || (node.ShortArray && column + node.Width <= LineLimit))
            { Raw(at, node.End - at); at = node.End; return; }
            Add('['); at++;
            for (int item = 0; item < node.Count; item++)
            {
                if (item > 0) { Add(','); at++; }
                NewLine(depth + 1);
                Value(depth + 1, true);
            }
            NewLine(depth); Add(']'); at++;
        }
    }
}
