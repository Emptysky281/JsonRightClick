using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace JsonRightClick
{
    static class EditorRegressionTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks;
        static object Field(object obj, string name) { return obj.GetType().GetField(name, Private).GetValue(obj); }
        static object Call(object obj, string name, params object[] args) { return obj.GetType().GetMethod(name, Private).Invoke(obj, args); }
        static void Check(bool condition, string message)
        {
            checks++;
            if (!condition) throw new Exception(message);
        }
        static void Run(string name, Action test)
        {
            var watch = Stopwatch.StartNew(); test();
            Console.WriteLine("PASS " + name + " (" + watch.ElapsedMilliseconds + " ms)");
        }

        [STAThread]
        static int Main()
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                ViewSettings.NoPersist = true;
                ViewSettings.ShowTree = false; ViewSettings.FontName = "Consolas"; ViewSettings.FontSize = 10.5f;
                Run("bracket index: nesting, escaping, comments, invalid input", IndexTests);
                Run("bracket index: recursive nested-string expansion", ExpansionIndexTests);
                Run("bracket index: large document and deep nesting", LargeIndexTests);
                Run("tooltip layout: screen edges and negative monitor coordinates", PlacementTests);
                Run("editor: click/caret, selection, edits, undo, themes, search", EditorTests);
                Run("editor: expand nested, minify, undo, JSON-only, mask", TransformationTests);
                Run("editor: off-screen matching line previews and jump", PreviewTests);
                Run("editor: long-token hover follows mouse and invalidates", HoverTests);
                Run("editor: large-file visible coloring preserves bracket match", LargeEditorTests);
                Run("folding: structural ranges, strings and empty containers", FoldingModelTests);
                Run("folding: native layout, full text and undo stay intact", FoldingNativeTests);
                Run("folding: independent nested states and clickable placeholders", FoldingNestedTests);
                Run("folding: gutter plus/minus and keyboard shortcuts", FoldingControlsTests);
                Run("folding: edits, Delete/Backspace, IME and undo", FoldingEditingTests);
                Run("folding: search and tree navigation reveal hidden targets", FoldingNavigationTests);
                Run("folding: restyle, nested expansion, minify, JSON-only and mask", FoldingTransformTests);
                Run("folding: large files and long wrapped values", FoldingLargeTests);
                Run("rendering: cached native layout matches Framework positions", NativeLayoutTests);
                Run("rendering: token index clips long strings and supports budget resume", SyntaxIndexTests);
                Run("rendering: range colors preserve caret, structure cache and undo", StableColoringTests);
                Run("rendering: long matches visit visible rows only", BoundedHighlightTests);
                Run("scrolling: real movement, notification dedup and long-file easing", SmoothScrollTests);
                Run("scope guides: full range, viewport clipping, folding and zoom", ScopeGuideTests);
                Run("fluid scrolling: high precision, immediate response, reversal and bounded queue", FluidScrollingTests);
                Run("pixel scrolling: hidden Unicode text, selections and mutation safety", HiddenTextCompatibilityTests);
                Console.WriteLine("ALL PASSED: " + checks + " assertions");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
        }

        static Dictionary<int, int> ExpectedPairs(JNode node)
        {
            var pairs = new Dictionary<int, int>();
            AddExpected(node, pairs);
            return pairs;
        }
        static void AddExpected(JNode node, Dictionary<int, int> pairs)
        {
            if (node.Kind == JKind.Array || node.Kind == JKind.Object)
            { pairs[node.VStart] = node.End - 1; pairs[node.End - 1] = node.VStart; }
            if (node.Children != null) foreach (var child in node.Children) AddExpected(child, pairs);
        }
        static void VerifyIndex(string text)
        {
            var node = Dom.Parse(text, 0);
            Check(node != null, "test input must parse");
            var expected = ExpectedPairs(node);
            var index = new BracketIndex(text);
            for (int i = 0; i < text.Length; i++)
            {
                int got, want;
                bool found = index.TryGetPair(i, out got), needed = expected.TryGetValue(i, out want);
                Check(found == needed && (!needed || got == want), "incorrect pair at " + i + " in " + text.Substring(0, Math.Min(60, text.Length)));
            }
        }
        static void IndexTests()
        {
            foreach (var text in new[] {
                "{}", "[]", "{\"a\": [{}, [1, {\"b\": []}]], \"c\": {\"d\": 2}}",
                "{\"text\":\"{not} [a] \\\"quoted\\\" \\\\ end\",\"tail\":[]}",
                "[\"\\\\\",{},\"\\\"[ignore]\\\"\",[[]]]",
                "{\"x\":\"\\u007b\\u005d\",\"y\":[{},{}]}"
            }) VerifyIndex(text);
            int other;
            string commented = "{ /* [ } */ 'x': '[ ]', // } [\n 'a': [] }";
            var index = new BracketIndex(commented);
            Check(index.TryGetPair(0, out other) && other == commented.Length - 1, "comments/single quotes should be skipped");
            Check(!index.TryGetPair(5, out other), "comment brackets must not match");
            index = new BracketIndex("{[}] [] {");
            Check(!index.TryGetPair(0, out other) && !index.TryGetPair(1, out other), "mismatched types must not cross");
            Check(index.TryGetPair(5, out other) && other == 6, "recover after mismatched types");
            Check(!index.TryGetPair(8, out other), "unclosed bracket must not match");
            index = new BracketIndex("log \"unterminated\n{\n  \"a\": []\n}");
            Check(index.TryGetPair(18, out other), "unterminated log quote must not swallow next line");
            Check(index.ContextAt(18).Contains("2"), "preview line number must be logical line");
        }
        static string NestedInput()
        { return "{\"data\":\"{\\\"a\\\":1,\\\"b\\\":\\\"[1,{\\\\\\\"x\\\\\\\":[]} ]\\\"}\",\"password\":\"secret-value\",\"plain\":\"braces { and [ ] }\"}"; }
        static void ExpansionIndexTests()
        {
            var result = JsonTools.Process(NestedInput(), new FormatOptions { Expand = true });
            Check(result.Count == 1 && result.Expanded == 2, "recursive expansion expected");
            VerifyIndex(result.Pieces[0].Text);
            VerifyIndex(JsonTools.Process(NestedInput(), false).Pieces[0].Text);
            var index = new BracketIndex("{\n\"a\": [1]\n}"); int other;
            Check(index.TryGetPair(7, out other) && other == 9, "LF offsets");
            index = new BracketIndex("{\r\n\"a\": [1]\r\n}");
            Check(index.TryGetPair(8, out other) && other == 10, "CRLF offsets");
        }
        static void LargeIndexTests()
        {
            string text = "{\"long\":\"" + new string('x', 8000000) + " [not structural] {ignored}\",\"end\":[{}]}";
            var watch = Stopwatch.StartNew(); var index = new BracketIndex(text); int other;
            Check(index.TryGetPair(0, out other) && other == text.Length - 1, "no DOM size cap on bracket indexing");
            int array = text.LastIndexOf('[');
            Check(index.TryGetPair(array, out other) && other == text.Length - 2, "large-tail pair");
            Check(!index.TryGetPair(text.IndexOf('['), out other), "large string is not structure");
            Console.WriteLine("  8 MB bracket index: " + watch.ElapsedMilliseconds + " ms");
            text = new string('[', 20000) + new string(']', 20000); index = new BracketIndex(text);
            Check(index.TryGetPair(19999, out other) && other == 20000, "iterative index supports deep nesting");
            Check(index.TryGetPair(0, out other) && other == 39999, "deep root pair");
        }
        static void PlacementTests()
        {
            foreach (var work in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1920, -240, 1920, 1040), new Rectangle(0, 0, 320, 240) })
                foreach (var cursor in new[] { work.Location, new Point(work.Right - 1, work.Bottom - 1), new Point(work.Left + work.Width / 2, work.Top + work.Height / 2) })
                {
                    var bounds = CursorToolTip.Place(cursor, new Size(450, 110), work, 18);
                    Check(work.Contains(bounds), "tooltip must stay in monitor work area");
                    if (cursor.Y == work.Bottom - 1 && work.Height > 150) Check(bounds.Bottom < cursor.Y, "bottom tooltip must flip above cursor");
                }
        }
        static JsonViewer Open(string text)
        {
            var viewer = new JsonViewer(text);
            viewer.Size = new Size(820, 450);
            viewer.Show(); Application.DoEvents();
            return viewer;
        }
        static CodeBox Box(JsonViewer viewer) { return (CodeBox)Field(viewer, "box"); }
        static void VerifyRoot(JsonViewer viewer)
        {
            var box = Box(viewer); int start = box.Text.IndexOf('{'), close = box.Text.LastIndexOf('}');
            box.Select(start, 0);
            Check(box.BracketOpen == start && box.BracketClose == close, "root pair must follow current display");
        }
        static void EditorTests()
        {
            using (var v = Open("{\"outer\":[{\"inner\":[]},{}],\"plain\":\"[ignored]\"}"))
            {
                var b = Box(v); string before = b.Text, rtf = b.Rtf; bool undo = b.CanUndo;
                int open = before.IndexOf('['), close = before.IndexOf("],", StringComparison.Ordinal);
                b.Select(open, 0);
                Check(b.BracketOpen == open && b.BracketClose == close, "caret on opening bracket");
                b.Select(close + 1, 0);
                Check(b.BracketOpen == open && b.BracketClose == close, "caret after closing bracket");
                Call(v, "UpdateBrackets", close);
                Check(b.BracketOpen == open, "click closing bracket uses actual hit");
                b.Select(open, 2); Check(b.BracketOpen == -1, "selection clears pair");
                int plain = b.Text.IndexOf("[ignored]", StringComparison.Ordinal);
                b.Select(plain, 0); Check(b.BracketOpen == -1, "string brackets must not highlight");
                VerifyRoot(v); Application.DoEvents();
                Check(b.Text == before && b.Rtf == rtf && b.CanUndo == undo && b.SelectionLength == 0, "overlay must not alter document/undo/selection");
                Check(!(bool)Field(v, "edited"), "bracket highlighting must not mark document edited");
                object cached = Field(v, "bracketIndex");
                b.Select(close, 0); Check(ReferenceEquals(cached, Field(v, "bracketIndex")), "caret movement must reuse index");
                b.Select(open, 0); b.SelectedText = " ";
                VerifyRoot(v); Check(!ReferenceEquals(cached, Field(v, "bracketIndex")), "edit must invalidate index");
                b.Undo(); Check(b.Text == before, "native undo remains intact"); VerifyRoot(v);
                ViewSettings.Dark = true; Call(v, "Restyle"); VerifyRoot(v);
                ViewSettings.FontSize = 14; Call(v, "Restyle"); VerifyRoot(v);
                var search = (ToolStripTextBox)Field(v, "search"); search.Text = "["; Call(v, "UpdateMatches", false);
                VerifyRoot(v); Check(b.Highlights.Count > 0 && b.BracketOpen >= 0, "search and bracket overlays coexist");
                Check((bool)Field(v, "edited"), "overlays/restyle should preserve existing edited state");
                ViewSettings.Dark = false; ViewSettings.FontSize = 10.5f;
            }
        }
        static void TransformationTests()
        {
            using (var v = Open(NestedInput()))
            {
                string before = Box(v).Text;
                Call(v, "Reprocess", false, true); VerifyRoot(v);
                var b = Box(v); int array = b.Text.IndexOf('[');
                b.Select(array, 0); Check(b.BracketClose > array, "expanded nested array matches");
                Call(v, "Reprocess", true, false); VerifyRoot(v);
                Call(v, "Undo"); VerifyRoot(v);
                Call(v, "Undo"); Check(b.Text == before, "nested expansion undo restores source"); VerifyRoot(v);
                var mask = (ToolStripButton)Field(v, "btnMask"); mask.Checked = true;
                Check(b.ReadOnly, "mask enabled"); VerifyRoot(v);
                mask.Checked = false; Check(b.Text == before, "unmask restores source offsets"); VerifyRoot(v);
            }
            using (var v = Open("prefix {\"a\": [{},{}]} middle {\"b\": []} suffix"))
            {
                var only = (ToolStripButton)Field(v, "btnOnly"); only.Checked = true;
                var b = Box(v); int open = b.Text.LastIndexOf('['), close = b.Text.LastIndexOf(']');
                b.Select(open, 0); Check(b.BracketOpen == open && b.BracketClose == close, "JSON-only fragment headers must not shift pair");
                only.Checked = false;
                open = b.Text.LastIndexOf('['); close = b.Text.LastIndexOf(']'); b.Select(open, 0);
                Check(b.BracketOpen == open && b.BracketClose == close, "restoring full log uses new offsets");
            }
        }
        static void PreviewTests()
        {
            var input = new StringBuilder("{\"items\":[");
            for (int i = 0; i < 160; i++) { if (i > 0) input.Append(','); input.Append("{\"value\":").Append(i).Append('}'); }
            input.Append("]}");
            using (var v = Open(input.ToString()))
            {
                var b = Box(v); VerifyRoot(v); b.ScrollPos = Point.Empty; Application.DoEvents();
                int target; Rectangle bounds; string text;
                Check(b.TryGetBracketPreview(out target, out bounds, out text) && target == b.TextLength - 1, "off-screen closing bracket preview");
                Check(bounds.Bottom <= b.ClientSize.Height && bounds.Top > b.ClientSize.Height / 2, "closing preview pinned to bottom");
                var point = new Point(bounds.Left + 15, bounds.Top + bounds.Height / 2);
                IntPtr lp = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
                Native.SendMessage(b.Handle, 0x0201, (IntPtr)1, lp); Native.SendMessage(b.Handle, 0x0202, IntPtr.Zero, lp); Application.DoEvents();
                Check(b.SelectionStart == b.TextLength - 1 && b.SelectionLength == 0, "preview click jumps without selecting/editing content");
                Check(b.TryGetBracketPreview(out target, out bounds, out text) && target == 0 && bounds.Top < 10, "off-screen opening bracket preview");
                Check(text.Contains("1") && text.Contains("{"), "opening context includes logical line and actual bracket");
                b.Select(b.Text.IndexOf("value", StringComparison.Ordinal), 0);
                Check(!b.TryGetBracketPreview(out target, out bounds, out text), "non-bracket caret clears preview");
            }
        }
        static Point At(CodeBox b, int index)
        {
            var p = b.GetPositionFromCharIndex(index);
            return new Point(p.X + 2, p.Y + b.LineHeight / 2);
        }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr handle, out Rect rect);
        static Rectangle ActualTipBounds(CursorToolTip tip)
        {
            var handle = (IntPtr)typeof(ToolTip).GetProperty("Handle", Private).GetValue(tip, null);
            Rect r; Check(GetWindowRect(handle, out r), "tooltip HWND available");
            return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        }
        static void HoverTests()
        {
            string inner = "{\"message\":\"" + new string('x', 31000) + "\",\"values\":[1,2]}";
            string input = "{\"nested\":\"" + inner.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\"time\":1792057728}";
            using (var v = Open(input))
            {
                var b = Box(v); int tokenStart = b.Text.IndexOf("\"{", StringComparison.Ordinal);
                int index = tokenStart + 25000;
                var args = new object[] { index, 0, 0, null };
                string kind = (string)Call(v, "TokenAt", args);
                Check(kind == "string" && (int)args[1] == tokenStart && ((string)args[3]).Length > 30000, "hover after 20k must decode the complete token");
                b.Select(index, 0); b.ScrollToCaret(); Application.DoEvents();
                Point first = At(b, index); Call(v, "HoverAt", first); Application.DoEvents();
                var tip = (CursorToolTip)Field(v, "tip");
                Check(!tip.DisplayBounds.IsEmpty && Field(v, "tipText") != null, "nested JSON tooltip visible");
                Rectangle actual1 = ActualTipBounds(tip); Point screen1 = b.PointToScreen(first);
                Check(Math.Abs(actual1.Top - screen1.Y) < 180, "tooltip anchored to pointer, not token end");
                int start = (int)Field(v, "tipStart"); Point second = At(b, index + 8);
                Call(v, "HoverAt", second); Application.DoEvents(); Rectangle actual2 = ActualTipBounds(tip);
                Check((int)Field(v, "tipStart") == start && actual2.Location != actual1.Location, "same-token movement repositions real native tooltip");
                b.ScrollPos = new Point(0, b.ScrollPos.Y + b.LineHeight);
                Check(tip.DisplayBounds.IsEmpty, "smooth/programmatic scroll hides stale tooltip");
                b.ScrollToCaret(); Call(v, "HoverAt", At(b, index));
                Call(b, "OnHScroll", EventArgs.Empty); Check(tip.DisplayBounds.IsEmpty, "horizontal scroll hides tooltip");
                Call(v, "HoverAt", At(b, index));
                b.SelectedText = "z"; Check(tip.DisplayBounds.IsEmpty, "edit invalidates tooltip/cache");
                Check((int)Field(v, "tipVersion") == -1, "old token version cleared");
                Call(v, "Reprocess", false, true); VerifyRoot(v);
            }
            using (var v = Open("{\"time\":1792057728,\"plain\":\"not a hint\"}"))
            {
                var b = Box(v); int time = b.Text.IndexOf("1792057728", StringComparison.Ordinal);
                Call(v, "HoverAt", At(b, time + 2));
                Check(((CursorToolTip)Field(v, "tip")).DisplayBounds.Width > 0, "timestamp hints preserved");
                Call(v, "HideTip");
                int plain = b.Text.IndexOf("not a hint", StringComparison.Ordinal);
                Call(v, "HoverAt", At(b, plain + 1));
                Check(Field(v, "tipText") == null, "non-hint token not shown");
                var tip = (CursorToolTip)Field(v, "tip");
                var work = Screen.FromControl(b).WorkingArea;
                Point corner = b.PointToClient(new Point(work.Right - 5, work.Bottom - 5));
                tip.ShowNear("A very long unbroken value: " + new string('x', 2000), b, corner);
                Application.DoEvents();
                Check(work.Contains(ActualTipBounds(tip)), "real native tooltip remains on screen, even for unbroken text");
                tip.HideFrom(b);
            }
            string header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"iat\":1792057728,\"exp\":1792061328}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            using (var v = Open("{\"jwt\":\"" + header + "." + payload + ".c2ln\"}"))
            {
                var b = Box(v); int start = b.Text.IndexOf("eyJ", StringComparison.Ordinal);
                Call(v, "HoverAt", At(b, start + 3));
                Check(((string)Field(v, "tipText")).Contains("JWT"), "JWT hover summary preserved");
            }
        }
        static FoldRegion FoldFor(CodeBox b, int open)
        { return b.Folding.Regions.Find(r => r.Open == open); }
        static void FoldingModelTests()
        {
            string text = "{\"a\":[1,{\"b\":[2,3]}],\"empty\":{},\"string\":\"[not a block] {ignored}\"}";
            var ranges = new BracketIndex(text).FoldRegions();
            Check(ranges.Count == 4, "only non-empty structural containers are foldable");
            Check(ranges[0].Open == 0 && ranges[0].Close == text.Length - 1, "fold ranges sorted by opening bracket");
            foreach (var r in ranges) Check(r.Close > r.Open + 1 && r.Lines == 0, "single-line containers may be folded");
            text = "{\r\n  \"a\": [\r\n    1,\r\n    2\r\n  ]\r\n}";
            ranges = new BracketIndex(text).FoldRegions();
            Check(ranges.Count == 2 && ranges[0].Lines == 5 && ranges[1].Lines == 3, "logical line counts handle CRLF");
            ranges = new BracketIndex("{[}] {} [] \"[ignore]\"").FoldRegions();
            Check(ranges.Count == 0, "unmatched and empty brackets are not fold controls");
        }
        static string FoldingInput()
        { return "{\"first\":{\"items\":[{\"value\":1},{\"value\":2}],\"tail\":3},\"second\":{\"items\":[4,5]},\"time\":1792057728}"; }
        static void FoldingNativeTests()
        {
            using (var v = Open(FoldingInput()))
            {
                var b = Box(v); string before = b.Text; int close = before.LastIndexOf('}');
                bool initialModified = b.Modified;
                int expandedY = b.GetPositionFromCharIndex(close).Y;
                b.Select(0, 0); Check(b.FoldCurrent(true), "root collapse succeeds"); Application.DoEvents();
                Check(b.CollapsedCount == 1 && b.Folding.Hidden.Count == 1, "one effective hidden root");
                Check(b.GetPositionFromCharIndex(close).Y == b.GetPositionFromCharIndex(0).Y && expandedY > b.LineHeight,
                    "hidden newlines really collapse in the native editor layout");
                Check(b.Text == before && b.TextLength == before.Length, "fold preserves complete document and all source indices: equal=" + (b.Text == before) + " length=" + b.TextLength + " expected=" + before.Length + " actual=" + b.Text.Length);
                Check(!b.CanUndo && !(bool)Field(v, "edited") && b.Modified == initialModified, "fold does not create undo entries or edited/modified state: undo=" + b.CanUndo + " edited=" + Field(v, "edited") + " modified=" + b.Modified + " initial=" + initialModified);
                Check(b.BracketOpen == 0 && b.BracketClose == close, "bracket pair survives collapse");
                b.SelectAll(); Check(b.SelectedText == before, "select-all/copy source includes all folded text");
                b.Select(0, 0); Check(b.FoldCurrent(false), "root expand succeeds");
                Check(b.Text == before && b.GetPositionFromCharIndex(close).Y > b.LineHeight, "unfold restores native lines without altering text");
                Check(!b.CanUndo, "unfold does not add a formatting undo action");
                b.Select(b.TextLength, 0); b.SelectedText = "X"; Check(b.CanUndo, "real edit recorded before folding");
                b.Select(0, 0); b.FoldCurrent(true); b.FoldCurrent(false); b.Undo();
                Check(b.Text == before, "first Ctrl+Z still undoes the real text edit, not a fold");
                Check(b.CanRedo, "folding preserves redo chain"); b.FoldCurrent(true); b.Redo();
                Check(b.Text == before + "X", "redo remains functional across a fold");
            }
        }
        static void FoldingNestedTests()
        {
            using (var v = Open(FoldingInput()))
            {
                var b = Box(v); string source = b.Text;
                int first = source.IndexOf('{', 1), array = source.IndexOf('[');
                b.Select(array, 0); b.FoldCurrent(true);
                Check(FoldFor(b, array).Collapsed, "inner array folded");
                b.Select(first, 0); b.FoldCurrent(true);
                Check(FoldFor(b, first).Collapsed && FoldFor(b, array).Collapsed && b.Folding.Hidden.Count == 1, "parent hides child but remembers its collapsed state");
                b.FoldCurrent(false);
                Check(!FoldFor(b, first).Collapsed && FoldFor(b, array).Collapsed && b.Folding.Hidden.Count == 1, "expanding parent leaves previously folded child closed");
                var r = FoldFor(b, array); var badge = b.Folding.Placeholder(r);
                Check(!badge.IsEmpty && badge.Width >= 10 && badge.Height == b.LineHeight, "inline folded block has a clickable placeholder");
                Point point = new Point(badge.X + badge.Width / 2, badge.Y + badge.Height / 2);
                IntPtr lp = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
                Native.SendMessage(b.Handle, 0x0201, (IntPtr)1, lp); Native.SendMessage(b.Handle, 0x0202, IntPtr.Zero, lp);
                Check(!r.Collapsed && b.CollapsedCount == 0 && b.Text == source, "placeholder click expands exactly that block");
                b.FoldAll(true); Check(b.CollapsedCount == b.Folding.Regions.Count && b.Folding.Hidden.Count == 1, "fold-all stores all levels but hides only outermost ranges");
                b.Select(0, 0); b.FoldCurrent(false);
                Check(!FoldFor(b, 0).Collapsed && b.CollapsedCount > 0, "one-level expansion after fold-all");
                b.FoldAll(false); Check(b.CollapsedCount == 0 && b.Text == source && !b.CanUndo, "expand-all restores all data and no undo noise");
            }
        }
        static void FoldingControlsTests()
        {
            using (var v = Open(FoldingInput()))
            {
                var b = Box(v); var gutter = b.FoldingGutter;
                Check(gutter.Parent == b.Parent && gutter.Width >= 18, "dedicated left folding gutter installed");
                using (var bitmap = new Bitmap(gutter.Width, gutter.Height)) gutter.DrawToBitmap(bitmap, gutter.ClientRectangle);
                Point p = b.GetPositionFromCharIndex(0);
                Call(gutter, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, gutter.Width / 2, p.Y + b.LineHeight / 2, 0));
                Check(FoldFor(b, 0).Collapsed, "gutter minus collapses root");
                using (var bitmap = new Bitmap(gutter.Width, gutter.Height)) gutter.DrawToBitmap(bitmap, gutter.ClientRectangle);
                Call(gutter, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, gutter.Width / 2, p.Y + b.LineHeight / 2, 0));
                Check(!FoldFor(b, 0).Collapsed, "gutter plus expands root");
                b.Focus(); b.Select(0, 0);
                object[] args = { new Message(), Keys.Control | Keys.Shift | Keys.OemOpenBrackets };
                Check((bool)Call(v, "ProcessCmdKey", args) && FoldFor(b, 0).Collapsed, "Ctrl+Shift+[ folds current block");
                args[1] = Keys.Control | Keys.Shift | Keys.Oem6;
                Check((bool)Call(v, "ProcessCmdKey", args) && !FoldFor(b, 0).Collapsed, "Ctrl+Shift+] expands current block");
                args[1] = Keys.Control | Keys.Alt | Keys.OemOpenBrackets;
                Check((bool)Call(v, "ProcessCmdKey", args) && b.CollapsedCount == b.Folding.Regions.Count, "Ctrl+Alt+[ folds all");
                args[1] = Keys.Control | Keys.Alt | Keys.Oem6;
                Check((bool)Call(v, "ProcessCmdKey", args) && b.CollapsedCount == 0, "Ctrl+Alt+] expands all");
                int array = b.Text.IndexOf('['); b.Select(0, 0); Call(v, "SetFold", true, false, array);
                Check(FoldFor(b, array).Collapsed && !FoldFor(b, 0).Collapsed, "context command uses clicked block, not previous caret");
            }
        }
        static void FoldingEditingTests()
        {
            using (var v = Open(FoldingInput()))
            {
                var b = Box(v); string before = b.Text;
                int first = before.IndexOf('{', 1), second = before.IndexOf('{', before.IndexOf("\"second\"", StringComparison.Ordinal));
                b.Select(second, 0); b.FoldCurrent(true);
                int value = b.Text.IndexOf("1", StringComparison.Ordinal);
                b.Select(value, 0); b.SelectedText = "9";
                Check(FoldFor(b, second + 1).Collapsed, "editing outside a folded sibling shifts and preserves the sibling");
                b.Undo(); Check(b.Text == before && FoldFor(b, second).Collapsed, "undo repositions collapsed sibling correctly");
                b.Select(first, 0); b.FoldCurrent(true);
                int hidden = b.Text.IndexOf("value", StringComparison.Ordinal) + 1;
                b.RevealRange(hidden, 0); b.Select(hidden, 0); b.SelectedText = "X";
                Check(!FoldFor(b, first).Collapsed && b.Text.Contains("vXalue"), "navigation reveals hidden range before inserting at its actual source index");
                b.Undo(); Check(b.Text == before, "hidden-range insertion undo preserves source");
                b.FoldAll(false); int array = b.Text.IndexOf('['); b.Select(array, 0); b.FoldCurrent(true);
                int arrayClose = FoldFor(b, array).Close;
                b.Select(array + 1, 0); Native.SendMessage(b.Handle, 0x0100, (IntPtr)Keys.Delete, IntPtr.Zero);
                Check(!FoldFor(b, array).Collapsed, "Delete at folded interior expands before editing");
                // 使用 WM_CHAR 检验真实 RichEdit 输入路径，而不触碰用户的剪贴板。
                b.Select(array, 0); b.FoldCurrent(true); b.Select(arrayClose, 0);
                Native.SendMessage(b.Handle, 0x0102, (IntPtr)8, IntPtr.Zero);
                Check(b.CollapsedCount <= 1 && b.Text.Contains("second"), "Backspace does not silently delete the whole hidden document");
                b.FoldAll(false); b.Select(0, 0); b.FoldCurrent(true); b.Select(2, 0);
                Native.SendMessage(b.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero);
                Check(!FoldFor(b, 0).Collapsed, "IME composition reveals the target first");
            }
        }
        static void FoldingNavigationTests()
        {
            using (var v = Open(FoldingInput()))
            {
                var b = Box(v); b.Select(0, 0); b.FoldCurrent(true);
                var search = (ToolStripTextBox)Field(v, "search"); search.Text = "value"; Call(v, "UpdateMatches", true);
                Check(b.SelectedText == "value" && !b.Folding.IsHidden(b.SelectionStart), "search reveals folded ancestors for current match");
                Check(b.CollapsedCount == 0, "root auto-expanded to show search result");
                search.Text = ""; Call(v, "UpdateMatches", false); b.Select(0, 0); b.FoldAll(true);
                JNode dom = Dom.Parse(b.Text, 0); JNode target = dom.Children[1];
                // OnTreePick 会使用已缓存 DOM 的版本；先通过正常入口准备缓存。
                Call(v, "GetDom"); Call(v, "OnTreePick", target);
                Check(!b.Folding.IsHidden(target.Start) && b.SelectedText.Contains("second"), "tree navigation reveals the selected folded member");
            }
        }
        static void FoldingTransformTests()
        {
            using (var v = Open(NestedInput()))
            {
                var b = Box(v); Call(v, "Reprocess", false, true); string expanded = b.Text;
                int array = expanded.IndexOf('['); b.Select(array, 0); b.FoldCurrent(true);
                Check(FoldFor(b, array).Collapsed, "expanded nested JSON supports folding");
                ViewSettings.Dark = !ViewSettings.Dark; ViewSettings.FontSize = 14; Call(v, "Restyle");
                Check(b.Text == expanded && FoldFor(b, array).Collapsed, "theme/font change preserves fold state");
                ViewSettings.FontSize = 10.5f; ViewSettings.Dark = false; Call(v, "Restyle");
                b.WordWrap = false; Check(FoldFor(b, array).Collapsed, "wrap toggle retains folding"); b.WordWrap = true;
                var mask = (ToolStripButton)Field(v, "btnMask"); mask.Checked = true; b.Select(0, 0); b.FoldCurrent(true);
                Check(b.ReadOnly && b.CollapsedCount > 0, "read-only masked text still folds"); mask.Checked = false;
                Check(b.Text == expanded, "unmask returns complete real text, not collapsed view");
                b.Select(0, 0); b.FoldCurrent(true); Call(v, "Reprocess", true, false);
                Check(b.Text.Length < expanded.Length && !b.Text.Contains("\n"), "minify uses complete hidden source");
                Call(v, "Undo"); Check(b.Text == expanded, "undo format restores entire expanded JSON");
            }
            using (var v = Open("prefix {\"a\":[1,2]} middle {\"b\":[3,4]} suffix"))
            {
                var b = Box(v); string full = b.Text;
                b.FoldAll(true); var only = (ToolStripButton)Field(v, "btnOnly"); only.Checked = true;
                Check(!b.Text.Contains("prefix") && b.Text.Contains("JSON"), "JSON-only view still loads full fragments after folding");
                b.FoldAll(true); only.Checked = false;
                Check(b.Text == full && b.Text.Contains("suffix"), "JSON-only merge never loses folded data or log text");
            }
        }
        static void FoldingLargeTests()
        {
            string input = "{\"long\":\"" + new string('x', 160000) + "\",\"tail\":[{\"n\":1},{\"n\":2}]}";
            using (var v = Open(input))
            {
                var b = Box(v); string full = b.Text; int close = full.LastIndexOf('}');
                b.Select(0, 0); b.FoldCurrent(true);
                Check(b.GetPositionFromCharIndex(close).Y == b.GetPositionFromCharIndex(0).Y, "large wrapped content really collapses");
                Call(v, "ColorVisible");
                Check(b.Text == full && FoldFor(b, 0).Collapsed && b.BracketOpen == 0, "lazy syntax coloring does not destroy fold or bracket state");
                b.FoldAll(false); Check(b.Text == full && b.GetPositionFromCharIndex(close).Y > b.LineHeight, "large document unfolds completely");
                b.SelectAll(); Check(b.SelectedText == full, "large fold/unfold preserves copy text");
                b.Select(b.TextLength, 0); b.SelectedText = "X";
                b.Select(0, 0); b.FoldCurrent(true); Call(v, "ColorVisible"); b.FoldAll(false); Call(v, "ColorVisible");
                b.Undo(); Check(b.Text == full, "large-file coloring while folding never inserts formatting steps into undo");
            }
        }

        static void NativeLayoutTests()
        {
            string input = "{\"unicode\":\"中文 😀 é \\\"[]{}\\\" " + new string('x', 70000) + "\",\"items\":[{\"n\":1},{\"n\":2}]}";
            using (var v = Open(input))
            {
                var b = Box(v); string text = b.Text;
                Check(b.DocumentLength == text.Length, "cached render length uses normalized editor indices");
                var indices = new List<int> { 0, 1, text.IndexOf("中文", StringComparison.Ordinal), text.IndexOf("😀", StringComparison.Ordinal), text.Length - 1, text.Length };
                for (int i = 0; i < 40; i++) indices.Add((int)((long)i * (text.Length - 1) / 39));
                foreach (int index in indices) Check(b.CharPosition(index) == b.GetPositionFromCharIndex(index), "native position must match Framework at UTF-16 index " + index);
                foreach (var point in new[] { Point.Empty, new Point(10, 20), new Point(100, 100), new Point(b.ClientSize.Width - 1, b.ClientSize.Height - 1) })
                    Check(b.CharIndexAt(point) == b.GetCharIndexFromPosition(point), "native hit testing must match Framework");
                int array = text.LastIndexOf('['); b.RevealRange(array, 0); b.Select(array, 0); b.FoldCurrent(true);
                int close = FoldFor(b, array).Close;
                Check(b.CharPosition(close) == b.GetPositionFromCharIndex(close), "layout remains correct for hidden ranges");
                b.Select(b.TextLength, 0); b.SelectedText = "\n";
                Check(b.DocumentLength == b.TextLength, "render length updates after text edit");
            }
        }
        static void SyntaxIndexTests()
        {
            string text = "{\n  \"long\": \"" + new string('x', 18000) + "\",\n  \"number\": 123,\n  \"flag\": true\n}";
            var index = new SyntaxIndex(text, JsonTools.FindJsonRanges(text));
            int from = text.IndexOf('x') + 12000, to = from + 500, count = 0;
            index.Visit(from, to, (start, length, color) => { Check(start == from && length == 500 && color == 4, "mid-string blocks keep string color"); count++; });
            Check(count == 1, "long string indexed as one clipped token, not rescanned characters");
            int resume = 0, visits = 0;
            while (resume < text.Length)
            {
                int before = resume;
                resume = index.VisitUntil(resume, text.Length, (start, length, color) => { visits++; return false; });
                Check(resume > before, "budgeted coloring always makes forward progress");
            }
            Check(visits > 5 && visits < 30, "budget can resume without repeating long prefixes");
            int number = text.IndexOf("123", StringComparison.Ordinal);
            index.Visit(number, number + 3, (start, length, color) => Check(start == number && length == 3 && color == 5, "number indexed with exact LF offsets"));
        }
        static void StableColoringTests()
        {
            string input = "{\"n\":123,\"flag\":true,\"word\":\"中文😀\",\"padding\":\"" + new string('x', 160000) + "\"}";
            using (var v = Open(input))
            {
                var b = Box(v); string text = b.Text; b.Select(0, 0); Call(v, "GetDom");
                object dom = Field(v, "domRoots"), bracket = Field(v, "bracketIndex"); int version = (int)Field(v, "textVersion");
                int selectionEvents = 0; b.SelectionChanged += delegate { selectionEvents++; };
                ((HashSet<int>)Field(v, "coloredBlocks")).Clear(); ((Dictionary<int, int>)Field(v, "colorProgress")).Clear();
                bool modified = b.Modified;
                for (int i = 0; i < 20; i++) Call(v, "ColorVisible");
                Check(selectionEvents == 0 && b.SelectionStart == 0 && b.SelectionLength == 0, "syntax coloring never moves caret or selection");
                Check((int)Field(v, "textVersion") == version, "formatting is not mistaken for text edits");
                Check(ReferenceEquals(dom, Field(v, "domRoots")) && ReferenceEquals(bracket, Field(v, "bracketIndex")), "scroll coloring keeps structure and bracket caches alive");
                Check(b.Text == text && b.Modified == modified && !b.CanUndo, "color pass preserves text, modified state and undo");
                int number = text.IndexOf("123", StringComparison.Ordinal); b.Select(number, 3);
                Check(b.SelectionColor.ToArgb() == Theme.Current.Syntax[5].ToArgb(), "non-selection native range correctly colors numbers");
                int word = text.IndexOf("中文", StringComparison.Ordinal); b.Select(word, 2);
                Check(b.SelectionColor.ToArgb() == Theme.Current.Syntax[4].ToArgb(), "native ranges respect Unicode and normalized newlines");
                b.ReadOnly = true; int readOnlyEvents = 0; b.ReadOnlyChanged += delegate { readOnlyEvents++; }; ((HashSet<int>)Field(v, "coloredBlocks")).Clear(); ((Dictionary<int, int>)Field(v, "colorProgress")).Clear(); Call(v, "ColorVisible");
                Check(b.ReadOnly && b.Text == text, "read-only large views can finish pending color slices");
                Check(readOnlyEvents == 0, "color batches never toggle the public read-only state"); b.ReadOnly = false;
                b.Select(text.Length, 0); b.SelectedText = "X";
                int editedVersion = (int)Field(v, "textVersion"); Call(v, "ColorVisible");
                Check((int)Field(v, "textVersion") == editedVersion, "recoloring after an edit leaves new text version intact");
                b.Undo(); Check(b.Text == text && b.CanRedo, "coloring never replaces text undo/redo entries");
                b.Select(0, 0);
                using (var colors = RichEditFold.Colors(b)) colors.Set(number, 3, Theme.Current.Syntax[5]);
                Check(b.SelectionStart == 0 && b.SelectionLength == 0, "TOM color application itself does not select the target");
            }
        }
        static void BoundedHighlightTests()
        {
            using (var v = Open("{\"long\":\"" + new string('x', 180000) + "\"}"))
            {
                var b = Box(v); int start = b.Text.IndexOf('x');
                b.Highlights = new List<int[]> { new[] { start, 180000 } };
                Native.SendMessage(b.Handle, 0x00B6, IntPtr.Zero, (IntPtr)500);
                var watch = Stopwatch.StartNew(); var rectangles = b.HighlightRectangles(b.ClientRectangle); long elapsed = watch.ElapsedMilliseconds;
                Check(rectangles.Count > 0 && rectangles.Count <= b.ClientSize.Height / b.LineHeight + 4, "long match is bounded by visible display rows");
                foreach (var rectangle in rectangles) Check(b.ClientRectangle.Contains(rectangle), "no highlight rectangle extends off screen");
                var clip = new Rectangle(0, b.ClientSize.Height / 3, b.ClientSize.Width, b.ClientSize.Height / 3);
                foreach (var rectangle in b.HighlightRectangles(clip)) Check(clip.Contains(rectangle), "partial invalidation only repaints clipped rows");
                Check(elapsed < 500, "visible highlight geometry must not scale with the full long match");
                Console.WriteLine("  long-match visible geometry: " + elapsed + " ms, " + rectangles.Count + " rows");
            }
        }
        static void SmoothScrollTests()
        {
            var json = new StringBuilder("{\"items\":[");
            for (int i = 0; i < 1800; i++) { if (i > 0) json.Append(','); json.Append("{\"id\":").Append(i).Append(",\"value\":[1,2,3]}"); }
            json.Append("]}");
            using (var v = Open(json.ToString()))
            {
                var b = Box(v); b.StopSmooth(); Call(b, "OnViewportChanged");
                int events = 0; b.ViewportChanged += delegate { events++; };
                b.ScrollPos = b.ScrollPos; Call(b, "OnViewportChanged");
                Check(events == 0, "unchanged viewport does not issue fake scroll events");
                int before = (int)Native.SendMessage(b.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
                Native.SendMessage(b.Handle, 0x00B6, IntPtr.Zero, (IntPtr)3); Call(b, "OnViewportChanged");
                int after = (int)Native.SendMessage(b.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
                Check(after > before && events == 1, "real native movement emits exactly one viewport notification");
                Call(b, "OnViewportChanged"); Check(events == 1, "duplicate native notifications are coalesced");
                if (SystemInformation.MouseWheelScrollLines != 0)
                {
                    before = after; Call(b, "Wheel", -120);
                    Check(b.IsSmoothScrolling && b.ScrollPos.Y > 0, "very tall files start moving immediately in real pixels");
                    var timeout = Stopwatch.StartNew();
                    while (b.IsSmoothScrolling && timeout.ElapsedMilliseconds < 1500) { System.Threading.Thread.Sleep(16); Application.DoEvents(); }
                    after = (int)Native.SendMessage(b.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
                    Check(!b.IsSmoothScrolling && after > before, "long-file wheel animation finishes with actual viewport movement");
                    Call(b, "Wheel", -120); b.StopSmooth(); Check(!b.IsSmoothScrolling, "navigation can immediately stop wheel animation");
                }
            }
        }
        static void ScopeGuideTests()
        {
            var json = new StringBuilder("{\"items\":[");
            for (int i = 0; i < 150; i++) { if (i > 0) json.Append(','); json.Append("{\"n\":").Append(i).Append('}'); } json.Append("]}");
            using (var v = Open(json.ToString()))
            {
                var b = Box(v); b.Select(0, 0); b.ScrollPos = Point.Empty;
                CodeBox.ScopeGuide guide;
                Check(b.TryGetScopeGuide(out guide), "multiline matched brackets produce a scope guide");
                Check(guide.Top >= 0 && guide.Bottom == b.ClientSize.Height - 1 && guide.OpenY >= 0, "off-screen closing bracket clips the guide to the viewport");
                using (var bitmap = new Bitmap(b.FoldingGutter.Width, b.FoldingGutter.Height))
                {
                    b.FoldingGutter.DrawToBitmap(bitmap, b.FoldingGutter.ClientRectangle);
                    Check(bitmap.GetPixel(bitmap.Width - 3, Math.Min(bitmap.Height - 2, guide.Top + 20)).ToArgb() == Theme.Current.Accent.ToArgb(), "left gutter paints the selected scope line");
                }
                Native.SendMessage(b.Handle, 0x00B6, IntPtr.Zero, (IntPtr)80);
                Check(b.TryGetScopeGuide(out guide) && guide.Top == 0 && guide.Bottom == b.ClientSize.Height - 1,
                    "range line remains visible when both brackets are outside the screen");
                b.Select(0, 0); b.ScrollPos = Point.Empty; b.FoldCurrent(true);
                Check(!b.TryGetScopeGuide(out guide), "folded same-row brackets do not leave a stale vertical line");
                b.FoldAll(false); b.Select(0, 0);
                int height = b.LineHeight, width = b.CharacterWidth;
                ViewSettings.FontSize = 18; Call(v, "Restyle");
                Check(b.LineHeight > height && b.CharacterWidth > width && b.TryGetScopeGuide(out guide), "zoom refreshes cached geometry and guide length");
                ViewSettings.FontSize = 10.5f; Call(v, "Restyle");
                b.Select(b.Text.IndexOf("items", StringComparison.Ordinal), 0);
                Check(!b.TryGetScopeGuide(out guide), "leaving a bracket clears its guide");
            }
        }

        static void DrainScroll(CodeBox box)
        {
            var timeout = Stopwatch.StartNew();
            while (box.IsSmoothScrolling && timeout.ElapsedMilliseconds < 1500)
            { System.Threading.Thread.Sleep(4); Application.DoEvents(); }
            Check(!box.IsSmoothScrolling, "pixel animation must settle instead of leaving a perpetual timer");
        }
        static void FluidScrollingTests()
        {
            var source = new StringBuilder("{\"items\":[");
            for (int i = 0; i < 3200; i++) { if (i > 0) source.Append(','); source.Append("{\"id\":").Append(i).Append(",\"value\":[1,2,3]}"); } source.Append("]}");
            using (var viewer = Open(source.ToString()))
            {
                var box = Box(viewer); string original = box.Text;
                Check(Native.ClassName(box.Handle).Contains("RichEdit"), "pixel scrolling retains the compatible Windows editing control");
                box.Select(0, 0); box.ScrollPos = new Point(0, 70003);
                Check(box.ScrollPos.Y == 70003, "large document scroll coordinates must not be scaled into 16 bits");
                var previous = box.CharPosition(0);
                for (int i = 1; i <= 8; i++)
                {
                    box.ScrollPos = new Point(0, 70003 + i);
                    Point next = box.CharPosition(0);
                    Check(box.ScrollPos.Y == 70003 + i && previous.Y - next.Y == 1, "one pixel request must really move one pixel, not jump a row");
                    previous = next;
                }
                if (SystemInformation.MouseWheelScrollLines > 0)
                {
                    var positions = new List<int>(); box.ViewportChanged += delegate { positions.Add(box.ScrollPos.Y); };
                    int start = box.ScrollPos.Y, selections = 0; box.SelectionChanged += delegate { selections++; };
                    int version = (int)Field(viewer, "textVersion");
                    var response = Stopwatch.StartNew(); Call(box, "Wheel", -120);
                    int immediate = box.ScrollPos.Y;
                    Check(immediate > start && immediate < start + SystemInformation.MouseWheelScrollLines * box.LineHeight,
                        "first visible motion occurs during wheel input and is smaller than a full tick");
                    Check(box.AnimationInterval >= 7 && box.AnimationInterval <= 16, "frame clock adapts to supported refresh intervals");
                    DrainScroll(box);
                    int distance = SystemInformation.MouseWheelScrollLines * box.LineHeight;
                    Check(Math.Abs(box.ScrollPos.Y - start - distance) <= 1, "pixel smoothing preserves configured wheel distance");
                    Check(positions.Count >= 3, "a single tick must have multiple real intermediate positions");
                    bool partial = false; int last = start;
                    foreach (int position in positions) { Check(position >= last, "single-direction motion is monotonic"); if ((position - start) % box.LineHeight != 0) partial = true; last = position; }
                    Check(partial, "long files have sub-line intermediate frames, not only whole-row steps");
                    Check(selections == 0 && (int)Field(viewer, "textVersion") == version && box.Text == original,
                        "animation never changes caret, text or structure version");
                    Console.WriteLine("  wheel intermediate positions: " + positions.Count + ", frame interval: " + box.AnimationInterval + " ms");
                    start = box.ScrollPos.Y; Call(box, "Wheel", -120); int forward = box.ScrollPos.Y;
                    Call(box, "Wheel", 120); int reversed = box.ScrollPos.Y;
                    Check(reversed < forward, "opposite wheel input immediately moves backwards, without old-direction drift");
                    DrainScroll(box);
                    Check(Math.Abs(box.ScrollPos.Y - start) <= 1, "equal quick down/up ticks return to the original position");
                    Call(box, "Wheel", -120); var driver = Field(box, "anim");
                    System.Threading.Thread.Sleep(65); var run = Field(driver, "run");
                    Check(run != null && (int)run.GetType().GetField("Pending").GetValue(run) <= 1, "busy UI queues at most one frame, never a backlog of timer callbacks");
                    box.StopSmooth(); Application.DoEvents(); int stopped = box.ScrollPos.Y;
                    System.Threading.Thread.Sleep(30); Application.DoEvents();
                    Check(box.ScrollPos.Y == stopped && !box.IsSmoothScrolling, "cancelled and stale frame messages cannot keep moving the document");
                }
            }
        }
        static void HiddenTextCompatibilityTests()
        {
            string input = "{\"unicode\":\"中文 😀 é\",\"items\":[{\"text\":\"甲\\n乙\"},{\"text\":\"尾部\"}]}";
            using (var viewer = Open(input))
            {
                var box = Box(viewer); string full = box.Text;
                box.Select(0, 0); box.FoldAll(true);
                Check(box.Text == full && box.TextLength == full.Length, "full Unicode source includes hidden text during pixel scrolling");
                box.SelectAll(); Check(box.SelectedText == full, "selection copy includes hidden Unicode paragraphs");
                box.FoldAll(false);
                int start = full.IndexOf("中文", StringComparison.Ordinal); box.Select(start, "中文 😀".Length);
                Check(box.SelectedText == "中文 😀", "UTF-16 selection boundaries do not split or lose supplementary characters");
                box.Select(full.Length, 0); box.SelectedText = "\n";
                Check(box.Text == full + "\n", "final newline survives native text retrieval");
                box.Undo(); Check(box.Text == full, "pixel scrolling preserves native text undo after folding and copy");
                box.Select(0, 0); box.FoldCurrent(true); string before = box.Text;
                ViewSettings.Dark = true; Call(viewer, "Restyle");
                Check(box.Text == before && box.CollapsedCount > 0, "restyle and native hidden-text streaming do not truncate the document");
                ViewSettings.Dark = false;
            }
        }

        static void LargeEditorTests()
        {
            string input = "{\"long\":\"" + new string('x', 150000) + "\",\"tail\":[{}]}";
            using (var v = Open(input))
            {
                var b = Box(v); Check((bool)Field(v, "large"), "large-file mode activated"); VerifyRoot(v);
                int open = b.Text.LastIndexOf('['), close = b.Text.LastIndexOf(']');
                b.Select(open, 0); b.ScrollToCaret(); Call(v, "ColorVisible");
                Check(b.BracketOpen == open && b.BracketClose == close && b.SelectionStart == open && b.SelectionLength == 0,
                    "visible syntax coloring preserves pair and caret");
            }
        }
    }
}
