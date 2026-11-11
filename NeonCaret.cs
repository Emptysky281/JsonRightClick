using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JsonRightClick
{
    /// <summary>系统 caret 位图使用 XOR：预先按背景编码亮芯和窄柔光，由 Windows 负责移动/闪烁。</summary>
    static class NeonCaretVisual
    {
        internal static int CoreWidth(int height, int nativeWidth)
        { return Math.Max(nativeWidth, Math.Max(2, (height + 7) / 10)); }
        internal static int HaloWidth(int core) { return Math.Max(2, Math.Min(8, core * 2)); }
        // 同一紫色系由浅到深；浅色主题降低亮度，避免亮芯在白底上发虚。
        static readonly Color[] Night = { Color.FromArgb(224, 199, 255), Color.FromArgb(201, 159, 255),
            Color.FromArgb(174, 115, 248), Color.FromArgb(149, 83, 230), Color.FromArgb(125, 61, 211) };
        static readonly Color[] Day = { Color.FromArgb(164, 105, 224), Color.FromArgb(143, 79, 210),
            Color.FromArgb(123, 56, 193), Color.FromArgb(104, 40, 174), Color.FromArgb(85, 30, 151) };
        internal static Color ColorAt(double position, bool dark)
        {
            Color[] colors = dark ? Night : Day;
            double scaled = (double.IsNaN(position) ? 0 : Math.Max(0, Math.Min(1, position))) * (colors.Length - 1);
            int at = Math.Min(colors.Length - 2, (int)scaled);
            return Mix(colors[at], colors[at + 1], scaled - at);
        }
        internal static Color CoreColor(bool dark) { return ColorAt(.5, dark); }
        internal static Color Mix(Color back, Color light, double amount)
        {
            return Color.FromArgb((int)Math.Round(back.R + (light.R - back.R) * amount),
                (int)Math.Round(back.G + (light.G - back.G) * amount), (int)Math.Round(back.B + (light.B - back.B) * amount));
        }
        internal static Bitmap Build(int height, int nativeWidth, Color back, bool dark)
        {
            if (height < 1 || height > 512) throw new ArgumentOutOfRangeException("height");
            if (nativeWidth < 1 || nativeWidth > 64) throw new ArgumentOutOfRangeException("nativeWidth");
            int core = CoreWidth(height, nativeWidth), halo = HaloWidth(core);
            var bitmap = new Bitmap(core + halo, height, PixelFormat.Format32bppRgb);
            for (int y = 0; y < height; y++)
            {
                Color light = ColorAt((double)y / Math.Max(1, height - 1), dark);
                for (int x = 0; x < bitmap.Width; x++)
                {
                    double end = y == 0 || y == height - 1 ? .62 : 1;
                    double glow = x < core ? 1 : .48 * Math.Pow((double)(bitmap.Width - x) / (halo + 1), 2);
                    Color pixel = Mix(back, light, end * glow);
                    bitmap.SetPixel(x, y, Color.FromArgb(back.R ^ pixel.R, back.G ^ pixel.G, back.B ^ pixel.B));
                }
            }
            return bitmap;
        }
    }

    sealed partial class CodeBox
    {
        IntPtr neonBitmap;
        int neonHeight, neonWidth, originalCaretWidth = 1, neonBitmapBuilds;
        bool neonEnabled, neonDark, neonStyleDirty = true, settingNeon;
        Color neonBackground;
        readonly RainbowCaretTrail caretTrail = new RainbowCaretTrail();
        System.Windows.Forms.Timer caretTrailTimer;
        Rectangle caretTrailBounds;
        bool neonTrailEnabled, neonAnimations, neonPainting, neonRedrawSuppressed;
        static readonly double TrailMilliseconds = 1000.0 / Stopwatch.Frequency;
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        static extern bool CaretSystemParameters(uint action, uint parameter, out int value, uint flags);
        [DllImport("user32.dll")]
        static extern bool CreateCaret(IntPtr window, IntPtr bitmap, int width, int height);
        [DllImport("user32.dll")]
        static extern bool DestroyCaret();
        [DllImport("user32.dll")]
        static extern bool GetCaretPos(out Native.POINT point);
        [DllImport("user32.dll")]
        static extern bool SetCaretPos(int x, int y);
        [DllImport("user32.dll")]
        static extern bool ShowCaret(IntPtr window);

        internal void SetNeonCaret(bool enabled, bool dark, Color background)
        {
            bool trail = ViewSettings.CaretTrail;
            if (neonEnabled != enabled || neonDark != dark || neonBackground != background)
            { neonStyleDirty = true; CancelCaretTrail(false); }
            if (neonTrailEnabled != trail) CancelCaretTrail(false);
            neonEnabled = enabled; neonDark = dark; neonBackground = background; neonTrailEnabled = trail;
            ReadCaretAnimationPreference();
            RefreshNeonCaret();
            SampleCaretTrail(false);
        }
        bool OwnCaret(out Native.GUITHREADINFO info)
        {
            info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(Native.GUITHREADINFO)) };
            return IsHandleCreated && Native.GetGUIThreadInfo(Native.GetCurrentThreadId(), ref info) && info.hwndCaret == Handle;
        }
        bool HasOurCaret(Native.GUITHREADINFO info)
        { return neonBitmap != IntPtr.Zero && info.r - info.l == neonWidth && info.b - info.t == neonHeight; }
        internal void RefreshNeonCaret()
        {
            if (settingNeon || !Focused || IsDisposed || Disposing) return;
            Native.GUITHREADINFO info;
            if (!OwnCaret(out info)) return;
            bool ours = HasOurCaret(info);
            int height = info.b - info.t;
            if (height <= 0 || height > 512) return;
            if (!ours) originalCaretWidth = Math.Max(1, info.r - info.l);
            bool active = neonEnabled && !SystemInformation.HighContrast && !imeComposing && originalCaretWidth <= 64;
            Native.POINT point;
            if (!GetCaretPos(out point)) return;
            settingNeon = true;
            try
            {
                if (!active)
                {
                    CancelCaretTrail(false);
                    if (ours && CreateCaret(Handle, IntPtr.Zero, originalCaretWidth, height))
                    {
                        SetCaretPos(point.X, point.Y);
                        if ((info.flags & 1 /* GUI_CARETBLINKING */) != 0) ShowCaret(Handle);
                    }
                    return;
                }
                // 选区期间由 RichEdit 决定是否隐藏 caret；不改可见性、不创建多余位图。
                if (SelectionLength != 0) { CancelCaretTrail(false); return; }
                IntPtr bitmap = neonBitmap;
                bool rebuilt = bitmap == IntPtr.Zero || neonStyleDirty || height != neonHeight;
                int width = NeonCaretVisual.CoreWidth(height, originalCaretWidth);
                width += NeonCaretVisual.HaloWidth(width);
                rebuilt |= width != neonWidth;
                if (rebuilt)
                {
                    using (var image = NeonCaretVisual.Build(height, originalCaretWidth, neonBackground, neonDark)) bitmap = image.GetHbitmap();
                }
                if (!ours || rebuilt)
                {
                    if (!CreateCaret(Handle, bitmap, width, height))
                    { if (rebuilt) Native.DeleteObject(bitmap); return; }
                    SetCaretPos(point.X, point.Y); // 柔光只向右延伸，不偏移输入位置或输入法候选坐标。
                    if ((info.flags & 1) != 0) ShowCaret(Handle);
                }
                if (rebuilt)
                {
                    if (neonBitmap != IntPtr.Zero) Native.DeleteObject(neonBitmap);
                    neonBitmap = bitmap; neonWidth = width; neonHeight = height; neonBitmapBuilds++;
                }
                neonStyleDirty = false;
            }
            finally { settingNeon = false; }
        }
        void ReleaseNeonCaret()
        {
            CancelCaretTrail(false); neonRedrawSuppressed = false;
            if (caretTrailTimer != null) { caretTrailTimer.Dispose(); caretTrailTimer = null; }
            if (neonBitmap == IntPtr.Zero) return;
            Native.GUITHREADINFO info;
            // 原生 caret 可能已经属于另一编辑器，不能销毁其他窗口的 caret。
            if (OwnCaret(out info) && HasOurCaret(info)) DestroyCaret();
            Native.DeleteObject(neonBitmap); neonBitmap = IntPtr.Zero;
            neonHeight = neonWidth = 0; neonStyleDirty = true;
        }
        static bool CaretMessage(int message)
        {
            switch (message)
            {
                case 0x0007: case 0x000F: case 0x000C: case 0x0005: case 0x001A:
                case 0x0100: case 0x0101: case 0x0102: case 0x010D: case 0x010E: case 0x010F:
                case 0x0201: case 0x0202: case 0x0203: case 0x0114: case 0x0115:
                case 0x00B1: case 0x00B6: case 0x00C2: case 0x0437: case 0x0444: case 0x0449: case 0x04DE:
                    return true;
                default: return false;
            }
        }
        static double CaretTime { get { return Stopwatch.GetTimestamp() * TrailMilliseconds; } }
        void ReadCaretAnimationPreference()
        {
            int value;
            neonAnimations = CaretSystemParameters(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, out value, 0)
                && value != 0 && !SystemInformation.HighContrast;
            if (!neonAnimations) CancelCaretTrail(false);
        }
        bool TrailAvailable
        {
            get { return neonEnabled && neonTrailEnabled && neonAnimations && !imeComposing && !neonRedrawSuppressed
                && Focused && Visible && !IsDisposed && !Disposing && !IsSmoothScrolling; }
        }
        void SampleCaretTrail(bool animate)
        {
            if (!TrailAvailable || viewportReadDepth != 0) { CancelCaretTrail(false); return; }
            Native.GUITHREADINFO info;
            if (!OwnCaret(out info) || !HasOurCaret(info) || SelectionLength != 0
                || info.l < 0 || info.t < 0 || info.l + neonWidth > ClientSize.Width || info.b > ClientSize.Height)
            { CancelCaretTrail(false); return; }
            var point = new Point(info.l, info.t);
            int core = NeonCaretVisual.CoreWidth(neonHeight, originalCaretWidth);
            if (!animate) { StopCaretTrailPaint(false); caretTrail.Seed(point, neonHeight, core); return; }
            if (caretTrail.Observe(point, neonHeight, core, CaretTime))
            {
                ScheduleCaretTrail();
                if (caretTrailTimer == null)
                {
                    caretTrailTimer = new System.Windows.Forms.Timer { Interval = RainbowCaretTrail.FrameInterval };
                    caretTrailTimer.Tick += delegate { ScheduleCaretTrail(); };
                }
                if (caretTrail.Active) caretTrailTimer.Start();
            }
            else if (!caretTrail.Active) StopCaretTrailPaint(false);
        }
        void ScheduleCaretTrail()
        {
            CaretTrailFrame frame;
            if (!TrailAvailable) { CancelCaretTrail(false); return; }
            if (!caretTrail.TryFrame(CaretTime, out frame)) { StopCaretTrailPaint(false); return; }
            Rectangle next = Rectangle.Intersect(ClientRectangle, frame.Bounds);
            if (!caretTrailBounds.IsEmpty) Invalidate(caretTrailBounds);
            if (!next.IsEmpty && next != caretTrailBounds) Invalidate(next);
            caretTrailBounds = next;
        }
        void StopCaretTrailPaint(bool eraseNow)
        {
            if (caretTrailTimer != null) caretTrailTimer.Stop(); // 空闲时真正停表，而非不断唤醒检查。
            Rectangle dirty = caretTrailBounds; caretTrailBounds = Rectangle.Empty;
            if (dirty.IsEmpty || !IsHandleCreated || IsDisposed || Disposing) return;
            Invalidate(dirty);
            // 滚屏前擦掉旧彩带，防止 RichEdit 的像素搬移把残影带到别处。
            if (eraseNow && !neonPainting && Visible && !neonRedrawSuppressed) Update();
        }
        void CancelCaretTrail(bool eraseNow) { caretTrail.Reset(); StopCaretTrailPaint(eraseNow); }
        void CaretTrailViewportChanged(Point before, Point after)
        {
            Rectangle shifted = RainbowCaretTrail.ScrollDamage(caretTrailBounds, before, after, ClientRectangle);
            CancelCaretTrail(false);
            if (!shifted.IsEmpty) Invalidate(shifted);
        }
        bool HasCaretTrailPaint { get { return !caretTrailBounds.IsEmpty; } }
        void PaintCaretTrail(Region clip)
        {
            CaretTrailFrame frame;
            if (!TrailAvailable || !caretTrail.TryFrame(CaretTime, out frame)) return;
            using (var graphics = Graphics.FromHwnd(Handle))
            {
                graphics.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Replace);
                RainbowCaretTrailRenderer.Paint(graphics, frame, neonDark);
                Rectangle drawn = Rectangle.Intersect(ClientRectangle, frame.Bounds);
                if (!drawn.IsEmpty) caretTrailBounds = caretTrailBounds.IsEmpty ? drawn : Rectangle.Union(caretTrailBounds, drawn);
            }
        }
        protected override void OnVisibleChanged(EventArgs e)
        { if (!Visible) CancelCaretTrail(false); base.OnVisibleChanged(e); }

        protected override void WndProc(ref Message message)
        {
            int kind = message.Msg;
            bool scroll = RainbowCaretTrail.IsViewportMessage(kind), reset = RainbowCaretTrail.ResetsMotion(kind);
            if (scroll) CancelCaretTrail(true);
            else if (reset || (kind == 0x0200 && (message.WParam.ToInt64() & 1) != 0)) CancelCaretTrail(false);
            if (kind == 0x000B) neonRedrawSuppressed = message.WParam == IntPtr.Zero;
            bool painting = neonPainting;
            if (kind == 0x000F) neonPainting = true;
            try { ProcessFocusMessage(ref message); }
            finally { neonPainting = painting; }
            // 原生 caret 只复用缓存；拖尾只采样真实导航/输入，不扫描全文，不改变选区或滚动。
            if (CaretMessage(kind)) RefreshNeonCaret();
            if (kind == 0x001A || kind == 0x0007) ReadCaretAnimationPreference();
            if (scroll || reset || kind == 0x010E) SampleCaretTrail(false);
            else if (kind == 0x0100 || kind == 0x0102 || kind == 0x0201 || kind == 0x0202 || kind == 0x0203) SampleCaretTrail(true);
        }
    }
}
