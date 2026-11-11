using System;
using System.Drawing;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace JsonRightClick
{
    enum Sel { Unknown, Yes, No }

    /// <summary>只记录右键实际命中的控件，不从其他窗口或隐藏编辑框借用选区。</summary>
    sealed class SelectionTarget
    {
        public readonly IntPtr Root, Control;
        public readonly Point Point;
        public readonly IntPtr Focus;
        readonly long captured = Stopwatch.GetTimestamp();
        public Task<Sel> BeforeMenu;
        public SelectionTarget(IntPtr root, IntPtr control, Point point) : this(root, control, point, IntPtr.Zero) { }
        SelectionTarget(IntPtr root, IntPtr control, Point point, IntPtr focus) { Root = root; Control = control; Point = point; Focus = focus; }
        internal static bool FreshAge(double milliseconds) { return milliseconds >= 0 && milliseconds <= 1500; }
        public bool Fresh { get { return FreshAge((Stopwatch.GetTimestamp() - captured) * 1000.0 / Stopwatch.Frequency); } }
        public bool IsCurrent
        {
            get { return Root != IntPtr.Zero && Control != IntPtr.Zero && Native.Root(Control) == Root
                && ForegroundBelongsTo(Root); }
        }
        internal static bool ForegroundBelongsTo(IntPtr root)
        {
            IntPtr foreground = Native.GetForegroundWindow();
            return root != IntPtr.Zero && (Native.Root(foreground) == root
                || (foreground != IntPtr.Zero && Native.GetAncestor(foreground, 3 /* GA_ROOTOWNER */) == Native.GetAncestor(root, 3)));
        }
        public static SelectionTarget Capture(IntPtr root, Point point)
        { return Capture(root, Native.WindowFromPoint(new Native.POINT { X = point.X, Y = point.Y }), point); }
        public static SelectionTarget Capture(IntPtr root, IntPtr control, Point point)
        {
            // 使用鼠标钩子已经拿到的原 HWND，UI 队列稍晚处理时菜单可能已盖住命中点。
            uint pid; uint thread = Native.GetWindowThreadProcessId(root, out pid);
            var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO)) };
            IntPtr focus = thread != 0 && Native.GetGUIThreadInfo(thread, ref info) && Native.Root(info.hwndFocus) == root ? info.hwndFocus : IntPtr.Zero;
            var target = new SelectionTarget(root, control, point, focus);
            return target.IsCurrent ? target : null;
        }
    }

    /// <summary>提供程序可能卡住；全局最多一项检测，超时后不继续堆积后台任务。</summary>
    sealed class SelectionProbeRunner
    {
        int running;
        public Task<Sel> Run(Func<Sel> check)
        {
            if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return Task.FromResult(Sel.Unknown);
            return Task.Run(() =>
            {
                try { return check(); }
                catch { return Sel.Unknown; }
                finally { Interlocked.Exchange(ref running, 0); }
            });
        }
    }

    /// <summary>必须从文本控件得到非空选区；不猜测拖动、双击、Ctrl+A 或剪贴板变化的含义。</summary>
    static class SelectionProbe
    {
        [ThreadStatic] public static string LastInfo;
        static readonly SelectionProbeRunner runner = new SelectionProbeRunner();
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
        static extern IntPtr GetSelection(IntPtr handle, uint message, ref uint start, ref uint end, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
        static extern IntPtr ReadEditText(IntPtr handle, uint message, IntPtr capacity, StringBuilder text, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        static extern int GetStyle(IntPtr handle, int index);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")]
        static extern bool IsChild(IntPtr parent, IntPtr child);

        // 是否有真实文字选区，与选中文字是否为 JSON 无关；不能把解析校验加到显示条件里。
        public static bool ShouldShow(Sel selection) { return selection == Sel.Yes; }
        public static Task<Sel> Begin(SelectionTarget target)
        { return runner.Run(() => Check(target)); }

        internal static bool NativeTextClass(string cls)
        {
            return cls == "Edit" || cls.StartsWith("Scintilla", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase)
                || cls == "RICHEDIT" || cls.StartsWith("RichEdit20", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("RICHEDIT50", StringComparison.OrdinalIgnoreCase)
                || cls.StartsWith("WindowsForms10.RichEdit", StringComparison.OrdinalIgnoreCase);
        }
        public static bool RecheckAfterMenu(SelectionTarget target) { return NativeTextClass(Native.ClassName(target.Control)); }

        // 仅文本控件才发送已知的选区消息；EM_GETSEL 使用完整 32 位端点，不依赖截断的返回值。
        internal static Sel NativeSelection(IntPtr handle)
        {
            string cls = Native.ClassName(handle);
            LastInfo = "class=" + cls;
            if (cls.StartsWith("Scintilla", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr result;
                return Native.Send(handle, 2650 /* SCI_GETSELECTIONEMPTY */, out result)
                    ? (result == IntPtr.Zero ? Sel.Yes : Sel.No) : Sel.Unknown;
            }
            if (!NativeTextClass(cls)) return Sel.Unknown;
            if ((GetStyle(handle, -16 /* GWL_STYLE */) & 0x20 /* ES_PASSWORD */) != 0) return Sel.No;
            uint start = uint.MaxValue, end = uint.MaxValue;
            IntPtr ignored;
            if (GetSelection(handle, 0x00B0 /* EM_GETSEL */, ref start, ref end, 2 /* SMTO_ABORTIFHUNG */, 150, out ignored) == IntPtr.Zero)
                return Sel.Unknown;
            if (start == uint.MaxValue || end == uint.MaxValue) return Sel.Unknown;
            return start != end ? Sel.Yes : Sel.No;
        }
        internal static string NativeSelectedText(IntPtr handle)
        {
            string cls = Native.ClassName(handle);
            // 标准 Edit 的 WM_GETTEXT 和 EM_GETSEL 使用相同的 CRLF 位置。RichEdit 的
            // 换行/隐藏字符坐标规则不同，必须交给其文本提供程序，不能拿字符串下标猜。
            if (cls != "Edit" && !cls.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase)) return null;
            if (NativeSelection(handle) != Sel.Yes) return null;
            uint start = uint.MaxValue, end = uint.MaxValue; IntPtr ignored, length;
            if (GetSelection(handle, 0x00B0, ref start, ref end, 2, 150, out ignored) == IntPtr.Zero || start == end) return null;
            if (!Native.Send(handle, 0x000E /* WM_GETTEXTLENGTH */, out length)) return null;
            long count = length.ToInt64();
            if (count <= 0 || count > SelectedTextReader.MaxCharacters) return null;
            var text = new StringBuilder((int)count + 1);
            if (ReadEditText(handle, 0x000D /* WM_GETTEXT */, (IntPtr)text.Capacity, text, 2, 150, out ignored) == IntPtr.Zero) return null;
            uint checkStart = uint.MaxValue, checkEnd = uint.MaxValue;
            if (GetSelection(handle, 0x00B0, ref checkStart, ref checkEnd, 2, 150, out ignored) == IntPtr.Zero
                || start != checkStart || end != checkEnd || end > text.Length || start > end) return null;
            return text.ToString((int)start, (int)(end - start));
        }

        internal static bool NonEmptyTextRange(int endpointOrder, string sample)
        {
            // 光标是退化范围。图标/图片对象的占位符不等于选中了可解析的文字。
            if (endpointOrder >= 0 || string.IsNullOrEmpty(sample)) return false;
            foreach (char c in sample) if (c != '\uFFFC' && c != '\0') return true;
            return false;
        }
        static bool OwnedBy(AutomationElement element, IntPtr root)
        {
            for (int i = 0; element != null && i < 16; i++)
            {
                int handle = element.Current.NativeWindowHandle;
                if (handle != 0) return Native.Root(new IntPtr(handle)) == root;
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return false;
        }
        internal static bool NearSelection(System.Windows.Rect[] rectangles, Point point)
        {
            // 部分提供程序不返回选区矩形；仍须有真实非空范围，不能以矩形缺失当作“无选区”。
            if (rectangles == null || rectangles.Length == 0) return true;
            foreach (var rectangle in rectangles)
            {
                double x = rectangle.X, y = rectangle.Y, w = rectangle.Width, h = rectangle.Height;
                if (w > 0 && h > 0 && point.X >= x - 6 && point.X <= x + w + 6 && point.Y >= y - 6 && point.Y <= y + h + 6) return true;
            }
            return false;
        }
        internal static bool ContainsTextHit(int startComparedToHit, int endComparedToHit)
        { return startComparedToHit <= 0 && endComparedToHit > 0; }

        static Sel ElementSelection(AutomationElement element, Point? point, out bool blocked, Action<string> capture = null)
        {
            blocked = false;
            var info = element.Current;
            bool contains = !point.HasValue || info.BoundingRectangle.Contains(point.Value.X, point.Value.Y);
            if (info.IsOffscreen || !contains) return Sel.Unknown;
            if (info.IsPassword) { blocked = true; return Sel.No; }
            object pattern;
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return Sel.Unknown;
            LastInfo += " text-pattern";
            foreach (var range in ((TextPattern)pattern).GetSelection())
            {
                int order = range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End);
                if (order >= 0 || !NonEmptyTextRange(order, range.GetText(32))) continue;
                if (point.HasValue)
                {
                    System.Windows.Rect[] rectangles = null;
                    try { rectangles = range.GetBoundingRectangles(); } catch { }
                    if (!NearSelection(rectangles, point.Value)) continue;
                    if (rectangles == null || rectangles.Length == 0)
                    {
                        try
                        {
                            var hit = ((TextPattern)pattern).RangeFromPoint(new System.Windows.Point(point.Value.X, point.Value.Y));
                            if (!ContainsTextHit(range.CompareEndpoints(TextPatternRangeEndpoint.Start, hit, TextPatternRangeEndpoint.Start),
                                range.CompareEndpoints(TextPatternRangeEndpoint.End, hit, TextPatternRangeEndpoint.Start))) continue;
                        }
                        catch
                        {
                            // 缺少矩形与点命中接口的宽泛 Document 不能借用其他位置的选区。
                            if (info.ControlType != ControlType.Edit && info.ControlType != ControlType.Text) continue;
                        }
                    }
                }
                if (capture != null)
                {
                    // 在发送 Esc / Ctrl+C 之前读取同一个已验证范围的完整内容；不按 JSON 形状筛选。
                    string full = range.GetText(SelectedTextReader.MaxCharacters + 1);
                    if (SelectedTextReader.Accept(full)) capture(full);
                }
                return Sel.Yes;
            }
            return Sel.No;
        }
        static Sel AutomationSelection(AutomationElement element, IntPtr root, Point? point, out bool blocked, Action<string> capture = null)
        {
            blocked = false;
            if (element == null || !OwnedBy(element, root)) return Sel.Unknown;
            bool supported = false;
            for (int i = 0; element != null && i < 12; i++)
            {
                int handle = element.Current.NativeWindowHandle;
                if (handle != 0 && Native.Root(new IntPtr(handle)) != root) break;
                var result = ElementSelection(element, point, out blocked, capture);
                if (blocked || result == Sel.Yes) return result;
                supported |= result == Sel.No;
                // HTML 文字叶节点/装饰子元素可能无选区；选区实际由父 Document/编辑器维护。
                if (handle != 0 && new IntPtr(handle) == root) break;
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return supported ? Sel.No : Sel.Unknown;
        }
        static Sel DescendantSelection(AutomationElement host, IntPtr root, Point point, out bool blocked, Action<string> capture = null)
        {
            blocked = false;
            if (host == null || !OwnedBy(host, root)) return Sel.Unknown;
            var stack = new Stack<KeyValuePair<AutomationElement, int>>();
            stack.Push(new KeyValuePair<AutomationElement, int>(host, 0));
            var watch = Stopwatch.StartNew(); int visited = 0;
            while (stack.Count > 0 && visited++ < 48 && watch.ElapsedMilliseconds < 180)
            {
                var item = stack.Pop(); var element = item.Key;
                var bounds = element.Current.BoundingRectangle;
                if (element.Current.IsOffscreen || !bounds.Contains(point.X, point.Y)) continue;
                var result = ElementSelection(element, point, out blocked, capture);
                if (blocked || result == Sel.Yes) return result;
                if (item.Value >= 6) continue;
                var child = TreeWalker.RawViewWalker.GetFirstChild(element);
                for (int siblings = 0; child != null && siblings < 24; siblings++)
                {
                    if (child.Current.BoundingRectangle.Contains(point.X, point.Y))
                        stack.Push(new KeyValuePair<AutomationElement, int>(child, item.Value + 1));
                    child = TreeWalker.RawViewWalker.GetNextSibling(child);
                }
            }
            return Sel.Unknown;
        }
        internal static Sel Check(SelectionTarget target, Action<string> capture = null)
        {
            LastInfo = "no-target";
            if (target == null || !target.IsCurrent || !IsWindowVisible(target.Control)) return Sel.Unknown;
            bool captured = false;
            Action<string> read = capture == null ? null : (Action<string>)(value => { if (SelectedTextReader.Accept(value)) { captured = true; capture(value); } });
            var result = NativeSelection(target.Control);
            if (result == Sel.No || (result == Sel.Yes && read == null)) return result;
            if (result == Sel.Yes)
            {
                string value = NativeSelectedText(target.Control);
                if (SelectedTextReader.Accept(value)) { read(value); return Sel.Yes; }
            }
            if (target.Focus != IntPtr.Zero && target.Focus != target.Control && IsChild(target.Focus, target.Control))
            {
                result = NativeSelection(target.Focus);
                if (result == Sel.No || (result == Sel.Yes && read == null)) return result;
                if (result == Sel.Yes)
                {
                    string value = NativeSelectedText(target.Focus);
                    if (SelectedTextReader.Accept(value)) { read(value); return Sel.Yes; }
                }
            }
            bool blocked;
            // Chrome/Electron/Firefox 可能只有 MSAA/IAccessible2，没有 UIA TextPattern。
            result = LegacyTextSelection.AtPoint(target.Root, target.Point, out blocked, read);
            if (blocked || (result == Sel.Yes && (read == null || captured))) { LastInfo += " ia2-point"; return result; }
            result = LegacyTextSelection.AtWindow(target.Control, target.Root, target.Point, out blocked, read);
            if (blocked || (result == Sel.Yes && (read == null || captured))) { LastInfo += " ia2-window"; return result; }
            try
            {
                result = AutomationSelection(AutomationElement.FromPoint(new System.Windows.Point(target.Point.X, target.Point.Y)), target.Root, target.Point, out blocked, read);
                if (blocked || (result == Sel.Yes && (read == null || captured))) return result;
            }
            catch { }
            try
            {
                // 从实际原控件定位虚拟 Document；只在命中点所在分支中有界查找，不扫描整窗其他编辑器。
                var host = AutomationElement.FromHandle(target.Control);
                result = AutomationSelection(host, target.Root, target.Point, out blocked, read);
                if (blocked || (result == Sel.Yes && (read == null || captured))) return result;
                result = DescendantSelection(host, target.Root, target.Point, out blocked, read);
                if (blocked || (result == Sel.Yes && (read == null || captured))) return result;
            }
            catch { }
            try
            {
                // WPF/浏览器的焦点可能是没有 HWND 的虚拟文本元素；矩形和原窗口归属仍须同时匹配。
                result = AutomationSelection(AutomationElement.FocusedElement, target.Root, target.Point, out blocked, read);
                if (blocked || (result == Sel.Yes && (read == null || captured))) return result;
            }
            catch { }
            return Sel.Unknown;
        }
        // --probe 诊断入口：不遍历任意子控件，也不模拟复制。
        public static Sel Check(IntPtr root)
        {
            LastInfo = "no-focus";
            uint pid; uint thread = Native.GetWindowThreadProcessId(root, out pid);
            var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO)) };
            if (root == IntPtr.Zero || thread == 0 || !Native.GetGUIThreadInfo(thread, ref info) || Native.Root(info.hwndFocus) != root)
                return Sel.Unknown;
            var native = NativeSelection(info.hwndFocus);
            if (native != Sel.Unknown) return native;
            try { bool blocked; return AutomationSelection(AutomationElement.FocusedElement, root, null, out blocked); }
            catch { return Sel.Unknown; }
        }
    }
}
