using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace JsonRightClick
{
    // ===================== 主题 =====================
    sealed class Theme
    {
        public bool Dark;
        public Color Highlight, Back, Fore, Bar, BarFore, Menu, Border, Hover, Checked, Accent, Muted, Ok, Warn, Err, InputBack;
        public Color[] Syntax; // 1 普通文字 2 标点 3 键名 4 字符串 5 数字 6 true/false/null

        public static readonly Theme Light = new Theme
        {
            Dark = false,
            Back = Color.White, Fore = Color.FromArgb(30, 30, 30),
            Bar = Color.FromArgb(243, 243, 243), BarFore = Color.FromArgb(30, 30, 30), Menu = Color.FromArgb(249, 249, 249),
            Border = Color.FromArgb(218, 218, 218), Hover = Color.FromArgb(222, 230, 245), Checked = Color.FromArgb(204, 220, 245),
            Accent = Color.FromArgb(37, 99, 235), Muted = Color.Gray, InputBack = Color.White, Highlight = Color.FromArgb(110, 255, 200, 0),
            Ok = Color.FromArgb(22, 128, 61), Warn = Color.FromArgb(194, 120, 3), Err = Color.FromArgb(200, 30, 30),
            Syntax = new[] { Color.Empty, Color.FromArgb(110, 110, 110), Color.FromArgb(60, 60, 60), Color.FromArgb(4, 81, 165),
                             Color.FromArgb(163, 21, 21), Color.FromArgb(9, 134, 88), Color.FromArgb(0, 0, 255) }
        };

        public static readonly Theme Night = new Theme
        {
            Dark = true,
            Back = Color.FromArgb(30, 30, 30), Fore = Color.FromArgb(212, 212, 212),
            Bar = Color.FromArgb(37, 37, 38), BarFore = Color.FromArgb(220, 220, 220), Menu = Color.FromArgb(43, 43, 44),
            Border = Color.FromArgb(62, 62, 66), Hover = Color.FromArgb(62, 62, 66), Checked = Color.FromArgb(9, 71, 113),
            Accent = Color.FromArgb(55, 148, 255), Muted = Color.FromArgb(150, 150, 150), InputBack = Color.FromArgb(49, 49, 50), Highlight = Color.FromArgb(90, 255, 190, 60),
            Ok = Color.FromArgb(115, 201, 145), Warn = Color.FromArgb(229, 192, 123), Err = Color.FromArgb(244, 135, 113),
            Syntax = new[] { Color.Empty, Color.FromArgb(150, 150, 150), Color.FromArgb(212, 212, 212), Color.FromArgb(156, 220, 254),
                             Color.FromArgb(206, 145, 120), Color.FromArgb(181, 206, 168), Color.FromArgb(86, 156, 214) }
        };

        public static Theme Current { get { return ViewSettings.Dark ? Night : Light; } }
    }

    /// <summary>查看窗口的显示设置（保存在 HKCU\Software\JsonRightClick）</summary>
    static class ViewSettings
    {
        public const string DefaultFont = "Consolas";
        public const float DefaultSize = 10.5f;
        /// <summary>诊断 / 截图模式：设置只保存在内存里，不写注册表</summary>
        public static bool NoPersist;
        static readonly Dictionary<string, string> mem = new Dictionary<string, string>();

        static string Get(string k, string def)
        {
            if (NoPersist) { string v; return mem.TryGetValue(k, out v) ? v : def; }
            return Settings.GetString(k, def);
        }

        static void Set(string k, string v) { if (NoPersist) mem[k] = v; else Settings.SetString(k, v); }
        static bool GetB(string k, bool def) { string v = Get(k, def ? "1" : "0"); return v == "1" || v == "True"; }
        static void SetB(string k, bool v) { if (NoPersist) mem[k] = v ? "1" : "0"; else Settings.Set(k, v); }

        public static bool Dark { get { return GetB("DarkMode", false); } set { SetB("DarkMode", value); } }
        public static bool Lenient { get { return GetB("Lenient", true); } set { SetB("Lenient", value); } }
        public static bool ShowTree { get { return GetB("ShowTree", false); } set { SetB("ShowTree", value); } }
        public static string MaskKeys { get { return Get("MaskKeys", Mask.DefaultKeys); } set { Set("MaskKeys", value); } }

        public static string FontName
        {
            get { string f = Get("FontName", DefaultFont); return FontStore.Exists(f) ? f : DefaultFont; }
            set { Set("FontName", value); }
        }

        public static float FontSize
        {
            get
            {
                float v;
                return float.TryParse(Get("FontSize", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 6 && v <= 72 ? v : DefaultSize;
            }
            set { Set("FontSize", value.ToString(CultureInfo.InvariantCulture)); }
        }
    }

    /// <summary>字体：导入的字体文件保存到程序所在文件夹的 Fonts 子文件夹，每次启动只在本程序内注册（不安装到系统）</summary>
    static class FontStore
    {
        public static readonly string Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts"); // 程序所在文件夹下的 Fonts
        static readonly Dictionary<string, string> imported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // 名称 → 文件
        static HashSet<string> installed;

        public static IEnumerable<string> Imported { get { return imported.Keys; } }

        public static void LoadAll()
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                foreach (var f in Directory.GetFiles(Dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".ttf" || ext == ".otf" || ext == ".ttc") Register(f);
                }
            }
            catch { }
        }

        /// <summary>导入字体文件，返回字体名称；失败抛出异常</summary>
        public static string Import(string file)
        {
            Directory.CreateDirectory(Dir);
            string dest = Path.Combine(Dir, Path.GetFileName(file));
            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(dest)) { try { File.Copy(file, dest, true); } catch (IOException) { } }
                else File.Copy(file, dest);
            }
            string name = Register(dest);
            if (name == null) throw new InvalidOperationException(L.T("无法识别这个字体文件（支持 .ttf / .otf / .ttc）"));
            return name;
        }

        static string Register(string file)
        {
            if (Native.AddFontResourceEx(file, 0x10 /* FR_PRIVATE */, IntPtr.Zero) == 0) return null;
            string name = null;
            try
            {
                using (var pfc = new System.Drawing.Text.PrivateFontCollection())
                {
                    pfc.AddFontFile(file);
                    if (pfc.Families.Length > 0)
                    {
                        var fam = pfc.Families[0];
                        try { name = fam.GetName(1033); } catch { name = fam.Name; }
                    }
                }
            }
            catch { }
            if (string.IsNullOrEmpty(name)) name = Native.FontResourceName(file);
            if (string.IsNullOrEmpty(name)) return null;
            imported[name] = file;
            return name;
        }

        public static bool IsInstalled(string name)
        {
            if (installed == null)
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var fc = new System.Drawing.Text.InstalledFontCollection())
                    foreach (var fam in fc.Families)
                    {
                        set.Add(fam.Name);
                        try { set.Add(fam.GetName(1033)); } catch { }
                    }
                installed = set;
            }
            return installed.Contains(name);
        }

        public static bool Exists(string name) { return !string.IsNullOrEmpty(name) && (imported.ContainsKey(name) || IsInstalled(name)); }
    }

    // ===================== 主题化的工具栏 / 菜单绘制 =====================
    sealed class ThemeRenderer : ToolStripProfessionalRenderer
    {
        readonly Theme th;

        public ThemeRenderer(Theme th) : base(new ThemeColors(th)) { this.th = th; RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (!(e.Item is ToolStripStatusLabel) || e.Item.ForeColor == SystemColors.ControlText) e.TextColor = th.BarFore;
            if (!e.Item.Enabled) e.TextColor = th.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = th.BarFore; base.OnRenderArrow(e); }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            r.Inflate(1, 1);
            using (var b = new SolidBrush(th.Checked)) e.Graphics.FillRectangle(b, r);
            using (var p = new Pen(th.BarFore, 1.6f))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float x = r.X + r.Width * 0.22f, y = r.Y + r.Height * 0.52f;
                e.Graphics.DrawLines(p, new[] { new PointF(x, y), new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f), new PointF(r.X + r.Width * 0.8f, r.Y + r.Height * 0.3f) });
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
                using (var p = new Pen(th.Border)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
    }

    sealed class ThemeColors : ProfessionalColorTable
    {
        readonly Theme t;
        public ThemeColors(Theme t) { this.t = t; UseSystemColors = false; }
        public override Color ToolStripGradientBegin { get { return t.Bar; } }
        public override Color ToolStripGradientMiddle { get { return t.Bar; } }
        public override Color ToolStripGradientEnd { get { return t.Bar; } }
        public override Color ToolStripBorder { get { return t.Bar; } }
        public override Color StatusStripGradientBegin { get { return t.Bar; } }
        public override Color StatusStripGradientEnd { get { return t.Bar; } }
        public override Color ToolStripDropDownBackground { get { return t.Menu; } }
        public override Color ImageMarginGradientBegin { get { return t.Menu; } }
        public override Color ImageMarginGradientMiddle { get { return t.Menu; } }
        public override Color ImageMarginGradientEnd { get { return t.Menu; } }
        public override Color MenuBorder { get { return t.Border; } }
        public override Color MenuItemBorder { get { return t.Hover; } }
        public override Color MenuItemSelected { get { return t.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return t.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return t.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return t.Menu; } }
        public override Color MenuItemPressedGradientEnd { get { return t.Menu; } }
        public override Color ButtonSelectedHighlight { get { return t.Hover; } }
        public override Color ButtonSelectedGradientBegin { get { return t.Hover; } }
        public override Color ButtonSelectedGradientMiddle { get { return t.Hover; } }
        public override Color ButtonSelectedGradientEnd { get { return t.Hover; } }
        public override Color ButtonSelectedBorder { get { return t.Hover; } }
        public override Color ButtonPressedGradientBegin { get { return t.Checked; } }
        public override Color ButtonPressedGradientMiddle { get { return t.Checked; } }
        public override Color ButtonPressedGradientEnd { get { return t.Checked; } }
        public override Color ButtonPressedBorder { get { return t.Checked; } }
        public override Color ButtonCheckedGradientBegin { get { return t.Checked; } }
        public override Color ButtonCheckedGradientMiddle { get { return t.Checked; } }
        public override Color ButtonCheckedGradientEnd { get { return t.Checked; } }
        public override Color ButtonCheckedHighlight { get { return t.Checked; } }
        public override Color ButtonCheckedHighlightBorder { get { return t.Checked; } }
        public override Color ButtonSelectedHighlightBorder { get { return t.Hover; } }
        public override Color CheckBackground { get { return t.Checked; } }
        public override Color CheckSelectedBackground { get { return t.Checked; } }
        public override Color CheckPressedBackground { get { return t.Checked; } }
        public override Color SeparatorDark { get { return t.Border; } }
        public override Color SeparatorLight { get { return t.Border; } }
        public override Color GripDark { get { return t.Border; } }
        public override Color GripLight { get { return t.Bar; } }
    }

    // ===================== 平滑滚动的编辑框 =====================
    /// <summary>
    /// RichTextBox 默认每格滚轮直接跳 3 行，显得一顿一顿的。这里接管滚轮：
    /// 累计目标位置，用约 8ms 一帧的缓动动画逐像素滚过去；快速连续滚动时速度会叠加。
    /// </summary>
    sealed partial class CodeBox : RichTextBox
    {
        public event Action<int> CtrlWheel;
        readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 8 };
        readonly Stopwatch clock = new Stopwatch();
        double target, current;
        bool animating;
        int lineAcc;

        /// <summary>搜索结果高亮：[起点, 长度]，按起点排序。当前选中的那个由系统选区颜色显示。</summary>
        public List<int[]> Highlights = new List<int[]>();
        public Color HighlightColor = Color.FromArgb(110, 255, 200, 0);
        public event Action ViewportChanged;
        int bracketOpen = -1, bracketClose = -1, bracketAnchor = -1;
        string openContext, closeContext;
        bool previewJumped;
        public int BracketOpen { get { return bracketOpen; } }
        public int BracketClose { get { return bracketClose; } }
        static float dpi;

        public CodeBox()
        {
            anim.Tick += delegate { Step(); };
            folding = new CodeFolding(this);
        }

        // 在 RichEdit 自己绘制完后，只在本次重绘区域内叠加半透明高亮，不改动文字格式（不影响撤销）
        void PaintHighlights(Region clip)
        {
            if (Highlights.Count == 0) return;
            Rectangle cb;
            using (var graphics = Graphics.FromHwnd(Handle)) cb = Rectangle.Round(clip.GetBounds(graphics));
            if (cb.Width <= 0 || cb.Height <= 0) return;
            int a = GetCharIndexFromPosition(new Point(0, Math.Max(0, cb.Top)));
            int lineA = GetLineFromCharIndex(a);
            int from = GetFirstCharIndexFromLine(Math.Max(0, lineA - 1));
            int b = GetCharIndexFromPosition(new Point(ClientSize.Width, Math.Min(ClientSize.Height - 1, cb.Bottom)));
            int lineB = GetLineFromCharIndex(b);
            int to = GetFirstCharIndexFromLine(lineB + 2);
            if (to < 0) to = TextLength;
            if (from < 0) from = 0;

            int lo = 0, hi = Highlights.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (Highlights[mid][0] + Highlights[mid][1] < from) lo = mid + 1; else hi = mid; }

            if (dpi == 0) using (var g0 = Graphics.FromHwnd(IntPtr.Zero)) dpi = g0.DpiX;
            int cw = Math.Max(4, (int)Math.Round(Native.FontCharWidthTwips(ViewSettings.FontName, ViewSettings.FontSize) * dpi / 1440f));
            int lh = LineHeight;
            int sel = SelectionStart, selLen = SelectionLength;

            using (var g = Graphics.FromHwnd(Handle))
            using (var br = new SolidBrush(HighlightColor))
            {
                g.SetClip(clip, CombineMode.Replace);
                for (int k = lo; k < Highlights.Count && Highlights[k][0] <= to; k++)
                {
                    int s = Highlights[k][0], e = s + Highlights[k][1];
                    if (s == sel && Highlights[k][1] == selLen) continue; // 当前项用系统选区色
                    int i = s;
                    while (i < e)
                    {
                        int visible = folding.SkipHidden(i);
                        if (visible != i) { i = Math.Min(e, visible); continue; }
                        var p0 = GetPositionFromCharIndex(i);
                        int line = GetLineFromCharIndex(i);
                        int j = i + 1;
                        while (j < e && !folding.IsHidden(j) && GetLineFromCharIndex(j) == line) j++;
                        int x1;
                        var pe = GetPositionFromCharIndex(j - 1);
                        x1 = pe.X + cw;
                        if (j < TextLength && j < e) { } else if (j < TextLength && GetLineFromCharIndex(j) == line) x1 = GetPositionFromCharIndex(j).X;
                        if (p0.Y + lh >= 0 && p0.Y <= ClientSize.Height)
                            g.FillRectangle(br, p0.X, p0.Y, Math.Max(2, x1 - p0.X), lh);
                        i = j;
                    }
                }
            }
        }

        // 括号提示与搜索高亮一样只叠加绘制，不改变 RTF、选区、剪贴板或撤销记录。
        public void SetBracketMatch(int open, int close, int anchor, string first, string last)
        {
            if (bracketOpen == open && bracketClose == close && bracketAnchor == anchor && openContext == first && closeContext == last) return;
            bracketOpen = open; bracketClose = close; bracketAnchor = anchor;
            openContext = first; closeContext = last;
            Invalidate();
        }

        public void ClearBracketMatch() { SetBracketMatch(-1, -1, -1, null, null); }

        Rectangle BracketRectangle(int index)
        {
            Point p = GetPositionFromCharIndex(index), next = GetPositionFromCharIndex(index + 1);
            if (dpi == 0) using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
            int width = next.Y == p.Y && next.X > p.X ? next.X - p.X :
                Math.Max(4, (int)Math.Round(Native.FontCharWidthTwips(ViewSettings.FontName, ViewSettings.FontSize) * dpi / 1440f));
            return new Rectangle(p.X - 1, p.Y, width + 2, LineHeight);
        }

        internal bool TryGetBracketPreview(out int target, out Rectangle bounds, out string text)
        {
            target = -1; bounds = Rectangle.Empty; text = null;
            if (bracketOpen < 0 || bracketClose >= TextLength || ClientSize.Width < 90 || ClientSize.Height < 80) return false;
            target = bracketAnchor == bracketOpen ? bracketClose : bracketOpen;
            Rectangle at = BracketRectangle(target);
            if (ClientRectangle.Contains(at)) return false;
            int height = TextRenderer.MeasureText("Ag", SystemFonts.MessageBoxFont).Height + 12;
            int y = at.Top < 0 ? 4 : ClientSize.Height - height - 4;
            bounds = new Rectangle(4, y, ClientSize.Width - 8, height);
            string arrow = at.Top < 0 ? "↑ " : at.Bottom > ClientSize.Height ? "↓ " : "↔ ";
            text = arrow + (target == bracketOpen ? openContext : closeContext) + L.T("  · 点击跳转", "  · Click to jump");
            return true;
        }

        void PaintBrackets(Region clip)
        {
            if (bracketOpen < 0 || bracketClose >= TextLength) return;
            var th = Theme.Current;
            using (var g = Graphics.FromHwnd(Handle))
            using (var fill = new SolidBrush(Color.FromArgb(th.Dark ? 85 : 55, th.Accent)))
            using (var pen = new Pen(th.Accent))
            {
                g.SetClip(clip, CombineMode.Replace);
                foreach (int index in new[] { bracketOpen, bracketClose })
                {
                    if (folding.IsHidden(index)) continue;
                    Rectangle r = BracketRectangle(index);
                    if (!ClientRectangle.IntersectsWith(r)) continue;
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                }
                int target; Rectangle bounds; string text;
                if (TryGetBracketPreview(out target, out bounds, out text))
                {
                    using (var background = new SolidBrush(th.Menu)) g.FillRectangle(background, bounds);
                    g.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                    bounds.Inflate(-7, -3);
                    TextRenderer.DrawText(g, text, SystemFonts.MessageBoxFont, bounds, th.BarFore,
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping);
                }
            }
        }

        void OnViewportChanged()
        {
            if (Highlights.Count > 0 || bracketOpen >= 0 || (folding != null && folding.CollapsedCount > 0)) Invalidate();
            var handler = ViewportChanged;
            if (handler != null) handler();
        }

        protected override void OnVScroll(EventArgs e) { base.OnVScroll(e); OnViewportChanged(); }
        protected override void OnHScroll(EventArgs e) { base.OnHScroll(e); OnViewportChanged(); }

        public Point ScrollPos
        {
            get { var p = new Native.POINT(); Native.SendMessage(Handle, 0x04DD /* EM_GETSCROLLPOS */, IntPtr.Zero, ref p); return new Point(p.X, p.Y); }
            set { var p = new Native.POINT { X = value.X, Y = value.Y }; Native.SendMessage(Handle, 0x04DE /* EM_SETSCROLLPOS */, IntPtr.Zero, ref p); OnViewportChanged(); }
        }

        int MaxScroll(out bool ok)
        {
            var si = new Native.SCROLLINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.SCROLLINFO)), fMask = 0x17 /* SIF_ALL */ };
            ok = Native.GetScrollInfo(Handle, 1 /* SB_VERT */, ref si);
            return Math.Max(0, si.nMax - Math.Max(0, (int)si.nPage - 1));
        }

        public int LineHeight { get { return Math.Max(8, (int)Math.Round(Native.FontLineHeight(ViewSettings.FontName, ViewSettings.FontSize))); } }

        protected override void WndProc(ref Message m)
        {
            if (HandleFoldingMessage(ref m)) return;
            switch (m.Msg)
            {
                case 0x020A: // WM_MOUSEWHEEL
                    {
                        int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                        if ((Control.ModifierKeys & Keys.Control) != 0) { StopSmooth(); if (CtrlWheel != null) CtrlWheel(delta); return; }
                        if (Wheel(delta)) return;
                        break;
                    }
                case 0x000F: // WM_PAINT
                    if (Highlights.Count > 0 || bracketOpen >= 0 || folding.CollapsedCount > 0)
                    {
                        IntPtr rgn = Native.CreateRectRgn(0, 0, 0, 0);
                        Native.GetUpdateRgn(Handle, rgn, false);
                        base.WndProc(ref m);
                        try { using (var clip = Region.FromHrgn(rgn)) { PaintHighlights(clip); folding.Paint(clip); PaintBrackets(clip); } }
                        catch { }
                        finally { Native.DeleteObject(rgn); }
                        return;
                    }
                    break;
                case 0x0201: // WM_LBUTTONDOWN：预览条点击不应先被 RichEdit 当作普通文本点击。
                    StopSmooth();
                    int target; Rectangle bounds; string text;
                    var point = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
                    if (TryGetBracketPreview(out target, out bounds, out text) && bounds.Contains(point))
                    {
                        previewJumped = true;
                        Focus(); Select(target, 0); ScrollToCaret(); OnViewportChanged();
                        return;
                    }
                    break;
                case 0x0202: // WM_LBUTTONUP
                    if (previewJumped) { previewJumped = false; return; }
                    break;
                case 0x0115: // WM_VSCROLL：拖动滚动条等，停止动画
                case 0x0114: // WM_HSCROLL
                case 0x0100: // WM_KEYDOWN
                    StopSmooth();
                    break;
            }
            base.WndProc(ref m);
        }

        bool Wheel(int delta)
        {
            bool ok;
            int max = MaxScroll(out ok);
            if (!ok) return false;
            if (max <= 0) return true;
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = 3;

            // 文档极长时 EM_SETSCROLLPOS 只有 16 位，改为按行平滑滚动
            if (max > 60000)
            {
                lineAcc += -delta * lines;
                int n = lineAcc / 120;
                if (n != 0) { lineAcc -= n * 120; Native.SendMessage(Handle, 0x00B6 /* EM_LINESCROLL */, IntPtr.Zero, (IntPtr)n); }
                return true;
            }

            if (!animating) { current = ScrollPos.Y; target = current; }
            target = Math.Max(0, Math.Min(max, target - delta / 120.0 * lines * LineHeight));
            if (!animating)
            {
                animating = true;
                Native.timeBeginPeriod(1);
                clock.Restart();
                anim.Start();
            }
            return true;
        }

        void Step()
        {
            double dt = Math.Max(1, clock.Elapsed.TotalMilliseconds);
            clock.Restart();
            double k = 1 - Math.Exp(-dt / 45.0); // 约 45ms 时间常数，ease-out
            current += (target - current) * k;
            if (Math.Abs(target - current) < 0.5) current = target;
            var p = ScrollPos;
            ScrollPos = new Point(p.X, (int)Math.Round(current));
            if (current == target) StopSmooth();
        }

        public void StopSmooth()
        {
            if (!animating) return;
            animating = false;
            anim.Stop();
            Native.timeEndPeriod(1);
        }

        protected override void Dispose(bool disposing)
        {
            StopSmooth();
            if (disposing) { anim.Dispose(); if (folding != null) folding.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 自定义换行规则：允许在任意字符处自动换行，
    /// 这样很长的 token / base64 会紧跟在键名后面开始，并按字符折行（不会整段被挤到下一行）。
    /// 双击选词、Ctrl+方向键仍按正常“单词”移动，文本内容本身不做任何改动。
    /// </summary>
    static class WordBreak
    {
        delegate int EditWordBreakProc(IntPtr text, int pos, int count, int code);
        static readonly EditWordBreakProc proc = Proc; // 保持引用，防止被 GC 回收

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, EditWordBreakProc l);

        public static void Attach(RichTextBox box)
        {
            SendMessage(box.Handle, 0x00D0 /* EM_SETWORDBREAKPROC */, IntPtr.Zero, proc);
        }

        static bool IsWordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        static int Proc(IntPtr text, int pos, int count, int code)
        {
            unsafe
            {
                char* s = (char*)text;
                switch (code)
                {
                    case 0: // WB_LEFT
                    case 4: // WB_MOVEWORDLEFT
                    case 6: // WB_LEFTBREAK
                        {
                            int i = pos;
                            while (i > 0 && !IsWordChar(s[i - 1])) i--;
                            while (i > 0 && IsWordChar(s[i - 1])) i--;
                            return i;
                        }
                    case 1: // WB_RIGHT
                    case 5: // WB_MOVEWORDRIGHT
                    case 7: // WB_RIGHTBREAK
                        {
                            int i = pos;
                            if (i < count && IsWordChar(s[i])) while (i < count && IsWordChar(s[i])) i++;
                            else if (i < count) i++;
                            while (i < count && s[i] == ' ') i++;
                            return i;
                        }
                    case 2: // WB_ISDELIMITER
                        return pos >= 0 && pos < count && (s[pos] == ' ' || s[pos] == '\t') ? 1 : 0;
                    case 3: // WB_CLASSIFY：每个字符都允许在其后断行
                        {
                            char c = pos >= 0 && pos < count ? s[pos] : ' ';
                            int cls = c == ' ' || c == '\t' ? 0x20 /* WBF_ISWHITE */ : 0;
                            return cls | 0x10 /* WBF_BREAKLINE */;
                        }
                }
                return 0;
            }
        }
    }

    // ===================== RTF 语法着色 =====================
    static class Rtf
    {
        /// <summary>生成带颜色、字体、悬挂缩进的 RTF。ranges 内按 JSON 着色，其余为普通文字。</summary>
        public static string Build(string text, List<int[]> ranges, Theme th, string fontName, float fontSize)
        {
            CharTwips = Math.Max(30, (int)Math.Round(Native.FontCharWidthTwips(fontName, fontSize)));
            var sb = new StringBuilder(text.Length * 2 + 512);
            sb.Append(@"{\rtf1\ansi\ansicpg936\deff0{\fonttbl{\f0\fnil\fcharset1 ");
            foreach (char c in fontName)
            {
                if (c < 0x80 && c != '\\' && c != '{' && c != '}' && c != ';') sb.Append(c);
                else sb.Append(@"\u").Append((int)(short)c).Append('?');
            }
            sb.Append(@";}}{\colortbl;");
            for (int i = 1; i < th.Syntax.Length; i++)
            {
                var c = th.Syntax[i];
                sb.Append(@"\red").Append(c.R).Append(@"\green").Append(c.G).Append(@"\blue").Append(c.B).Append(';');
            }
            sb.Append(@"}\viewkind4\uc1\pard\f0\fs").Append((int)Math.Round(fontSize * 2)).Append(@"\cf1 ");

            int pos = 0;
            foreach (var range in ranges)
            {
                if (range[0] > pos) Run(sb, 1, text, pos, range[0] - pos, false);
                Indent(sb, text, range[0]); // JSON 段首行
                Json(sb, text, range[0], range[0] + range[1]);
                pos = range[0] + range[1];
                if (pos < text.Length) sb.Append(@"\li0\fi0 ");
            }
            if (pos < text.Length) Run(sb, 1, text, pos, text.Length - pos, false);
            if (text.EndsWith("\n") || text.EndsWith("\r")) sb.Append(@"\par"); // RichEdit 会把最后一个段落标记吞掉，补一个保留结尾换行
            sb.Append('}');
            return sb.ToString();
        }

        static int CharTwips = 116;

        /// <summary>
        /// 给 JSON 的一行设置悬挂缩进：自动换行后的续行对齐到值开始的位置，
        /// 例如 "token": "eyJ0..." 很长时，续行和 "eyJ0 对齐，而不是回到最左边。
        /// 只改段落格式，不改文字本身，复制出来的内容不受影响。
        /// </summary>
        static void Indent(StringBuilder sb, string t, int lineStart)
        {
            int i = lineStart, n = t.Length, lead = 0;
            while (i < n && t[i] == ' ') { i++; lead++; }
            int col = lead;
            if (i < n && t[i] == '"')
            {
                // 跳过键名，看后面是不是 ": "
                int j = i + 1, width = 1;
                while (j < n && t[j] != '"' && t[j] != '\r' && t[j] != '\n')
                {
                    width += t[j] >= 0x1100 ? 2 : 1;
                    j += t[j] == '\\' ? 2 : 1;
                }
                if (j < n && t[j] == '"')
                {
                    int k = j + 1;
                    width++;
                    if (k < n && t[k] == ':')
                    {
                        k++; width++;
                        while (k < n && t[k] == ' ') { k++; width++; }
                        col = lead + width;
                    }
                }
            }
            col = Math.Min(col, 60);
            int li = col * CharTwips, fi = -li; // 首行从最左开始（行首空格本身就是缩进），续行对齐到 col
            sb.Append(@"\li").Append(li).Append(@"\fi").Append(fi).Append(' ');
        }

        /// <summary>按与 RTF 相同的规则切分 JSON，回调 (起点, 长度, 颜色编号)；大文件模式下用来按需着色</summary>
        public static void Tokenize(string t, int i, int end, Action<int, int, int> emit)
        {
            while (i < end)
            {
                char c = t[i];
                int j, color;
                if (c == '"')
                {
                    j = i + 1;
                    while (j < end && t[j] != '"') j += t[j] == '\\' ? 2 : 1;
                    j = Math.Min(j + 1, end);
                    int k = j;
                    while (k < end && t[k] == ' ') k++;
                    color = k < end && t[k] == ':' ? 3 : 4;
                }
                else if (c == '-' || (c >= '0' && c <= '9'))
                {
                    j = i + 1;
                    while (j < end && "0123456789.eE+-".IndexOf(t[j]) >= 0) j++;
                    color = 5;
                }
                else if (c >= 'a' && c <= 'z')
                {
                    j = i;
                    while (j < end && t[j] >= 'a' && t[j] <= 'z') j++;
                    color = 6;
                }
                else
                {
                    j = i;
                    while (j < end && t[j] != '"' && t[j] != '-' && !(t[j] >= '0' && t[j] <= '9') && !(t[j] >= 'a' && t[j] <= 'z')) j++;
                    color = 2;
                }
                emit(i, j - i, color);
                i = j;
            }
        }

        static void Json(StringBuilder sb, string t, int i, int end)
        {
            while (i < end)
            {
                char c = t[i];
                int j;
                if (c == '"')
                {
                    j = i + 1;
                    while (j < end && t[j] != '"') j += t[j] == '\\' ? 2 : 1;
                    j = Math.Min(j + 1, end);
                    int k = j;
                    while (k < end && t[k] == ' ') k++;
                    if (k < end && t[k] == ':') Run(sb, 3, t, i, j - i, true);
                    else Run(sb, 4, t, i, j - i, true);
                }
                else if (c == '-' || (c >= '0' && c <= '9'))
                {
                    j = i + 1;
                    while (j < end && "0123456789.eE+-".IndexOf(t[j]) >= 0) j++;
                    Run(sb, 5, t, i, j - i, true);
                }
                else if (c >= 'a' && c <= 'z')
                {
                    j = i;
                    while (j < end && t[j] >= 'a' && t[j] <= 'z') j++;
                    Run(sb, 6, t, i, j - i, true);
                }
                else
                {
                    j = i;
                    while (j < end && t[j] != '"' && t[j] != '-' && !(t[j] >= '0' && t[j] <= '9') && !(t[j] >= 'a' && t[j] <= 'z')) j++;
                    Run(sb, 2, t, i, j - i, true);
                }
                i = j;
            }
        }

        static void Run(StringBuilder sb, int color, string t, int start, int len, bool json)
        {
            sb.Append(@"\cf").Append(color).Append(' ');
            int end = start + len;
            for (int i = start; i < end; i++)
            {
                char c = t[i];
                if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < t.Length && t[i + 1] == '\n') i++;
                    sb.Append("\\par\r\n");
                    if (json) Indent(sb, t, i + 1);
                }
                else if (c == '\t') sb.Append(@"\tab ");
                else if (c < 0x80) sb.Append(c);
                else sb.Append(@"\u").Append((int)(short)c).Append('?');
            }
        }
    }
}
