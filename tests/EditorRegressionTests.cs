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
                Run("selection detection: strict evidence, games/canvas, password and 32-bit ranges", StrictSelectionTests);
                Run("selection detection: bounded workers, errors and MTA isolation", SelectionProbeConcurrencyTests);
                Run("selection compatibility: IA2 ranges, object samples, hit geometry and fresh evidence", SelectionCompatibilityTests);
                Run("selection input: full quoted/escaped JSON and nested-field preservation", SelectionInputTests);
                Run("selection input: open ANY selected text, preserve invalid input and retry", AnySelectionViewerTests);
                Run("button capture: read before Esc, complete original text and visible viewer", DirectButtonCaptureTests);
                Run("parser: whole map dump, bare UID/IP/package values and type preservation", BareMapInputTests);
                Run("parser: map scalar boundaries, strict mode, comments and roundtrips", MapScalarBoundaryTests);
                Run("editor: map normalization, tree/folding and minify undo", MapViewerTests);
                Run("selection capture: delayed formats, busy clipboard and version races", SelectionCaptureTests);
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
                Run("scope guides: caret on both sides, mouse-up and nested expansion", AdjacentCaretGuideTests);
                Run("fluid scrolling: high precision, immediate response, reversal and bounded queue", FluidScrollingTests);
                Run("pixel scrolling: hidden Unicode text, selections and mutation safety", HiddenTextCompatibilityTests);
                Run("native frame clock: restart, cancellation, disposal and deferred work", FrameLifecycleTests);
                Run("inactive scrolling: focus/IME queries preserve the browsed viewport", InactiveScrollActivationTests);
                Run("neon caret: XOR colors, contrast and bounded glow geometry", NeonCaretVisualTests);
                Run("neon caret: native focus, input/undo, selection, theme, zoom and toggle", NeonCaretNativeTests);
                Run("neon caret: cached bitmap and GDI ownership lifecycle", NeonCaretResourceTests);
                Run("purple trail: real key movement, idle stop, interruption and disposal", RainbowCaretNativeTests);
                Console.WriteLine("ALL PASSED: " + checks + " assertions");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex); return 1; }
        }

        static T WaitForProbe<T>(System.Threading.Tasks.Task<T> task)
        {
            var timeout = Stopwatch.StartNew();
            while (!task.IsCompleted && timeout.ElapsedMilliseconds < 4000)
            { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
            Check(task.IsCompleted, "selection read completes without blocking the UI thread");
            return task.GetAwaiter().GetResult();
        }
        static void StrictSelectionTests()
        {
            Check(SelectionProbe.ShouldShow(Sel.Yes), "only a confirmed text selection can show the floating button");
            Check(!SelectionProbe.ShouldShow(Sel.No) && !SelectionProbe.ShouldShow(Sel.Unknown),
                "game drags/double-clicks/Ctrl+A and clipboard changes cannot turn unknown/no into yes");
            Check(!SelectionProbe.NonEmptyTextRange(0, "x") && !SelectionProbe.NonEmptyTextRange(1, "x"), "a caret or reversed range is not selected text");
            Check(!SelectionProbe.NonEmptyTextRange(-1, null) && !SelectionProbe.NonEmptyTextRange(-1, ""), "empty ranges never count as text");
            Check(!SelectionProbe.NonEmptyTextRange(-1, "\uFFFC"), "a selected image/object is not selected text");
            Check(SelectionProbe.NonEmptyTextRange(-1, "中") && SelectionProbe.NonEmptyTextRange(-1, " "), "real Unicode and whitespace selections count");
            using (var host = new Form { Size = new Size(650, 350), ShowInTaskbar = false })
            using (var edit = new TextBox { Multiline = true, Bounds = new Rectangle(10, 10, 280, 100), MaxLength = int.MaxValue })
            using (var rich = new RichTextBox { Bounds = new Rectangle(300, 10, 280, 100), MaxLength = int.MaxValue })
            using (var password = new TextBox { Bounds = new Rectangle(10, 130, 250, 24), UseSystemPasswordChar = true, Text = "synthetic only" })
            using (var canvas = new Panel { Bounds = new Rectangle(10, 180, 500, 100) })
            {
                host.Controls.AddRange(new Control[] { edit, rich, password, canvas }); host.Show(); Application.DoEvents();
                edit.Text = "plain 中文 selection"; rich.Text = "rich 中文 selection";
                IntPtr editHandle = edit.Handle, richHandle = rich.Handle, passwordHandle = password.Handle, canvasHandle = canvas.Handle;
                edit.Select(2, 0); rich.Select(3, 0);
                Check(WaitForProbe(System.Threading.Tasks.Task.Run(() => SelectionProbe.NativeSelection(editHandle))) == Sel.No,
                    "ordinary insertion caret in a native edit control does not show the button");
                edit.Select(0, 5); rich.Select(5, 2); password.SelectAll();
                Check(WaitForProbe(System.Threading.Tasks.Task.Run(() => SelectionProbe.NativeSelection(editHandle))) == Sel.Yes, "native Edit nonempty text selection is confirmed");
                Check(WaitForProbe(System.Threading.Tasks.Task.Run(() => SelectionProbe.NativeSelection(richHandle))) == Sel.Yes, "RichEdit nonempty text selection is confirmed");
                Check(WaitForProbe(System.Threading.Tasks.Task.Run(() => SelectionProbe.NativeSelection(passwordHandle))) == Sel.No,
                    "a password field is excluded even when its characters are selected");
                Check(WaitForProbe(System.Threading.Tasks.Task.Run(() => SelectionProbe.NativeSelection(canvasHandle))) == Sel.Unknown,
                    "a game-like canvas never receives editor selection messages");
                Point point = canvas.PointToScreen(new Point(10, 10));
                var target = new SelectionTarget(host.Handle, canvasHandle, point);
                var canvasProbe = SelectionProbe.Begin(target);
                // 实际托盘只给提供程序有限等待时间。自绘画布的 UIA/MSAA 提供程序可能
                // 永远不返回，回归测试也应验证超时不弹出，而不是要求外部接口一定完成。
                var completed = WaitForProbe(System.Threading.Tasks.Task.WhenAny(canvasProbe, System.Threading.Tasks.Task.Delay(350)));
                Sel canvasSelection = completed == canvasProbe ? canvasProbe.GetAwaiter().GetResult() : Sel.Unknown;
                Check(!SelectionProbe.ShouldShow(canvasSelection),
                    "selected text in a sibling editor cannot make a right-click on the canvas eligible, including provider timeouts");
                foreach (var control in new TextBoxBase[] { edit, rich })
                {
                    control.Text = new string('x', 80000) + "中文"; control.Select(70000, 0); IntPtr handle = control.Handle;
                    Check(WaitForProbe(System.Threading.Tasks.Task.Run(() => SelectionProbe.NativeSelection(handle))) == Sel.No,
                        "a caret above 65535 chars does not look like a selection");
                    control.Select(70000, 12);
                    Check(WaitForProbe(System.Threading.Tasks.Task.Run(() => SelectionProbe.NativeSelection(handle))) == Sel.Yes,
                        "full 32-bit native selection endpoints preserve long-document selection");
                }
                Check(!new SelectionTarget(IntPtr.Zero, editHandle, point).IsCurrent, "invalid target roots are rejected");
                Check(!new SelectionTarget(host.Handle, IntPtr.Zero, point).IsCurrent, "destroyed/missing targets are rejected");
            }
        }
        static void SelectionCompatibilityTests()
        {
            Check(SelectionProbe.NativeTextClass("Edit") && SelectionProbe.NativeTextClass("WindowsForms10.RichEdit20W.app") && SelectionProbe.NativeTextClass("Scintilla"),
                "known native text controls receive an after-menu recheck instead of stale early proof");
            Check(!SelectionProbe.NativeTextClass("Chrome_RenderWidgetHostHWND") && !SelectionProbe.NativeTextClass("RichEditD2DPT") && !SelectionProbe.NativeTextClass("UnrealWindow"),
                "browsers and modern virtual text controls use accessibility providers; games are not treated as edit controls");
            Check(SelectionProbe.NonEmptyTextRange(-1, "\uFFFC中文"), "an embedded object before real selected text cannot hide that text");
            Check(!SelectionProbe.NonEmptyTextRange(-1, "\uFFFC\uFFFC\0"), "only image/object placeholders still do not count as text");
            Check(!LegacyTextSelection.ValidOffsets(-1, 12) && !LegacyTextSelection.ValidOffsets(12, -1), "IA2 special/missing offsets are rejected");
            Check(!LegacyTextSelection.ValidOffsets(12, 12), "IA2 insertion caret is not a nonempty selection");
            Check(LegacyTextSelection.ValidOffsets(10, 20) && LegacyTextSelection.ValidOffsets(20, 10), "forward and reversed IA2 selection endpoints are supported");
            Check(LegacyTextSelection.OffsetWithinSelection(10, 20, 15) && LegacyTextSelection.OffsetWithinSelection(20, 10, 15), "IA2 click falls inside either selection direction");
            Check(!LegacyTextSelection.OffsetWithinSelection(10, 20, 35) && !LegacyTextSelection.OffsetWithinSelection(10, 20, -1),
                "a selection elsewhere in the document cannot make canvas/image clicks eligible");
            Check(LegacyTextSelection.OffsetWithinSelection(int.MaxValue - 8, int.MaxValue, int.MaxValue - 4), "32-bit IA2 offsets never overflow while checking the hit");
            var rectangles = new[] { new System.Windows.Rect(20, 30, 80, 18), new System.Windows.Rect(20, 48, 150, 18) };
            Check(SelectionProbe.NearSelection(rectangles, new Point(30, 35)) && SelectionProbe.NearSelection(rectangles, new Point(160, 55)), "selected text can wrap over multiple visual rows");
            Check(!SelectionProbe.NearSelection(rectangles, new Point(50, 250)), "a distant canvas is excluded even when its parent document has a real selection");
            Check(SelectionProbe.NearSelection(rectangles, new Point(103, 35)), "small pointer/glyph boundary differences do not drop a real selection");
            Check(SelectionProbe.NearSelection(new[] { new System.Windows.Rect(-1800, -120, 90, 24) }, new Point(-1750, -110)), "negative monitor coordinates are supported");
            Check(SelectionProbe.ContainsTextHit(-1, 1) && SelectionProbe.ContainsTextHit(0, 1), "UIA point ranges locate a hit when visible rectangles are unavailable");
            Check(!SelectionProbe.ContainsTextHit(1, 2) && !SelectionProbe.ContainsTextHit(-2, -1) && !SelectionProbe.ContainsTextHit(-1, 0),
                "a missing rectangle does not allow a distant or just-past-end document selection to match");
            Check(SelectionProbe.NearSelection(null, Point.Empty) && SelectionProbe.NearSelection(new System.Windows.Rect[0], Point.Empty),
                "a provider without range geometry may still prove actual selected text");
            Check(!SelectionProbe.NearSelection(new[] { System.Windows.Rect.Empty }, Point.Empty), "an explicitly empty visible range is not a hit");
            Check(SelectionTarget.FreshAge(0) && SelectionTarget.FreshAge(1499) && !SelectionTarget.FreshAge(1501) && !SelectionTarget.FreshAge(-1),
                "only recent same-gesture evidence may survive a context menu focus change");
            var target = new SelectionTarget(IntPtr.Zero, IntPtr.Zero, Point.Empty);
            Check(target.Fresh, "new gesture evidence uses a monotonic clock");
            target.BeforeMenu = System.Threading.Tasks.Task.FromResult(Sel.Yes);
            Check(target.BeforeMenu.Result == Sel.Yes && !target.IsCurrent, "cached proof never overrides the independent foreground/target validation");
        }

        static void SelectionProbeConcurrencyTests()
        {
            var runner = new SelectionProbeRunner();
            using (var started = new System.Threading.ManualResetEventSlim())
            using (var release = new System.Threading.ManualResetEventSlim())
            {
                int calls = 0;
                var first = runner.Run(() =>
                {
                    System.Threading.Interlocked.Increment(ref calls); started.Set();
                    release.Wait(3000); return Sel.Yes;
                });
                Check(started.Wait(1000), "first background selection probe started");
                try
                {
                    for (int i = 0; i < 80; i++)
                    {
                        var skipped = runner.Run(() => { System.Threading.Interlocked.Increment(ref calls); return Sel.Yes; });
                        Check(skipped.IsCompleted && skipped.Result == Sel.Unknown, "busy/slow providers cause fail-closed responses, never queued work");
                    }
                    Check(calls == 1 && !first.IsCompleted, "right-click spam cannot accumulate provider calls or worker threads");
                }
                finally { release.Set(); }
                Check(WaitForProbe(first) == Sel.Yes, "the original provider can still finish");
            }
            Check(WaitForProbe(runner.Run(() => { throw new InvalidOperationException("synthetic provider failure"); })) == Sel.Unknown,
                "provider errors do not escape into the tray or count as text selection");
            Check(WaitForProbe(runner.Run(() => Sel.No)) == Sel.No, "the worker slot is reusable after failure");
            Check(WaitForProbe(runner.Run(() => System.Threading.Thread.CurrentThread.GetApartmentState() == System.Threading.ApartmentState.MTA ? Sel.Yes : Sel.No)) == Sel.Yes,
                "UI Automation queries run on an MTA worker, not the WinForms STA thread");
        }


        static void AnySelectionViewerTests()
        {
            bool previous = ViewSettings.Lenient;
            try
            {
                foreach (bool lenient in new[] { false, true })
                {
                    ViewSettings.Lenient = lenient;
                    foreach (string input in new[] {
                        "普通选中文字，并不是 JSON。", "ERROR synthetic log entry: connection closed", "{", "[", "{\"value\":", "{\"value\": }",
                        "<message>ordinary text</message>", "SELECT * FROM synthetic_table", "123", "true", "\"a scalar string\"", " \t\r\n  " })
                    {
                        Check(SelectionProbe.ShouldShow(Sel.Yes) && SelectionProbe.NonEmptyTextRange(-1, input),
                            "any actual text selection is eligible regardless of JSON validity, including whitespace");
                        int ticks, reads;
                        var captured = SimulateCopy(70, n => 71u, n => input, out ticks, out reads);
                        Check(captured.Text == input, "capture does not reject or preformat plain/invalid selected text");
                        using (var viewer = TrayContext.ShowViewer(captured.Text))
                        {
                            Application.DoEvents(); var box = Box(viewer);
                            string expected = input.Replace("\r\n", "\n").Replace("\r", "\n");
                            Check(viewer.Visible && box.Text == expected && !box.ReadOnly, "shared tray/button entry opens a usable editable viewer with all original text");
                            var pieces = (List<Piece>)Field(viewer, "pieces");
                            Check(pieces.Count == 1 && !pieces[0].IsJson && pieces[0].Text == input, "zero parsed fragments retain the entire original input, not an empty document");
                            Check((string)Field(viewer, "statusKind") == "warn" && ((ToolStripStatusLabel)Field(viewer, "status")).Text.Contains(L.T("未识别到 JSON 对象或数组，已保留全部原文，可编辑后重试")),
                                "unsupported input explains that all original text was retained instead of being rejected");
                            ((ToolStripButton)Field(viewer, "btnOnly")).Checked = true;
                            Check(box.Text == expected && !(bool)Field(viewer, "shownOnly"), "JSON-only mode cannot hide an unparseable selection");
                            Call(viewer, "Reprocess", true, true);
                            Check(box.Text == expected, "format/minify/expand retry never drops an unparseable selection");
                            box.SelectAll(); Check(box.SelectedText == expected, "plain/invalid selection remains available in full for copy");
                        }
                    }
                }
                ViewSettings.Lenient = true;
                using (var viewer = TrayContext.ShowViewer("not JSON yet"))
                {
                    var box = Box(viewer); box.SelectAll(); box.SelectedText = "{ \"fixed\" : 1 }"; string edited = box.Text;
                    Call(viewer, "Reprocess", false, false);
                    Check(JsonTools.Pretty(box.Text) != null && box.Text != edited && (string)Field(viewer, "statusKind") == "ok",
                        "an initially unparseable selection can be edited into JSON and formatted without reopening");
                    Call(viewer, "Undo"); Check(box.Text == edited, "formatting after a plain-text opening remains undoable");
                }
                string longText = "synthetic plain text " + new string('x', 160000);
                using (var viewer = TrayContext.ShowViewer(longText))
                { Check(Box(viewer).Text == longText && (bool)Field(viewer, "large"), "large unparseable selections also open without truncation"); }
                using (var viewer = TrayContext.ShowViewer(null))
                { Check(viewer.Visible && Box(viewer).Text == "" && (string)Field(viewer, "statusKind") == "muted", "explicit blank-window commands still work"); }
                Check(!SelectionProbe.ShouldShow(Sel.No) && !SelectionProbe.ShouldShow(Sel.Unknown), "content-independent parsing never restores game-gesture false positives");
            }
            finally { ViewSettings.Lenient = previous; }
        }


        static void DirectButtonCaptureTests()
        {
            Check(SelectedTextReader.Accept("plain non-JSON") && SelectedTextReader.Accept(" \r\n\t") && !SelectedTextReader.Accept(null) && !SelectedTextReader.Accept(""),
                "direct selection text is accepted by presence, never by JSON syntax or trimming");
            using (var host = new Form { Size = new Size(620, 300), ShowInTaskbar = false })
            using (var editor = new TextBox { Multiline = true, MaxLength = int.MaxValue, Dock = DockStyle.Fill })
            {
                host.Controls.Add(editor); host.Show();
                foreach (string selected in new[] { "unrecognized ordinary text", "{\"broken\":", "[", "  \t\r\n ",
                    "中文 😀 first\r\nsecond line", "plain " + new string('x', 70000) })
                {
                    host.Activate(); Native.ForceForeground(host.Handle); editor.Focus(); Application.DoEvents();
                    editor.Text = "prefix--" + selected + "--suffix"; editor.Select(8, selected.Length);
                    string exact = editor.SelectedText; Point point = editor.PointToScreen(new Point(15, 15));
                    var target = SelectionTarget.Capture(host.Handle, editor.Handle, point);
                    Check(target != null && target.IsCurrent, "direct capture test uses the actual focused source control; target=" + (target != null)
                        + "; foreground=" + Native.ClassName(Native.GetForegroundWindow()) + "; sourceRoot=" + Native.Root(editor.Handle) + "; host=" + host.Handle);
                    string direct = WaitForProbe(SelectedTextReader.BeforeDismissAsync(target));
                    Check(direct == exact, "native source returns the full selected text, not a 32-character detection sample or the whole document");
                    var order = new List<string>(); JsonViewer opened = null; int copies = 0;
                    try
                    {
                        var flow = SelectedTextReader.OpenBeforeCopyAsync(
                            () => { order.Add("read"); Check(editor.SelectionLength > 0, "selection still exists when it is read"); return System.Threading.Tasks.Task.FromResult(direct); },
                            () => target.IsCurrent,
                            () => { order.Add("dismiss"); editor.Select(0, 0); return System.Threading.Tasks.Task.FromResult(0); },
                            text => { order.Add("open"); opened = TrayContext.ShowViewer(text); });
                        bool shown = WaitForProbe(flow); if (!shown) copies++;
                        Check(shown && copies == 0 && string.Join(",", order) == "read,dismiss,open", "actual button workflow opens directly and never needs a Ctrl+C fallback after Esc");
                        Check(editor.SelectionLength == 0, "source fixture simulates an application that clears selection on menu dismissal");
                        string expected = exact.Replace("\r\n", "\n").Replace("\r", "\n");
                        Check(opened != null && opened.Visible && Box(opened).Text == expected, "ordinary/invalid content really has a visible complete viewer after source selection is gone");
                    }
                    finally { if (opened != null) opened.Dispose(); }
                }
            }
            int dismissed = 0, shownCount = 0;
            var stale = SelectedTextReader.OpenBeforeCopyAsync(() => System.Threading.Tasks.Task.FromResult("stale text"), () => false,
                () => { dismissed++; return System.Threading.Tasks.Task.FromResult(0); }, text => shownCount++);
            Check(!WaitForProbe(stale) && dismissed == 0 && shownCount == 0, "a changed target cannot dismiss another app menu or open stale text");
            var missing = SelectedTextReader.OpenBeforeCopyAsync(() => System.Threading.Tasks.Task.FromResult<string>(null), () => true,
                () => { dismissed++; return System.Threading.Tasks.Task.FromResult(0); }, text => shownCount++);
            Check(!WaitForProbe(missing) && dismissed == 0 && shownCount == 0, "unavailable direct read leaves the original clipboard fallback in control");
            var pending = new System.Threading.Tasks.TaskCompletionSource<string>();
            string timeout = WaitForProbe(SelectedTextReader.BeforeDismissAsync(() => pending.Task, ms => System.Threading.Tasks.Task.FromResult(0)));
            Check(timeout == null && !pending.Task.IsCompleted, "hung direct providers do not block the click or manufacture a text result");
            Check(SelectedTextReader.CompletedText(pending.Task) == null && SelectedTextReader.CompletedText(null) == null,
                "unfinished direct reads cannot produce success from missing data");
            pending.SetResult("late plain text");
            Check(SelectedTextReader.CompletedText(pending.Task) == "late plain text", "late complete source text is recoverable if the clipboard copy failed");
            Check(SelectedTextReader.CompletedText(System.Threading.Tasks.Task.FromResult("  ")) == "  ", "late whitespace text is also not rejected by content");
            var reader = new SelectedTextReader.Reader();
            using (var release = new System.Threading.ManualResetEventSlim())
            using (var started = new System.Threading.ManualResetEventSlim())
            {
                int calls = 0;
                var first = reader.Read(() => { System.Threading.Interlocked.Increment(ref calls); started.Set(); release.Wait(3000); return "plain"; });
                Check(started.Wait(1000), "one bounded direct reader starts");
                try
                {
                    for (int i = 0; i < 20; i++) Check(reader.Read(() => { calls++; return "duplicate"; }).Result == null, "slow selected-text providers never accumulate parallel calls");
                    Check(calls == 1, "only the original provider is running");
                }
                finally { release.Set(); }
                Check(WaitForProbe(first) == "plain", "direct reader releases its slot when complete");
            }
            Check(WaitForProbe(reader.Read(() => { throw new InvalidOperationException("synthetic provider"); })) == null, "direct provider failures use the clipboard fallback instead of escaping into async UI code");
        }


        static string MapLikeInput()
        {
            // 与报告结构一致，节点/域名/标识均为合成数据，禁止把实际配置写进公开测试。
            return "{nodes_data: {node_a: {uid: aabbcc0011223344, ip: 127.0.0.1, server: 192.0.2.20, serverName: node.example.invalid, country: CN, ports: [5678], protocal: trojan, core: 0, yl_limitrate: 4Mbps, yl_transport: auto, download_domain: [], download_ip: [], domain_glob: [], node_use_type: 1, t_port: 10006, t_port2: 10018}, "
                + "node_b: {node_use_type: 0, core: 3, uid: 123456ab7890cdef123456ab7890cdef, ip: 198.51.100.8, ports: [2324], protocal: tj, turn: {server: [203.0.113.9]}}}, "
                + "pkg_speed_list: [com.example.gsf, com.example.gms, com.android.vending], white_list: [], regex_speed_list: [play.example.invalid], vpn_ip_white_list: [], ca_switch: 0, "
                + "dns: {\"proxy_server\":\"8.8.8.8\",\"custom_host_map\":{\"www.example.invalid\":\"1.1.1.1\"}}, attach: {\"diverter\":0}, "
                + "yunluo_cfg: {\"basic\":{\"path\":\"${}\",\"mode\":\"Process\",\"processname\":[\"sample.exe\",\"browser.exe\"]},\"log\":{\"enabled\":true,\"loglevel\":\"debug\"},\"user\":{\"id\":\"synthetic\"},\"routing\":[{\"ips\":[\"192.0.2.0/24\"],\"domains\":[\"example.invalid\"],\"ports\":[],\"geoip\":0,\"outboundtags\":{\"default\":[\"direct\"]}}]}, fake_server_ip: 1.1.1.1}";
        }
        static JNode Child(JNode node, string key)
        { return node.Children.Find(n => n.Key == key); }
        static void BareMapInputTests()
        {
            string input = MapLikeInput();
            foreach (bool minify in new[] { false, true })
            {
                var result = JsonTools.Process(input, new FormatOptions { Lenient = true, Minify = minify });
                Check(result.Count == 1 && result.Error == null && result.Pieces.Count == 1 && result.Pieces[0].IsJson,
                    "map-like input is converted as ONE entire object, not only the trailing three quoted fragments");
                Check(result.Warning != null && result.Warning.Contains(L.T("未加引号的字符串值（按字符串推断，原始类型可能有歧义）")), "bare string inference is explicitly reported as lenient conversion");
                string output = result.Pieces[0].Text;
                Check(JsonTools.Pretty(output) != null, "map conversion produces fully strict JSON");
                var root = Dom.Parse(output, 0); Check(root != null && root.Kind == JKind.Object && root.Count == 10, "all outer keys including the opening nodes_data survive");
                var nodes = Child(root, "nodes_data"); Check(nodes.Kind == JKind.Object && nodes.Count == 2, "node hierarchy is retained instead of becoming log text");
                var node = Child(nodes, "node_a");
                foreach (var item in new[] { new[] { "uid", "aabbcc0011223344" }, new[] { "ip", "127.0.0.1" }, new[] { "server", "192.0.2.20" },
                    new[] { "serverName", "node.example.invalid" }, new[] { "yl_limitrate", "4Mbps" }, new[] { "protocal", "trojan" }, new[] { "yl_transport", "auto" } })
                    Check(Child(node, item[0]).Kind == JKind.String && Child(node, item[0]).StringValue == item[1], "UID/IP/domain/unit/enum values become strings without truncation");
                Check(Child(Child(nodes, "node_b"), "uid").StringValue == "123456ab7890cdef123456ab7890cdef", "digit-leading IDs are not parsed as partial numbers");
                Check(Child(node, "core").Kind == JKind.Number && Child(node, "core").Raw == "0", "genuine numbers retain their numeric type");
                Check(Child(node, "ports").Children[0].Kind == JKind.Number && Child(node, "ports").Children[0].Raw == "5678", "numeric port arrays remain numbers");
                Check(Child(root, "pkg_speed_list").Children.TrueForAll(n => n.Kind == JKind.String), "unquoted package names inside arrays become strings");
                Check(Child(root, "white_list").Kind == JKind.Array && Child(root, "white_list").Count == 0, "empty arrays remain arrays");
                Check(Child(Child(root, "dns"), "proxy_server").StringValue == "8.8.8.8", "already quoted nested JSON is preserved");
                var config = Child(root, "yunluo_cfg");
                Check(Child(Child(config, "basic"), "path").StringValue == "${}", "braces inside quoted strings do not affect structure");
                Check(Child(Child(config, "log"), "enabled").Kind == JKind.Literal && Child(Child(config, "log"), "enabled").Raw == "true", "real JSON booleans are not quoted");
                Check(Child(root, "fake_server_ip").StringValue == "1.1.1.1", "trailing bare IP survives with the whole root");
                var stable = JsonTools.Process(output, new FormatOptions { Minify = minify });
                Check(stable.Count == 1 && stable.Warning == null && stable.Error == null && stable.Pieces[0].Text == output,
                    "format/minify is stable after a single explicit conversion");
            }
        }


        static void MapScalarBoundaryTests()
        {
            var options = new FormatOptions { Lenient = true, Minify = true };
            foreach (var item in new[] {
                new[] { "trojan", "trojan" }, new[] { "CN", "CN" }, new[] { "127.0.0.1", "127.0.0.1" }, new[] { "4Mbps", "4Mbps" },
                new[] { "com.example.service", "com.example.service" }, new[] { "1234abcdef", "1234abcdef" },
                new[] { "true.example.invalid", "true.example.invalid" }, new[] { "false-positive", "false-positive" },
                new[] { "null.exe", "null.exe" }, new[] { "Trueish", "Trueish" }, new[] { "00123", "00123" },
                new[] { "1e", "1e" }, new[] { "+-1", "+-1" }, new[] { "1.2.3", "1.2.3" },
                new[] { "NaN", "NaN" }, new[] { "Infinity", "Infinity" }, new[] { "中文😀", "中文😀" },
                new[] { @"C:\temp\new\regex\d+", @"C:\temp\new\regex\d+" }, new[] { @"\\server\share\file", @"\\server\share\file" },
                new[] { "https://example.invalid/a//b?q=x", "https://example.invalid/a//b?q=x" },
                new[] { "http://example.invalid:8080/a?ref=scheme:value", "http://example.invalid:8080/a?ref=scheme:value" } })
            {
                var result = JsonTools.Process("{value:" + item[0] + "}", options);
                Check(result.Count == 1 && result.Pieces.Count == 1 && JsonTools.Pretty(result.Pieces[0].Text) != null, "bare map value produces one strict object");
                var value = Child(Dom.Parse(result.Pieces[0].Text, 0), "value");
                Check(value.Kind == JKind.String && value.StringValue == item[1], "bare map token remains a whole string with literal backslashes and Unicode");
                Check(result.Warning != null, "nonstandard string inference cannot be silent");
            }
            string source = "{name: auto, zero: 0, negative: -0, integer: 123, fraction: -1.20, exponent: 1e+99, big: 12345678901234567890123456789, plus: +2, yes: true, no: false, nothing: null, aliases: [True, False, None, undefined, NULL], quoted: [\"true\",\"123\",\"001\"]}";
            var typed = JsonTools.Process(source, options); Check(typed.Count == 1 && typed.Pieces.Count == 1, "mixed map scalar types parse together");
            var root = Dom.Parse(typed.Pieces[0].Text, 0);
            foreach (var item in new[] { new[] { "zero", "0" }, new[] { "negative", "-0" }, new[] { "integer", "123" }, new[] { "fraction", "-1.20" },
                new[] { "exponent", "1e+99" }, new[] { "big", "12345678901234567890123456789" }, new[] { "plus", "2" } })
                Check(Child(root, item[0]).Kind == JKind.Number && Child(root, item[0]).Raw == item[1], "numeric lexemes/precision are preserved instead of double conversion");
            Check(Child(root, "aliases").Raw == "[true,false,null,null,null]", "existing Python/JavaScript aliases keep their previous lenient semantics");
            Check(Child(root, "quoted").Children.TrueForAll(n => n.Kind == JKind.String), "already quoted scalar strings are never type-coerced");
            foreach (string text in new[] { "{x: auto/* note */, y: 1}", "{x: auto, // note\n y: 1}", "{x: auto, y: 1//note\n}", "{x:\u00a0auto, y: [1,],}" })
            {
                var result = JsonTools.Process(text, options);
                Check(result.Count == 1 && result.Pieces.Count == 1 && Child(Dom.Parse(result.Pieces[0].Text, 0), "x").StringValue == "auto",
                    "comments, special spaces and trailing commas remain compatible with map strings");
            }
            foreach (string invalid in new[] { "{a: one b: two}", "{a:1 b:2}", "{a:one:b:two}", "{a: }", "{a:,b:1}", "{a:foo[bar]}", "{a:${x}}", "{a:line\nsecond}", "{a:foo\"bar}" })
            {
                var result = JsonTools.Process(invalid, options);
                Check(result.Count == 0 || result.Warning != null, "ambiguous/missing separators, empty values and broken structure are not reported as complete success");
                Check(!(result.Count == 1 && result.Pieces.Count == 1 && result.Pieces[0].IsJson && result.Error == null),
                    "map support does not invent an entire object from structurally invalid text");
            }
            Check(JsonTools.Process("[INFO]", options).Count == 0, "bare standalone log tags are not guessed into JSON arrays");
            Check(JsonTools.Process("[INFO] 2026 [1] user=[abc] ok", options).Count == 0, "log prefixes remain ordinary text");
            Check(JsonTools.Process("{\"x\": flase}", options).Count == 0, "quoted-key JSON typos do not opt into Map string inference");
            Check(JsonTools.Process("{x:auto}", new FormatOptions()).Count == 0, "strict parsing never repairs unquoted map values");
            var mixed = JsonTools.Process("prefix " + MapLikeInput() + " suffix", options);
            Check(mixed.Count == 1 && mixed.Pieces.Count == 3 && mixed.Pieces[0].Text.StartsWith("prefix", StringComparison.Ordinal) && mixed.Pieces[2].Text.Contains("suffix"),
                "whole maps inside mixed logs preserve surrounding text");
            var incomplete = JsonTools.Process("{x:bad token, dns:{\"x\":1}}", options);
            Check(incomplete.Count == 1 && incomplete.Warning != null && incomplete.Pieces.Count > 1, "partial extraction after real syntax errors retains an explicit warning");
            bool wasEnglish = L.En;
            try
            {
                L.En = true; var translated = JsonTools.Process("{x:auto}", options);
                Check(translated.Warning != null && translated.Warning.Contains("inferred as strings"), "English UI reports inferred string types as well");
            }
            finally { L.En = wasEnglish; }
            var watch = Stopwatch.StartNew();
            var large = new StringBuilder("{items:[");
            for (int i = 0; i < 3000; i++) { if (i > 0) large.Append(','); large.Append("{uid:id_").Append(i).Append(",ip:192.0.2.1,rate:4Mbps,active:true}"); }
            large.Append("]}"); var batch = JsonTools.Process(large.ToString(), options);
            Check(batch.Count == 1 && batch.Pieces.Count == 1 && JsonTools.Pretty(batch.Pieces[0].Text) != null, "large map dump is normalized as one complete document");
            Check(watch.ElapsedMilliseconds < 5000, "map scanning does not repeatedly reparse suffixes of a valid document");
        }
        static void MapViewerTests()
        {
            bool previous = ViewSettings.Lenient;
            try
            {
                ViewSettings.Lenient = true;
                using (var viewer = Open(MapLikeInput()))
                {
                    var box = Box(viewer); string displayed = box.Text;
                    Check((string)Field(viewer, "statusKind") == "warn" && ((List<Piece>)Field(viewer, "pieces")).Count == 1,
                        "viewer shows an explicit inference warning and a single complete JSON document");
                    var roots = (List<JNode>)Call(viewer, "GetDom");
                    Check(roots.Count == 1 && Child(roots[0], "nodes_data").Count == 2, "tree/path model includes the previously unformatted beginning");
                    VerifyRoot(viewer); box.FoldAll(true); Check(box.CollapsedCount > 0 && box.Text == displayed, "whole normalized map folds without dropping opening data");
                    box.FoldAll(false); Call(viewer, "Reprocess", true, false);
                    Check(box.Text != displayed && JsonTools.Pretty(box.Text) != null, "normalized map minifies as standard JSON");
                    Call(viewer, "Undo"); Check(box.Text == displayed, "minify undo restores the complete normalized map");
                    ((ToolStripButton)Field(viewer, "btnOnly")).Checked = true;
                    Check(JsonTools.BuildView(JsonTools.Process(box.Text, true).Pieces, false).Text
                        == JsonTools.BuildView(JsonTools.Process(displayed, true).Pieces, false).Text,
                        "JSON-only view does not discard an unparsed map prefix, regardless of the last formatting mode");
                    Call(viewer, "LoadText", MapLikeInput(), false, true);
                    Check((string)Field(viewer, "statusKind") == "warn" && ((ToolStripStatusLabel)Field(viewer, "status")).Text.Contains(L.T("未加引号的字符串值（按字符串推断，原始类型可能有歧义）")),
                        "expand-nested operation cannot hide the conversion/type-inference warning");
                    Check(JsonTools.Pretty(box.Text) != null && Child(Dom.Parse(box.Text, 0), "nodes_data").Count == 2,
                        "expanding nested input still displays the complete converted map");
                }
            }
            finally { ViewSettings.Lenient = previous; }
        }

        static string QuoteJson(string text)
        {
            return "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
        }
        static void SelectionInputTests()
        {
            // 与报告相同的形状，但全部为合成数据，不把用户的节点/账号内容放进仓库。
            string nested = "{\"routes\":[{\"server\":\"example.invalid\",\"port\":[10001,10002]}],\"path\":\"C:\\\\tmp\\\\file\",\"text\":\"中文 😀\\nnext\"}";
            var source = new StringBuilder("{\"code\":200,\"msg\":\"success\",\"data\":{\"nodes_data\":{");
            for (int i = 0; i < 24; i++)
            {
                if (i > 0) source.Append(',');
                source.Append('"').Append("node_").Append(i).Append("\":{\"uid\":\"synthetic\",\"ports\":[10001],\"active\":true}");
            }
            source.Append("},\"connection\":").Append(QuoteJson(nested)).Append("}}");
            string raw = source.ToString(), expected = JsonTools.Pretty(raw);
            Check(expected != null, "synthetic reported-shape input is valid JSON");
            foreach (bool lenient in new[] { false, true })
            {
                var options = new FormatOptions { Lenient = lenient };
                var normal = JsonTools.Process(raw, options);
                Check(normal.Count == 1 && normal.Error == null && normal.Warning == null && normal.Pieces[0].Text == expected,
                    "normal JSON and nested string escaping are unchanged");
                foreach (string input in new[] { QuoteJson(raw), QuoteJson(raw).Substring(1, QuoteJson(raw).Length - 2), QuoteJson(QuoteJson(raw)), "\uFEFF \r\n" + QuoteJson(raw) + "\uFEFF" })
                {
                    var result = JsonTools.Process(input, options);
                    Check(result.Count == 1 && result.Error == null && result.Warning != null && result.Pieces.Count == 1 && result.Pieces[0].Text == expected,
                        "quoted/escaped entire selection is decoded without dropping fields or changing nested strings");
                    var roundtrip = JsonTools.Process(JsonTools.BuildView(result.Pieces, false).Text, options);
                    Check(roundtrip.Count == 1 && roundtrip.Warning == null && roundtrip.Pieces[0].Text == expected,
                        "normalized selection remains stable when formatted again");
                }
                var expanded = JsonTools.Process(QuoteJson(raw), new FormatOptions { Lenient = lenient, Expand = true });
                Check(expanded.Count == 1 && expanded.Expanded == 1 && JsonTools.Pretty(expanded.Pieces[0].Text) != null,
                    "explicit nested expansion works after decoding an outer string");
            }
            foreach (string rawValue in new[] { "{}", "[]", "[1,2,3]", "{\"n\":4.05e+123,\"s\":\"\\u4e2d\\n\\\\literal\"}" })
            {
                var result = JsonTools.Process(QuoteJson(rawValue), false);
                Check(result.Count == 1 && result.Pieces[0].Text == JsonTools.Pretty(rawValue), "empty containers, arrays, number lexemes and Unicode escapes roundtrip");
            }
            Check(JsonTools.Process(QuoteJson("42"), false).Count == 0, "scalar strings are not silently reinterpreted as objects");
            Check(JsonTools.Process(QuoteJson("prefix {\"a\":1} suffix"), false).Count == 0, "not an entire object: no lossy outer-string decoding");
            Check(JsonTools.Process(QuoteJson("{\"a\":1"), false).Count == 0, "a truncated decoded object is still a parse failure");
            Check(JsonTools.Process("{\\\"a\\\":\\q}", new FormatOptions { Lenient = true }).Count == 0,
                "invalid outer escape sequences are not guessed or stripped");
            string tooMany = raw; for (int i = 0; i < 5; i++) tooMany = QuoteJson(tooMany);
            Check(JsonTools.Process(tooMany, false).Count == 0, "outer decoding has a bounded depth");
            var mixed = JsonTools.Process("prefix {\"value\":1} suffix", false);
            Check(mixed.Count == 1 && mixed.Pieces.Count == 3 && mixed.Warning == null, "mixed-log extraction is not changed");
            using (var viewer = Open(QuoteJson(raw)))
            {
                var box = Box(viewer); string before = box.Text;
                Check(before.Replace("\r\n", "\n") == expected.Replace("\r\n", "\n") && (string)Field(viewer, "statusKind") == "warn", "viewer shows complete decoded JSON with an explicit notice");
                Call(viewer, "Reprocess", false, true);
                Check(box.Text != before && box.Text.Contains("example.invalid"), "decoded selection can expand its nested JSON");
                Call(viewer, "Undo"); Check(box.Text == before, "nested expansion still undoes after selection decoding");
                VerifyRoot(viewer);
            }
        }
        static SelectionCapture.Result SimulateCopy(uint before, Func<int, uint> sequence, Func<int, string> text, out int ticks, out int reads)
        {
            int tick = 0, count = 0;
            var task = SelectionCapture.ReadCopiedTextAsync(before, () => sequence(tick), () => { count++; return text(tick); },
                ms => { Check(ms == 25, "copy wait is asynchronous and bounded"); tick++; return System.Threading.Tasks.Task.FromResult(0); });
            var result = task.GetAwaiter().GetResult(); ticks = tick; reads = count; return result;
        }
        static void SelectionCaptureTests()
        {
            int ticks, reads;
            var absent = SimulateCopy(50, t => 50, t => "old unrelated clipboard", out ticks, out reads);
            Check(absent.Text == null && ticks == 80 && reads == 0, "unchanged clipboard never supplies stale text");
            const string full = "{\"code\":200,\"text\":\"中文 😀\",\"nested\":\"{\\\"n\\\":1}\"}";
            var delayed = SimulateCopy(50, t => t < 3 ? 50u : 51u, t => t < 14 ? null : full, out ticks, out reads);
            Check(delayed.Text == full && delayed.Sequence == 51 && ticks >= 14 && reads > 1, "delayed rendering and busy/empty clipboard are retried beyond the old 40ms read");
            var formats = SimulateCopy(50, t => t < 3 ? 51u : t < 6 ? 52u : 53u, t => full, out ticks, out reads);
            Check(formats.Text == full && formats.Sequence == 53 && ticks >= 9 && reads == 1, "wait for all formats to settle before reading text");
            bool changed = false;
            var changedWhileReading = SelectionCapture.ReadCopiedTextAsync(50, () => changed ? 52u : 51u,
                () => { if (!changed) { changed = true; return "partial"; } return full; }, ms => System.Threading.Tasks.Task.FromResult(0)).GetAwaiter().GetResult();
            Check(changedWhileReading.Text == full && changedWhileReading.Sequence == 52, "a version change during GetText discards that result and retries");
            var wrappedSequence = SimulateCopy(uint.MaxValue, t => 0u, t => full, out ticks, out reads);
            Check(wrappedSequence.Text == full && wrappedSequence.Sequence == 0, "clipboard sequence wraparound is supported");
        }
        sealed class FrameWindow : NativeWindow, IDisposable
        {
            public readonly AnimationFramePump Pump = new AnimationFramePump();
            public int Frames;
            public FrameWindow() { CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == AnimationFramePump.MessageId)
                { if (Pump.Accept(message.WParam)) Frames++; return; }
                base.WndProc(ref message);
            }
            public void Dispose() { Pump.Dispose(); DestroyHandle(); }
        }
        static void FrameLifecycleTests()
        {
            using (var sink = new FrameWindow())
            {
                sink.Pump.Start(sink.Handle, 8);
                var watch = Stopwatch.StartNew();
                while (sink.Frames < 3 && watch.ElapsedMilliseconds < 2000) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                Check(sink.Frames >= 3, "native frame clock delivers repeated UI messages");
                var first = Field(sink.Pump, "run"); int oldId = (int)first.GetType().GetField("Id").GetValue(first);
                for (int i = 0; i < 25; i++) { sink.Pump.Stop(); sink.Pump.Start(sink.Handle, 8); }
                Check(!sink.Pump.Accept((IntPtr)oldId), "restarting never accepts an old animation message");
                var current = Field(sink.Pump, "run");
                System.Threading.Thread.Sleep(55);
                Check((int)current.GetType().GetField("Pending").GetValue(current) <= 1, "even a stalled UI has at most one native frame queued");
                sink.Pump.Stop(); int stopped = sink.Frames;
                for (int i = 0; i < 5; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(2); }
                Check(sink.Frames == stopped && Field(sink.Pump, "run") == null, "idle pump does not deliver accepted frames");
                sink.Pump.Dispose(); sink.Pump.Dispose();
                bool rejected = false; try { sink.Pump.Start(sink.Handle, 16); } catch (ObjectDisposedException) { rejected = true; }
                Check(rejected, "disposed pump cannot restart a worker or use released handles");
            }
            string source = "{\"items\":[" + new string(' ', 5000) + "1,2,3]}";
            using (var viewer = Open(source))
            {
                var box = Box(viewer);
                // 足够长的多行文档，避免光标操作或滚动边界提前结束动画。
                box.Text = "{\n\"items\":[\n" + string.Join(",\n", new string('1', 200).ToCharArray()) + "\n]}";
                box.Select(0, 0); box.ScrollPos = Point.Empty;
                ((ToolStripTextBox)Field(viewer, "search")).Text = "1";
                typeof(JsonViewer).GetField("domVersion", Private).SetValue(viewer, -1);
                Call(box, "Wheel", -120);
                if (box.IsSmoothScrolling)
                {
                    Call(viewer, "UpdatePath"); Call(viewer, "UpdateMatches", false); Call(viewer, "HoverAt", new Point(12, 12));
                    Check((int)Field(viewer, "domVersion") == -1 && ((Timer)Field(viewer, "pathTimer")).Enabled,
                        "DOM/path work waits until scrolling is idle");
                    Check(((Timer)Field(viewer, "matchTimer")).Enabled && (int)Field(viewer, "tipStart") == -2,
                        "search and hover decoding cannot block an animation frame");
                    box.StopSmooth(); Call(viewer, "UpdatePath"); Call(viewer, "UpdateMatches", false);
                    Check((int)Field(viewer, "domVersion") == (int)Field(viewer, "textVersion") && box.Highlights.Count == 200,
                        "deferred path and search remain correct after animation ends");
                }
            }
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

        static void CheckAdjacentCaretGuide(JsonViewer viewer, FoldRegion region, int caret, string scenario)
        {
            var box = Box(viewer);
            int plain = box.Text.IndexOf("value", StringComparison.Ordinal);
            box.Select(plain, 0); // 先离开括号，不能复用上一轮残留的配对状态。
            box.Select(caret, 0);
            CodeBox.ScopeGuide guide;
            Check(box.SelectionStart == caret && box.SelectionLength == 0 && box.BracketOpen == region.Open && box.BracketClose == region.Close,
                scenario + ": insertion caret alone selects its adjacent bracket pair");
            Check(box.TryGetScopeGuide(out guide), scenario + ": both gutter and text scope guides have visible geometry");
            // 原生鼠标命中可能是括号右边的换行/逗号。MouseUp 不能把 SelectionChanged 已找到的配对清掉。
            Point hit = box.CharPosition(caret);
            Call(box, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, hit.X, hit.Y + box.LineHeight / 2, 0));
            Check(box.BracketOpen == region.Open && box.BracketClose == region.Close && box.TryGetScopeGuide(out guide),
                scenario + ": mouse release preserves the guide even when hit testing returns the adjacent character");
        }
        static void AdjacentCaretGuideTests()
        {
            using (var viewer = Open("{\"outer\":{\"items\":[{\"value\":1},{\"value\":2}],\"tail\":true},\"plain\":\"{[ignored]}\"}"))
            {
                var box = Box(viewer); string original = box.Text, rtf = box.Rtf; bool undo = box.CanUndo;
                var regions = new List<FoldRegion>(box.Folding.Regions);
                foreach (var region in regions)
                {
                    CheckAdjacentCaretGuide(viewer, region, region.Open, "before opening bracket");
                    CheckAdjacentCaretGuide(viewer, region, region.Open + 1, "after opening bracket");
                    CheckAdjacentCaretGuide(viewer, region, region.Close, "before closing bracket");
                    CheckAdjacentCaretGuide(viewer, region, region.Close + 1, "after closing bracket");
                }
                // 真实鼠标消息：字符左半边/右半边落在两个不同的输入光标位置。
                var child = regions.Find(r => r.Open > 0 && original[r.Open] == '{');
                foreach (int bracket in new[] { child.Open, child.Close })
                    foreach (bool right in new[] { false, true })
                    {
                        int plain = original.IndexOf("value", StringComparison.Ordinal); box.Select(plain, 0);
                        box.ScrollPos = Point.Empty;
                        Point point = box.CharPosition(bracket);
                        point.Offset(right ? box.CharacterWidth * 3 / 4 : box.CharacterWidth / 4, box.LineHeight / 2);
                        IntPtr location = (IntPtr)((point.Y << 16) | (point.X & 0xFFFF));
                        Native.SendMessage(box.Handle, 0x0201, (IntPtr)1, location);
                        Native.SendMessage(box.Handle, 0x0202, IntPtr.Zero, location);
                        Check(box.SelectionLength == 0 && box.SelectionStart == bracket + (right ? 1 : 0),
                            "native mouse click places the insertion caret on the requested side");
                        Check(box.BracketOpen == child.Open && box.BracketClose == child.Close,
                            "native click on either half of opening/closing brackets keeps its scope");
                    }
                box.Select(child.Open, 0); box.Focus();
                Native.SendMessage(box.Handle, 0x0100, (IntPtr)Keys.Right, IntPtr.Zero);
                Native.SendMessage(box.Handle, 0x0101, (IntPtr)Keys.Right, IntPtr.Zero);
                Check(box.SelectionStart == child.Open + 1 && box.BracketOpen == child.Open && box.BracketClose == child.Close,
                    "native right arrow moves past opening bracket without clearing its scope");
                box.Select(child.Close + 1, 0);
                Native.SendMessage(box.Handle, 0x0100, (IntPtr)Keys.Left, IntPtr.Zero);
                Native.SendMessage(box.Handle, 0x0101, (IntPtr)Keys.Left, IntPtr.Zero);
                Check(box.SelectionStart == child.Close && box.BracketOpen == child.Open && box.BracketClose == child.Close,
                    "native left arrow moves before closing bracket without clearing its scope");
                box.Select(child.Open + 1, 0); Call(viewer, "UpdateBrackets", original.Length - 1);
                Check(box.BracketOpen == child.Open && box.BracketClose == child.Close, "an unrelated mouse hit cannot override the adjacent insertion caret scope");
                int ignored = original.IndexOf("{[ignored]}", StringComparison.Ordinal);
                foreach (int caret in new[] { ignored, ignored + 1, ignored + 2, ignored + 10, ignored + 11 })
                { box.Select(caret, 0); Check(box.BracketOpen == -1, "string-literal bracket adjacency never creates a scope"); }
                box.Select(child.Open, 2); CodeBox.ScopeGuide guide;
                Check(box.BracketOpen == -1 && !box.TryGetScopeGuide(out guide), "an ordinary text selection still clears the caret guide");
                Check(box.Text == original && box.Rtf == rtf && box.CanUndo == undo && !(bool)Field(viewer, "edited"),
                    "caret and mouse scope changes do not mutate text, formatting or undo");
            }
            using (var viewer = Open("{\"value\":\"{\\\"items\\\":[{\\\"value\\\":1}]}\",\"tail\":0}"))
            {
                Call(viewer, "Reprocess", false, true);
                var box = Box(viewer); var region = box.Folding.Regions.Find(r => box.Text[r.Open] == '[');
                Check(region != null, "nested string expansion creates a real array scope");
                CheckAdjacentCaretGuide(viewer, region, region.Open + 1, "expanded nested array after opening bracket");
                CheckAdjacentCaretGuide(viewer, region, region.Close + 1, "expanded nested array after closing bracket");
                box.Select(region.Open, 0); box.FoldCurrent(true);
                CodeBox.ScopeGuide unused;
                Check(!box.TryGetScopeGuide(out unused), "collapsed bracket pair does not show a false multiline guide");
                box.FoldCurrent(false);
                CheckAdjacentCaretGuide(viewer, region, region.Close + 1, "reopened nested array after closing bracket");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ImePositionQuery
        {
            public uint Size, Character;
            public int X, Y;
            public uint LineHeight;
            public int Left, Top, Right, Bottom;
        }
        static void QueryImePosition(CodeBox box, int message, int request, string scenario)
        {
            Point before = box.ScrollPos;
            int start = box.SelectionStart, length = box.SelectionLength, notifications = 0;
            Action changed = delegate { notifications++; }; box.ViewportChanged += changed;
            var query = new ImePositionQuery { Size = (uint)Marshal.SizeOf(typeof(ImePositionQuery)) };
            IntPtr memory = Marshal.AllocHGlobal((int)query.Size);
            try
            {
                Marshal.StructureToPtr(query, memory, false);
                IntPtr result = Native.SendMessage(box.Handle, message, (IntPtr)request, memory);
                Check(box.ScrollPos == before, scenario + ": IME position-only query cannot scroll back to an old caret");
                Check(box.SelectionStart == start && box.SelectionLength == length, scenario + ": IME query cannot reselect text or move the caret");
                Check(notifications == 0, scenario + ": temporary engine movement cannot invalidate viewport listeners");
                if (result != IntPtr.Zero)
                {
                    var response = (ImePositionQuery)Marshal.PtrToStructure(memory, typeof(ImePositionQuery));
                    Point expected = box.PointToScreen(box.CharPosition(start));
                    Check(Math.Abs((long)response.Y - expected.Y) <= box.LineHeight,
                        scenario + ": returned IME screen coordinates correspond to the restored viewport");
                }
            }
            finally { Marshal.FreeHGlobal(memory); box.ViewportChanged -= changed; }
        }
        static void InactiveScrollActivationTests()
        {
            var source = new StringBuilder("{\"items\":[");
            for (int i = 0; i < 1800; i++)
            { if (i > 0) source.Append(','); source.Append("{\"id\":").Append(i).Append(",\"value\":\"synthetic 中文\"}"); }
            source.Append("]}");
            using (var viewer = Open(source.ToString()))
            using (var other = new Form { Size = new Size(260, 120), ShowInTaskbar = false })
            using (var input = new TextBox { Dock = DockStyle.Fill })
            {
                other.Controls.Add(input); var box = Box(viewer); string original = box.Text;
                int imeMessage = (int)typeof(CodeBox).GetField("ImeQueryPosition", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                foreach (bool oldCaretBelow in new[] { false, true })
                {
                    Native.ForceForeground(viewer.Handle); box.Focus();
                    int old = oldCaretBelow ? original.LastIndexOf("synthetic", StringComparison.Ordinal) : original.IndexOf("synthetic", StringComparison.Ordinal);
                    box.Select(old, 4); box.ScrollToCaret();
                    other.Show(); Native.ForceForeground(other.Handle); input.Focus(); Application.DoEvents();
                    Check(!box.Focused, "test really moves the input focus to a different window");
                    int version = (int)Field(viewer, "textVersion"); bool undo = box.CanUndo;
                    Point initial = box.ScrollPos;
                    if (SystemInformation.MouseWheelScrollLines != 0)
                    {
                        for (int i = 0; i < 8; i++) Call(box, "Wheel", oldCaretBelow ? 120 : -120);
                        DrainScroll(box);
                        Check(box.ScrollPos != initial && !box.Focused, "hover wheel browsing moves the inactive viewport without stealing input focus");
                    }
                    box.ScrollPos = new Point(0, oldCaretBelow ? 1200 : 70003); // 包括 16 位以上的像素坐标。
                    Point browsed = box.ScrollPos;
                    QueryImePosition(box, imeMessage, 1, oldCaretBelow ? "old caret below viewport" : "old caret above viewport");
                    QueryImePosition(box, 0x0288, 6, "standard IMR_QUERYCHARPOSITION");
                    Native.SendMessage(box.Handle, 0x0021, viewer.Handle, (IntPtr)((0x0201 << 16) | 1));
                    Check(box.ScrollPos == browsed, "mouse activation preserves the currently browsed viewport before hit testing");
                    viewer.Activate(); Native.ForceForeground(viewer.Handle); box.Focus(); Application.DoEvents();
                    Check(box.Focused && box.ScrollPos == browsed, "returning to the viewer does not jump to the old selection");
                    Check(box.SelectionStart == old && box.SelectionLength == 4 && box.Text == original && box.CanUndo == undo
                        && (int)Field(viewer, "textVersion") == version, "activation keeps source, old selection, undo and structural caches intact");
                    int hit = box.CharIndexAt(new Point(80, box.ClientSize.Height / 2));
                    box.Select(hit, 0);
                    Check(box.SelectionStart == hit && box.ScrollPos == browsed && hit != old, "new caret placement is based on the browsed viewport, not the old viewport");
                    System.Threading.Thread.Sleep(25); Application.DoEvents();
                    Check(box.ScrollPos == browsed, "delayed position queries cannot create a second jump after caret placement");
                    box.Select(old, 0); box.ScrollToCaret();
                    Check(Math.Abs(box.CharPosition(old).Y) <= box.ClientSize.Height,
                        "explicit ScrollToCaret navigation is still allowed to reveal a target");
                    box.Select(hit, 0); box.ScrollToCaret(); Native.SendMessage(box.Handle, 0x0102, (IntPtr)'x', IntPtr.Zero);
                    Check(box.Text != original && box.CanUndo, "actual keyboard input still edits normally after focus restoration");
                    box.Undo(); Check(box.Text == original, "keyboard input after activation keeps native undo intact");
                }
                box.Select(0, 0); box.ScrollPos = new Point(0, 70003);
                box.FoldAll(true); QueryImePosition(box, imeMessage, 1, "folded document");
                box.FoldAll(false); box.Select(0, 0); box.ScrollPos = new Point(0, 70003);
                Native.SendMessage(box.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero);
                Check((bool)Field(box, "imeComposing"), "active IME composition is not treated as a passive viewport read");
                Native.SendMessage(box.Handle, 0x010E, IntPtr.Zero, IntPtr.Zero);
                Check(!(bool)Field(box, "imeComposing"), "IME end releases composition state");
                Native.SendMessage(box.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero);
                other.Activate(); input.Focus(); Application.DoEvents();
                Check(!(bool)Field(box, "imeComposing"), "losing focus cannot leave a stale composition state");
            }
            using (var viewer = Open("{\"long\":\"" + new string('x', 3000) + "\",\"tail\":[1,2]}"))
            {
                var box = Box(viewer); ((ToolStripButton)Field(viewer, "btnWrap")).Checked = false;
                box.Select(0, 0); box.ScrollPos = new Point(260, 0);
                Check(box.ScrollPos.X > 0, "horizontal preservation test actually scrolls a nonwrapped document");
                int imeMessage = (int)typeof(CodeBox).GetField("ImeQueryPosition", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                QueryImePosition(box, imeMessage, 1, "horizontal offscreen caret");
                QueryImePosition(box, 0x0288, 6, "horizontal standard IME request");
            }
            using (var viewer = Open(""))
            {
                var box = Box(viewer); box.Focus();
                Check(box.ScrollPos == Point.Empty && box.SelectionStart == 0, "empty-editor focus still initializes normally");
            }
        }


        static Native.GUITHREADINFO CaretState(CodeBox box)
        {
            var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO)) };
            Check(Native.GetGUIThreadInfo(Native.GetCurrentThreadId(), ref info) && info.hwndCaret == box.Handle,
                "the editor still owns the native accessibility/IME caret");
            return info;
        }
        static int CaretDistance(Color a, Color b)
        { return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); }
        static Color DecodeCaretPixel(Color back, Color mask)
        { return Color.FromArgb(back.R ^ mask.R, back.G ^ mask.G, back.B ^ mask.B); }
        static void NeonCaretVisualTests()
        {
            foreach (bool dark in new[] { false, true })
                foreach (int height in new[] { 10, 19, 28, 44, 96 })
                    foreach (int nativeWidth in new[] { 1, 3, 6 })
                    {
                        Color back = dark ? Theme.Night.Back : Theme.Light.Back;
                        using (var image = NeonCaretVisual.Build(height, nativeWidth, back, dark))
                        {
                            int core = NeonCaretVisual.CoreWidth(height, nativeWidth), halo = NeonCaretVisual.HaloWidth(core);
                            Check(core >= 2 && core >= nativeWidth && image.Width == core + halo && image.Height == height,
                                "neon caret has a thick readable core, respects accessibility width and scales with line height");
                            Check(halo <= 8 && image.Width <= Math.Max(nativeWidth, (height + 7) / 10) + 8,
                                "glow remains a narrow strip rather than a large expensive overlay");
                            Color center = DecodeCaretPixel(back, image.GetPixel(0, height / 2));
                            Check(center.ToArgb() == NeonCaretVisual.ColorAt((double)(height / 2) / (height - 1), dark).ToArgb(), "native XOR restores the intended visible core color in both themes");
                            Check(CaretDistance(back, center) > 250, "the core stays high-contrast against both light and dark backgrounds");
                            int previous = CaretDistance(back, center);
                            for (int x = core; x < image.Width; x++)
                            {
                                int next = CaretDistance(back, DecodeCaretPixel(back, image.GetPixel(x, height / 2)));
                                Check(next < previous, "cached glow smoothly fades to the right of the exact insertion position"); previous = next;
                            }
                            Check(CaretDistance(back, DecodeCaretPixel(back, image.GetPixel(0, 0))) < CaretDistance(back, center),
                                "top and bottom caps are softened without moving the native caret origin");
                        }
                    }
        }
        static void NeonCaretNativeTests()
        {
            bool oldDark = ViewSettings.Dark, oldNeon = ViewSettings.NeonCaret;
            float oldSize = ViewSettings.FontSize;
            try
            {
                ViewSettings.NeonCaret = true; ViewSettings.FontSize = 12;
                foreach (bool dark in new[] { true, false })
                {
                    ViewSettings.Dark = dark;
                    using (var viewer = Open("{\"unicode\":\"中文 😀\",\"items\":[1,2,3]}"))
                    {
                        var box = Box(viewer); Native.ForceForeground(viewer.Handle); box.Focus(); box.Select(4, 0); Application.DoEvents();
                        string text = box.Text, rtf = box.Rtf; bool undo = box.CanUndo;
                        int version = (int)Field(viewer, "textVersion"); Point scroll = box.ScrollPos;
                        var info = CaretState(box);
                        if (SystemInformation.HighContrast)
                        {
                            Check((IntPtr)Field(box, "neonBitmap") == IntPtr.Zero, "Windows high-contrast mode retains its original system caret");
                            continue;
                        }
                        Check((IntPtr)Field(box, "neonBitmap") != IntPtr.Zero && info.r - info.l == (int)Field(box, "neonWidth"),
                            "the real Windows caret uses the neon bitmap, not merely a painted screenshot");
                        Check(info.r - info.l > (int)Field(box, "originalCaretWidth") && info.b - info.t > 8,
                            "native caret gets a visibly thicker core plus glow at actual editor line height");
                        Point start = new Point(info.l, info.t);
                        box.SetNeonCaret(false, dark, Theme.Current.Back);
                        info = CaretState(box);
                        Check(new Point(info.l, info.t) == start && info.r - info.l == (int)Field(box, "originalCaretWidth"),
                            "disabling neon immediately restores a normal caret at exactly the same text position");
                        box.SetNeonCaret(true, dark, Theme.Current.Back);
                        int builds = (int)Field(box, "neonBitmapBuilds"), invalidations = 0;
                        IntPtr bitmap = (IntPtr)Field(box, "neonBitmap");
                        InvalidateEventHandler invalidated = delegate { invalidations++; };
                        box.Invalidated += invalidated;
                        var watch = Stopwatch.StartNew();
                        for (int i = 0; i < 600; i++) box.RefreshNeonCaret();
                        box.Invalidated -= invalidated;
                        Check(invalidations == 0 && (int)Field(box, "neonBitmapBuilds") == builds && (IntPtr)Field(box, "neonBitmap") == bitmap,
                            "idle/native refresh reuses one cached bitmap without issuing any editor repaint");
                        Check(watch.ElapsedMilliseconds < 1200, "constant-time caret refresh has no document scan or animation work");
                        Console.WriteLine("  neon native refresh: 600 calls in " + watch.ElapsedMilliseconds + " ms, bitmap builds=" + builds);
                        for (int i = 0; i < 20; i++) { box.Select(4 + i % 6, 0); box.Invalidate(); box.Update(); }
                        Check((int)Field(box, "neonBitmapBuilds") == builds, "caret movement and normal painting do not allocate a bitmap per frame");
                        box.Select(4, 0);
                        Check(box.Text == text && box.Rtf == rtf && box.CanUndo == undo && (int)Field(viewer, "textVersion") == version && box.ScrollPos == scroll,
                            "neon never changes source, formatting, selection semantics, undo or scroll position");
                        box.Select(4, 5); box.RefreshNeonCaret();
                        Check(box.SelectionStart == 4 && box.SelectionLength == 5 && (int)Field(box, "neonBitmapBuilds") == builds,
                            "a selected text range is left to RichEdit without moving or reallocating it");
                        box.Select(4, 0);
                        Native.SendMessage(box.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero);
                        Check((bool)Field(box, "imeComposing"), "IME composing flag remains active while glow is suppressed");
                        info = CaretState(box);
                        Check(info.r - info.l <= (int)Field(box, "originalCaretWidth"), "composition uses the normal input-method caret");
                        Native.SendMessage(box.Handle, 0x010E, IntPtr.Zero, IntPtr.Zero);
                        Check(!(bool)Field(box, "imeComposing") && CaretState(box).r - CaretState(box).l == (int)Field(box, "neonWidth"),
                            "ending IME composition restores the cached glow");
                        Native.SendMessage(box.Handle, 0x0102, (IntPtr)'x', IntPtr.Zero); Check(box.CanUndo && box.Text != text, "neon input still reaches the native editor");
                        box.Undo(); Check(box.Text == text, "native undo remains intact with a bitmap caret");
                        ViewSettings.FontSize = 18; Call(viewer, "Restyle"); box.RefreshNeonCaret();
                        Check((int)Field(box, "neonHeight") > info.b - info.t, "zoom rebuilds one bitmap to match the new line height");
                        ViewSettings.FontSize = 12; Call(viewer, "Restyle");
                        ViewSettings.Dark = !dark; Call(viewer, "Restyle");
                        Check((bool)Field(box, "neonDark") == !dark && (Color)Field(box, "neonBackground") == Theme.Current.Back,
                            "theme changes use a new XOR mask for the actual editor background");
                        ViewSettings.Dark = dark; Call(viewer, "Restyle");
                        using (var search = new TextBox { Dock = DockStyle.Top, Text = "another input control" })
                        {
                            // 窄窗口中的工具栏搜索框可能在溢出菜单里，使用一个明确可见的输入框验证焦点归属。
                            viewer.Controls.Add(search); search.BringToFront(); search.Focus(); Application.DoEvents();
                            var otherCaret = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO)) };
                            Native.GetGUIThreadInfo(Native.GetCurrentThreadId(), ref otherCaret); IntPtr owner = otherCaret.hwndCaret;
                            box.RefreshNeonCaret(); Native.GetGUIThreadInfo(Native.GetCurrentThreadId(), ref otherCaret);
                            Check(search.Focused && !box.Focused && otherCaret.hwndCaret == owner, "a nonfocused editor never steals another input field caret");
                            viewer.Controls.Remove(search);
                        }
                        box.Focus(); box.Select(4, 0); box.RefreshNeonCaret();
                        Call(viewer, "BuildSettingsMenu");
                        var settings = (ToolStripDropDownButton)Field(viewer, "btnSettings"); ToolStripMenuItem option = null;
                        foreach (ToolStripItem item in settings.DropDownItems)
                            if (item.Text == L.T("霓虹光标（轻量）", "Neon caret (lightweight)")) option = (ToolStripMenuItem)item;
                        Check(option != null && option.Checked, "settings expose a remembered simple neon caret toggle");
                        option.PerformClick(); box.Focus(); box.RefreshNeonCaret();
                        Check(!ViewSettings.NeonCaret && CaretState(box).r - CaretState(box).l == (int)Field(box, "originalCaretWidth"),
                            "the settings toggle restores the system caret without reformatting the document");
                        option.PerformClick(); box.Focus(); box.RefreshNeonCaret();
                        Check(ViewSettings.NeonCaret && box.Text == text, "toggle back on retains all content");
                    }
                }
                using (var viewer = Open(""))
                {
                    var box = Box(viewer); Native.ForceForeground(viewer.Handle); box.Focus(); box.RefreshNeonCaret();
                    Check(box.Text == "" && box.SelectionStart == 0 && (SystemInformation.HighContrast || (IntPtr)Field(box, "neonBitmap") != IntPtr.Zero),
                        "empty input windows also get a visible insertion caret");
                }
            }
            finally { ViewSettings.Dark = oldDark; ViewSettings.NeonCaret = oldNeon; ViewSettings.FontSize = oldSize; }
        }
        static void NeonCaretResourceTests()
        {
            bool dark = ViewSettings.Dark, enabled = ViewSettings.NeonCaret;
            try
            {
                ViewSettings.NeonCaret = true;
                var viewer = Open("{\"text\":\"resource test\"}"); var box = Box(viewer);
                try
                {
                    Native.ForceForeground(viewer.Handle); box.Focus(); box.Select(5, 0); box.RefreshNeonCaret();
                    uint before = Native.GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                    for (int i = 0; i < 80; i++)
                    { bool night = i % 2 == 0; box.SetNeonCaret(true, night, night ? Theme.Night.Back : Theme.Light.Back); }
                    uint after = Native.GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                    Check(after <= before + 2, "80 theme changes keep at most one native caret bitmap instead of leaking GDI handles");
                    var other = new TextBox { Dock = DockStyle.Top }; viewer.Controls.Add(other); other.BringToFront(); other.Text = "search-like control"; other.Focus();
                    var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO)) }; Native.GetGUIThreadInfo(Native.GetCurrentThreadId(), ref info);
                    IntPtr owner = info.hwndCaret; box.Dispose(); Native.GetGUIThreadInfo(Native.GetCurrentThreadId(), ref info);
                    Check((IntPtr)Field(box, "neonBitmap") == IntPtr.Zero && info.hwndCaret == owner,
                        "disposing an inactive editor frees its bitmap without destroying another control caret");
                    other.Dispose();
                }
                finally { viewer.Dispose(); }
            }
            finally { ViewSettings.Dark = dark; ViewSettings.NeonCaret = enabled; }
        }


        // This is an interactive/CI test only. The headless entry never executes it.
        static void RainbowCaretNativeTests()
        {
            bool oldNeon = ViewSettings.NeonCaret, oldTrail = ViewSettings.CaretTrail;
            try
            {
                ViewSettings.NeonCaret = ViewSettings.CaretTrail = true;
                using (var viewer = Open("{\"text\":\"steady purple ribbon movement example\"}"))
                {
                    var box = Box(viewer); Native.ForceForeground(viewer.Handle); box.Focus();
                    box.Select(box.Text.IndexOf("steady", StringComparison.Ordinal) + 2, 0); box.Update(); Application.DoEvents();
                    var model = (RainbowCaretTrail)Field(box, "caretTrail");
                    string text = box.Text; Point scroll = box.ScrollPos; bool undo = box.CanUndo;
                    int start = box.SelectionStart;
                    Native.SendMessage(box.Handle, 0x0100, (IntPtr)Keys.Right, IntPtr.Zero);
                    Check(box.SelectionStart == start + 1, "arrow input still moves the real native insertion point immediately");
                    if (!(bool)Field(box, "neonAnimations") || SystemInformation.HighContrast)
                    { Check(!model.Active, "Windows reduced-animation/high-contrast preferences suppress the trail"); return; }
                    Check(model.Active, "real native keyboard movement, not only a synthetic bitmap, starts the ribbon");
                    var timer = (System.Windows.Forms.Timer)Field(box, "caretTrailTimer");
                    Check(timer != null && timer.Enabled, "movement arms the bounded-duration timer");
                    var deadline = Stopwatch.StartNew();
                    while (timer.Enabled && deadline.ElapsedMilliseconds < 2000) { System.Threading.Thread.Sleep(5); Application.DoEvents(); }
                    Check(!model.Active && !timer.Enabled, "idle stops the timer completely rather than polling indefinitely");
                    Native.SendMessage(box.Handle, 0x0100, (IntPtr)Keys.Right, IntPtr.Zero);
                    Check(model.Active && timer.Enabled, "typing/navigation after idle retains the last real caret baseline");
                    box.Select(box.SelectionStart, 3);
                    Check(!model.Active && !timer.Enabled && box.SelectionLength == 3, "EM_EXSETSEL selection cancels without changing its range");
                    box.Select(start, 0);
                    Native.SendMessage(box.Handle, 0x0100, (IntPtr)Keys.Right, IntPtr.Zero);
                    Native.SendMessage(box.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero);
                    Check(!model.Active && !timer.Enabled, "IME composition cancels motion before candidate positioning");
                    Native.SendMessage(box.Handle, 0x010E, IntPtr.Zero, IntPtr.Zero);
                    Native.SendMessage(box.Handle, 0x0100, (IntPtr)Keys.Right, IntPtr.Zero);
                    ViewSettings.CaretTrail = false; box.SetNeonCaret(true, Theme.Current.Dark, Theme.Current.Back);
                    Check(!model.Active && !timer.Enabled && (IntPtr)Field(box, "neonBitmap") != IntPtr.Zero,
                        "disabling only the trail retains the cached static purple-gradient caret");
                    Check(box.Text == text && box.CanUndo == undo && box.ScrollPos == scroll, "visual motion leaves text, undo and viewport unchanged");
                    box.Dispose();
                    Check(Field(box, "caretTrailTimer") == null, "disposing the editor releases its animation timer");
                }
            }
            finally { ViewSettings.NeonCaret = oldNeon; ViewSettings.CaretTrail = oldTrail; }
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
