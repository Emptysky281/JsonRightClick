using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JsonRightClick
{
    sealed class FoldRegion
    {
        public int Open, Close, Lines;
        public bool Collapsed;
        public string Label;
    }

    /// <summary>折叠只使用 RichEdit 的隐藏字符格式，原文和字符位置始终保持完整。</summary>
    sealed class CodeFolding : IDisposable
    {
        readonly CodeBox box;
        readonly FoldGutter gutter;
        string source = "";
        BracketIndex index;
        List<FoldRegion> regions = new List<FoldRegion>();
        List<FoldRegion> hidden = new List<FoldRegion>();
        bool changing, nativeHidden;
        int collapsedCount;
        public event Action Changed;
        public bool Changing { get { return changing; } }
        public int DocumentLength { get { return source.Length; } }
        public Control Gutter { get { return gutter; } }
        internal List<FoldRegion> Regions { get { return regions; } }
        internal List<FoldRegion> Hidden { get { return hidden; } }
        public int CollapsedCount { get { return collapsedCount; } }

        public CodeFolding(CodeBox box)
        {
            this.box = box;
            gutter = new FoldGutter(this, box);
            box.ViewportChanged += Refresh;
            box.Resize += delegate { Refresh(); };
        }

        public BracketIndex IndexFor(string text) { return source == text ? index : null; }

        public void Rebuild(string text, bool edited)
        {
            if (changing) return;
            var keep = new Dictionary<int, int>();
            int prefix = 0, oldEnd = source.Length, delta = text.Length - source.Length;
            bool same = source == text;
            if (!same && edited && nativeHidden)
            {
                int n = Math.Min(source.Length, text.Length);
                while (prefix < n && source[prefix] == text[prefix]) prefix++;
                int suffix = 0;
                while (suffix < n - prefix && source[source.Length - suffix - 1] == text[text.Length - suffix - 1]) suffix++;
                oldEnd = source.Length - suffix;
            }
            foreach (var r in regions)
            {
                if (!r.Collapsed) continue;
                if (same || (edited && r.Close < prefix)) keep[r.Open] = r.Close;
                else if (edited && r.Open >= oldEnd) keep[r.Open + delta] = r.Close + delta;
            }
            source = text;
            index = new BracketIndex(text);
            regions = index.FoldRegions();
            foreach (var r in regions)
            {
                int close;
                r.Collapsed = keep.TryGetValue(r.Open, out close) && close == r.Close;
            }
            Apply();
        }

        public FoldRegion RegionAt(int position, bool collapsedOnly)
        {
            FoldRegion found = null;
            int lineEnd = source.IndexOf('\n', Math.Min(Math.Max(0, position), source.Length));
            if (lineEnd < 0) lineEnd = source.Length;
            foreach (var r in regions)
            {
                if (collapsedOnly && !r.Collapsed) continue;
                if (r.Open == position || r.Close == position || r.Close + 1 == position) return r;
                if (r.Open <= position && r.Close >= position) found = r;
                // 键名所在行的冒号之后的对象 / 数组也可通过“折叠此层级”操作。
                if (r.Open > position && r.Open < lineEnd)
                {
                    int colon = source.IndexOf(':', Math.Max(0, position), r.Open - Math.Max(0, position));
                    if (colon >= 0) return r;
                }
                if (r.Open > position) break;
            }
            return found;
        }

        public bool Set(FoldRegion region, bool collapsed)
        {
            if (region == null || region.Collapsed == collapsed) return false;
            region.Collapsed = collapsed;
            Apply();
            return true;
        }
        public bool ToggleAt(int position)
        {
            var r = RegionAt(position, false);
            return r != null && Set(r, !r.Collapsed);
        }
        public bool SetAt(int position, bool collapsed) { return Set(RegionAt(position, !collapsed), collapsed); }
        public void SetAll(bool collapsed)
        {
            bool changed = false;
            foreach (var r in regions) if (r.Collapsed != collapsed) { r.Collapsed = collapsed; changed = true; }
            if (changed) Apply();
        }

        public bool IsHidden(int position) { return HiddenAt(position) != null; }
        FoldRegion HiddenAt(int position)
        {
            int lo = 0, hi = hidden.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2; var r = hidden[mid];
                if (position <= r.Open) hi = mid - 1;
                else if (position >= r.Close) lo = mid + 1;
                else return r;
            }
            return null;
        }
        public int SkipHidden(int position) { var r = HiddenAt(position); return r == null ? position : r.Close; }

        public void Reveal(int start, int length)
        {
            int end = start + Math.Max(0, length);
            bool changed = false;
            foreach (var r in regions)
                if (r.Collapsed && start < r.Close && end > r.Open)
                { r.Collapsed = false; changed = true; }
            if (changed) Apply();
        }

        public bool BeforeEdit(int direction)
        {
            if (changing || box.ReadOnly || hidden.Count == 0) return false;
            int start = box.SelectionStart, length = box.SelectionLength;
            if (length == 0)
            {
                if (direction < 0 && start > 0) { start--; length = 1; }
                else if (direction > 0) length = 1;
            }
            int end = start + length;
            bool changed = false;
            foreach (var r in regions)
                if (r.Collapsed && start <= r.Close && end > r.Open)
                { r.Collapsed = false; changed = true; }
            if (changed) Apply();
            return changed;
        }

        public void EachVisibleRange(int start, int length, Action<int, int> action)
        {
            int end = start + length, at = start;
            foreach (var r in hidden)
            {
                if (r.Close <= at) continue;
                if (r.Open >= end) break;
                int to = Math.Min(end, r.Open + 1);
                if (to > at) action(at, to - at);
                at = Math.Max(at, r.Close);
                if (at >= end) return;
            }
            if (at < end) action(at, end - at);
        }

        void Apply()
        {
            var visible = new List<FoldRegion>();
            int end = -1; collapsedCount = 0;
            foreach (var r in regions)
                if (r.Collapsed)
                {
                    collapsedCount++;
                    if (r.Open > end) { visible.Add(r); end = r.Close; }
                }
            if (nativeHidden || visible.Count > 0)
            {
                int sel = box.SelectionStart, len = box.SelectionLength;
                Point scroll = box.ScrollPos;
                bool modified = box.Modified;
                box.StopSmooth(); changing = true;
                Native.SendMessage(box.Handle, 0x000B /* WM_SETREDRAW */, IntPtr.Zero, IntPtr.Zero);
                try
                {
                    using (var undo = RichEditFold.UndoScope(box.Handle))
                    {
                        // 先清除旧的隐藏位，避免编辑、父子折叠或重新着色后留下一部分不可见内容。
                        RichEditFold.Hidden(box, 0, box.TextLength, false);
                        foreach (var r in visible) RichEditFold.Hidden(box, r.Open + 1, r.Close - r.Open - 1, true);
                        foreach (var r in visible)
                            if (len == 0 && sel > r.Open && sel < r.Close) { sel = r.Open; break; }
                        box.Select(Math.Min(sel, box.TextLength), Math.Min(len, Math.Max(0, box.TextLength - sel)));
                        // 插入点不能继承隐藏格式；它只影响随后输入，不会修改字符。
                        if (box.SelectionLength == 0) RichEditFold.InsertionVisible(box);
                        box.Modified = modified;
                        box.ScrollPos = scroll;
                    }
                }
                finally
                {
                    changing = false;
                    Native.SendMessage(box.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
                }
            }
            hidden = visible; nativeHidden = hidden.Count > 0;
            box.Invalidate();
            Refresh();
            var handler = Changed; if (handler != null) handler();
        }

        public Rectangle Placeholder(FoldRegion r)
        {
            if (!r.Collapsed || IsHidden(r.Open)) return Rectangle.Empty;
            Point a = box.CharPosition(r.Open), b = box.CharPosition(r.Close);
            if (a.Y != b.Y) return Rectangle.Empty;
            int width = Math.Max(6, box.CharacterWidth);
            return new Rectangle(a.X, a.Y, Math.Max(width * 2, b.X - a.X + width), box.LineHeight);
        }

        public FoldRegion PlaceholderAt(Point point)
        {
            if (hidden.Count == 0 || !box.ClientRectangle.Contains(point)) return null;
            int position = box.CharIndexAt(point);
            int lo = 0, hi = hidden.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2; var r = hidden[mid];
                if (position < r.Open) hi = mid - 1;
                else if (position > r.Close) lo = mid + 1;
                else { var bounds = Placeholder(r); return !bounds.IsEmpty && bounds.Contains(point) ? r : null; }
            }
            return null;
        }
        public IEnumerable<FoldRegion> VisibleRegions()
        {
            if (!box.IsHandleCreated || source.Length == 0) yield break;
            int top = box.CharIndexAt(Point.Empty);
            int bottom = box.CharIndexAt(new Point(box.ClientSize.Width, Math.Max(0, box.ClientSize.Height - 1)));
            int lo = 0, hi = regions.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (regions[mid].Open < top) lo = mid + 1; else hi = mid; }
            for (int i = Math.Max(0, lo - 1); i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.Open > bottom) break;
                int next = SkipHidden(r.Open);
                if (next != r.Open)
                {
                    while (i + 1 < regions.Count && regions[i + 1].Open < next) i++;
                    continue;
                }
                Point p = box.CharPosition(r.Open);
                if (p.Y + box.LineHeight >= 0 && p.Y < box.ClientSize.Height) yield return r;
            }
        }

        public void Paint(Region clip)
        {
            if (hidden.Count == 0) return;
            var th = Theme.Current;
            using (var g = Graphics.FromHwnd(box.Handle))
            using (var fill = new SolidBrush(th.Checked))
            using (var pen = new Pen(th.Accent))
            using (var font = new Font("Consolas", Math.Max(5, box.RenderFontSize * 0.65f)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
            using (var ink = new SolidBrush(th.BarFore))
            {
                g.SetClip(clip, CombineMode.Replace);
                foreach (var r in VisibleRegions())
                {
                    if (!r.Collapsed) continue;
                    var bounds = Placeholder(r);
                    if (bounds.IsEmpty || !bounds.IntersectsWith(box.ClientRectangle)) continue;
                    g.FillRectangle(fill, bounds);
                    g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                    g.DrawString(r.Label, font, ink, bounds, format);
                }
            }
        }
        public void Refresh() { if (!gutter.IsDisposed) gutter.Invalidate(); }
        public void Dispose() { box.ViewportChanged -= Refresh; gutter.Dispose(); }
    }

    sealed class FoldGutter : Control
    {
        readonly CodeFolding folding;
        readonly CodeBox box;
        readonly ToolTip tip = new ToolTip();
        readonly Dictionary<Rectangle, FoldRegion> hits = new Dictionary<Rectangle, FoldRegion>();
        public FoldGutter(CodeFolding folding, CodeBox box)
        {
            this.folding = folding; this.box = box;
            Dock = DockStyle.Left; Width = 22; Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            tip.SetToolTip(this, L.T("点击 − / + 折叠或展开此层级\n折叠后点击 {…} / […] 也可展开", "Click − / + to fold or expand\nClick {…} / […] to expand a folded block"));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); hits.Clear();
            var th = Theme.Current; e.Graphics.Clear(th.Bar);
            using (var border = new Pen(th.Border)) e.Graphics.DrawLine(border, Width - 1, 0, Width - 1, Height);
            box.PaintGutterScopeGuide(e.Graphics, Width);
            var rows = new HashSet<int>();
            using (var pen = new Pen(th.Muted))
            using (var fill = new SolidBrush(th.Back))
                foreach (var r in folding.VisibleRegions())
                {
                    int y = box.CharPosition(r.Open).Y + box.LineHeight / 2;
                    if (!rows.Add(y)) continue;
                    int size = Math.Max(9, Math.Min(13, box.LineHeight - 2));
                    var bounds = new Rectangle((Width - size) / 2, y - size / 2, size, size);
                    e.Graphics.FillRectangle(fill, bounds);
                    pen.Color = r.Collapsed ? th.Accent : th.Muted;
                    e.Graphics.DrawRectangle(pen, bounds);
                    e.Graphics.DrawLine(pen, bounds.Left + 3, y, bounds.Right - 3, y);
                    if (r.Collapsed) e.Graphics.DrawLine(pen, bounds.Left + size / 2, bounds.Top + 3, bounds.Left + size / 2, bounds.Bottom - 3);
                    var hit = bounds; hit.Inflate(3, 3); hits[hit] = r;
                }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (var hit in hits)
                if (hit.Key.Contains(e.Location)) { box.Focus(); folding.Set(hit.Value, !hit.Value.Collapsed); return; }
        }
        protected override void Dispose(bool disposing) { if (disposing) tip.Dispose(); base.Dispose(disposing); }
    }

    sealed partial class CodeBox
    {
        readonly CodeFolding folding;
        bool foldMouseUp;
        public Control FoldingGutter { get { return folding.Gutter; } }
        public bool FoldingFormatting { get { return folding != null && folding.Changing; } }
        public int CollapsedCount { get { return folding.CollapsedCount; } }
        public event Action FoldingChanged { add { folding.Changed += value; } remove { folding.Changed -= value; } }
        internal CodeFolding Folding { get { return folding; } }
        internal float DeviceDpiForFolding { get { if (dpi == 0) using (var g = CreateGraphics()) dpi = g.DpiX; return dpi; } }
        public void RebuildFolding(string text, bool edited) { folding.Rebuild(text, edited); }
        public bool FoldCurrent(bool collapsed) { return folding.SetAt(SelectionStart, collapsed); }
        public void FoldAll(bool collapsed) { folding.SetAll(collapsed); }
        public void RevealRange(int start, int length) { folding.Reveal(start, length); }
        public void PrepareFoldEdit() { folding.BeforeEdit(0); }
        internal BracketIndex FoldingIndexFor(string text) { return folding.IndexFor(text); }
        public bool IsFoldPlaceholder(Point point) { return folding.PlaceholderAt(point) != null; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = folding != null && IsFoldPlaceholder(e.Location) ? Cursors.Hand : Cursors.IBeam;
            base.OnMouseMove(e);
        }

        bool HandleFoldingMessage(ref Message m)
        {
            if (folding == null || FoldingFormatting) return false;
            if (m.Msg == 0x0201 /* WM_LBUTTONDOWN */)
            {
                var point = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
                var r = folding.PlaceholderAt(point);
                if (r != null) { foldMouseUp = true; Focus(); folding.Set(r, false); Select(r.Open, 0); return true; }
            }
            else if (m.Msg == 0x0202 && foldMouseUp) { foldMouseUp = false; return true; }
            else if (m.Msg == 0x0102 /* WM_CHAR */)
            {
                int key = m.WParam.ToInt32();
                bool revealed = folding.BeforeEdit(key == 8 ? -1 : key == 127 ? 1 : 0);
                if (revealed && (key == 8 || key == 127)) return true; // 边界删除先展开，下一次按键才真正删除。
            }
            else if (m.Msg == 0x0100 && m.WParam.ToInt32() == 0x2E /* Delete */)
            { if (folding.BeforeEdit(1)) return true; }
            else if (m.Msg == 0x010D /* WM_IME_STARTCOMPOSITION */ || m.Msg == 0x0300 || m.Msg == 0x0302 || m.Msg == 0x0303) folding.BeforeEdit(0);
            return false;
        }
    }

    /// <summary>隐藏格式不写入撤销栈。接口顺序及常量对应 Windows SDK TOM.h / RichEdit.h。</summary>
    static partial class RichEditFold
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CharFormat
        {
            public int cbSize; public uint dwMask, dwEffects; public int yHeight, yOffset, crTextColor;
            public byte bCharSet, bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szFaceName;
            public short wWeight, sSpacing; public int crBackColor, lcid, dwReserved; public short sStyle, wKerning;
            public byte bUnderlineType, bAnimation, bRevAuthor, bReserved1;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, ref CharFormat format);

        static void SetHidden(CodeBox box, bool hidden)
        {
            var format = new CharFormat { cbSize = Marshal.SizeOf(typeof(CharFormat)), dwMask = 0x100 /* CFM_HIDDEN */, dwEffects = hidden ? 0x100u : 0u };
            SendMessage(box.Handle, 0x0444 /* EM_SETCHARFORMAT */, (IntPtr)1 /* SCF_SELECTION */, ref format);
        }
        public static void Hidden(CodeBox box, int start, int length, bool hidden)
        { if (length <= 0) return; box.Select(start, length); SetHidden(box, hidden); }
        public static void InsertionVisible(CodeBox box) { SetHidden(box, false); }
        public static IDisposable UndoScope(IntPtr handle) { return new UndoSuspension(handle); }

        [ComImport, Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface ITextDocument
        {
            [PreserveSig] int GetName(out IntPtr name);
            [PreserveSig] int GetSelection(out IntPtr selection);
            [PreserveSig] int GetStoryCount(out int count);
            [PreserveSig] int GetStoryRanges(out IntPtr ranges);
            [PreserveSig] int GetSaved(out int value);
            [PreserveSig] int SetSaved(int value);
            [PreserveSig] int GetDefaultTabStop(out float value);
            [PreserveSig] int SetDefaultTabStop(float value);
            [PreserveSig] int New();
            [PreserveSig] int Open(IntPtr variant, int flags, int codePage);
            [PreserveSig] int Save(IntPtr variant, int flags, int codePage);
            [PreserveSig] int Freeze(out int count);
            [PreserveSig] int Unfreeze(out int count);
            [PreserveSig] int BeginEditCollection();
            [PreserveSig] int EndEditCollection();
            [PreserveSig] int Undo(int count, out int result);
            [PreserveSig] int Redo(int count, out int result);
            [PreserveSig] int Range(int start, int end, out ITextRange range);
        }
        sealed class UndoSuspension : IDisposable
        {
            ITextDocument document;
            public ITextDocument Document { get { return document; } }
            public UndoSuspension(IntPtr handle)
            {
                IntPtr memory = Marshal.AllocHGlobal(IntPtr.Size), ole = IntPtr.Zero;
                try
                {
                    Marshal.WriteIntPtr(memory, IntPtr.Zero);
                    Native.SendMessage(handle, 0x043C /* EM_GETOLEINTERFACE */, IntPtr.Zero, memory);
                    ole = Marshal.ReadIntPtr(memory);
                    if (ole == IntPtr.Zero) throw new InvalidOperationException("RichEdit Text Object Model is unavailable.");
                    document = (ITextDocument)Marshal.GetObjectForIUnknown(ole);
                    int done;
                    Marshal.ThrowExceptionForHR(document.Undo(-9999995 /* tomSuspend */, out done));
                }
                catch { if (document != null) { Marshal.ReleaseComObject(document); document = null; } throw; }
                finally { if (ole != IntPtr.Zero) Marshal.Release(ole); Marshal.FreeHGlobal(memory); }
            }
            public void Dispose()
            {
                if (document == null) return;
                try { int done; document.Undo(-9999994 /* tomResume */, out done); }
                finally { Marshal.ReleaseComObject(document); document = null; }
            }
        }
    }
}
