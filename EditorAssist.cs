using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JsonRightClick
{
    /// <summary>当前显示文本的括号索引。只扫描一次，不依赖格式化前的片段位置或虚拟树节点。</summary>
    sealed class BracketIndex
    {
        readonly string text;
        readonly Dictionary<int, int> pairs = new Dictionary<int, int>();
        readonly List<int> lines = new List<int> { 0 };

        public BracketIndex(string text)
        {
            this.text = text;
            var stack = new Stack<int>();
            char quote = '\0';
            bool escape = false, lineComment = false, blockComment = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n' || (c == '\r' && (i + 1 == text.Length || text[i + 1] != '\n'))) lines.Add(i + 1);
                if (lineComment) { if (c == '\r' || c == '\n') lineComment = false; continue; }
                if (blockComment)
                {
                    if (c == '*' && i + 1 < text.Length && text[i + 1] == '/') { blockComment = false; i++; }
                    continue;
                }
                if (quote != '\0')
                {
                    // 未完成的编辑 / 日志里的引号不能吞掉后面整份文档。
                    if (c == '\r' || c == '\n') { quote = '\0'; escape = false; }
                    else if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == quote) quote = '\0';
                    continue;
                }
                if (c == '"' || (c == '\'' && stack.Count > 0)) { quote = c; continue; }
                if (c == '/' && i + 1 < text.Length && stack.Count > 0)
                {
                    if (text[i + 1] == '/') { lineComment = true; i++; continue; }
                    if (text[i + 1] == '*') { blockComment = true; i++; continue; }
                }
                if (c == '{' || c == '[') stack.Push(i);
                else if (c == '}' || c == ']')
                {
                    if (stack.Count == 0) continue;
                    int start = stack.Peek();
                    if ((text[start] == '{' && c == '}') || (text[start] == '[' && c == ']'))
                    {
                        stack.Pop();
                        pairs[start] = i; pairs[i] = start;
                    }
                    else stack.Clear(); // 错配时不能跨过错误括号，连到另一个层级。
                }
            }
        }

        public static bool IsBracket(string text, int index)
        {
            return index >= 0 && index < text.Length && "{}[]".IndexOf(text[index]) >= 0;
        }

        public bool TryGetPair(int index, out int other) { return pairs.TryGetValue(index, out other); }

        public List<FoldRegion> FoldRegions()
        {
            var result = new List<FoldRegion>();
            foreach (var pair in pairs)
            {
                if (pair.Key + 1 >= pair.Value) continue;
                int line = lines.BinarySearch(pair.Key), endLine = lines.BinarySearch(pair.Value);
                if (line < 0) line = ~line - 1;
                if (endLine < 0) endLine = ~endLine - 1;
                result.Add(new FoldRegion { Open = pair.Key, Close = pair.Value, Lines = endLine - line,
                    Label = text[pair.Key] == '{' ? "{…}" : "[…]" });
            }
            result.Sort((a, b) => a.Open.CompareTo(b.Open));
            return result;
        }


        public string ContextAt(int index)
        {
            int line = lines.BinarySearch(index);
            if (line < 0) line = ~line - 1;
            int start = lines[line], end = line + 1 < lines.Count ? lines[line + 1] : text.Length;
            // 压缩 JSON 的单行可能非常长，只截取括号附近，且始终保留目标括号。
            int from = Math.Max(start, index - 80), to = Math.Min(end, from + 140);
            string snippet = (from > start ? "…" : "") + text.Substring(from, to - from).Trim() + (to < end ? "…" : "");
            return string.Format(L.T("第 {0} 行 · {1}", "Line {0} · {1}"), line + 1, snippet);
        }
    }

    /// <summary>鼠标附近的提示：限制宽度、屏幕边界，空间不足时改在鼠标上方显示。</summary>
    sealed class CursorToolTip : ToolTip
    {
        const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
        Size measured;
        int padding;
        string caption;
        int measureWidth;
        public Rectangle DisplayBounds { get; private set; }

        public CursorToolTip()
        {
            InitialDelay = ReshowDelay = 0; AutoPopDelay = 20000;
            UseAnimation = UseFading = false; OwnerDraw = true;
            Popup += delegate(object sender, PopupEventArgs e) { e.ToolTipSize = measured; };
            Draw += delegate(object sender, DrawToolTipEventArgs e)
            {
                var th = Theme.Current;
                using (var b = new SolidBrush(th.Menu)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(th.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                var r = e.Bounds; r.Inflate(-padding, -padding);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, SystemFonts.MessageBoxFont, r, th.BarFore, Flags);
            };
        }

        public static Rectangle Place(Point cursor, Size size, Rectangle work, int gap)
        {
            int width = Math.Min(size.Width, work.Width), height = Math.Min(size.Height, work.Height);
            int x = Math.Max(work.Left, Math.Min(cursor.X + gap, work.Right - width));
            int y = cursor.Y + gap;
            if (y + height > work.Bottom) y = cursor.Y - gap - height;
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - height));
            return new Rectangle(x, y, width, height);
        }

        public void ShowNear(string text, Control owner, Point point)
        {
            Point screen = owner.PointToScreen(point);
            Rectangle work = Screen.FromPoint(screen).WorkingArea;
            float scale;
            using (var g = owner.CreateGraphics()) scale = g.DpiX / 96f;
            padding = Math.Max(4, (int)(7 * scale));
            int maxWidth = Math.Max(1, Math.Min((int)(520 * scale), work.Width - 2 * padding));
            if (caption != text || measureWidth != maxWidth)
            {
                caption = text; measureWidth = maxWidth;
                var content = TextRenderer.MeasureText(text, SystemFonts.MessageBoxFont, new Size(maxWidth, int.MaxValue), Flags);
                measured = new Size(Math.Min(work.Width, Math.Min(maxWidth, content.Width) + 2 * padding), Math.Min(work.Height, content.Height + 2 * padding));
            }
            DisplayBounds = Place(screen, measured, work, Math.Max(12, (int)(18 * scale)));
            Point at = owner.PointToClient(DisplayBounds.Location);
            Show(text, owner, at.X, at.Y);
        }

        public void HideFrom(Control owner) { Hide(owner); DisplayBounds = Rectangle.Empty; }
    }
}
