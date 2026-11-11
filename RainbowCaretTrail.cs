using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace JsonRightClick
{
    /// <summary>纯几何状态：真实 caret 立即到位，只有身后的短彩带缓动；不读取控件、文档或系统时钟。</summary>
    sealed class RainbowCaretTrail
    {
        internal const int FrameInterval = 25, Segments = 8, MaximumLifetime = 160;
        bool known;
        Point target;
        PointF origin;
        int height, core;
        double started, duration;
        internal bool Active { get; private set; }

        internal static bool IsViewportMessage(int message)
        {
            switch (message)
            {
                case 0x020A: case 0x020E: case 0x0114: case 0x0115: case 0x00A1:
                case 0x00B5: case 0x00B6: case 0x04DE: case 0x000B:
                    return true;
                default: return false;
            }
        }
        internal static bool ResetsMotion(int message)
        {
            switch (message)
            {
                case 0x0005: case 0x0007: case 0x0008: case 0x000C: case 0x0018: case 0x001A: case 0x0030:
                case 0x010D: case 0x010F: case 0x0204: case 0x0207: case 0x02E0: case 0x031A:
                case 0x00B1: case 0x00C2: case 0x0437: case 0x0449: case 0x04E1:
                case 0x0300: case 0x0302: case 0x0303: case 0x0304: case 0x00C7:
                    return true;
                default: return false;
            }
        }
        internal static float LengthLimit(int height) { return Math.Min(96, Math.Max(36, height * 3f)); }
        internal void Reset() { known = Active = false; }
        internal void Seed(Point point, int lineHeight, int coreWidth)
        {
            target = point; height = lineHeight; core = coreWidth;
            known = lineHeight > 0 && coreWidth > 0; Active = false;
        }
        PointF Center(Point point) { return new PointF(point.X + core * .5f, point.Y + height * .5f); }

        // 返回是否产生了新运动。同位置的重复通知不会重置动画寿命。
        internal bool Observe(Point point, int lineHeight, int coreWidth, double now)
        {
            if (!known || lineHeight != height || coreWidth != core || double.IsNaN(now) || double.IsInfinity(now) || (Active && now < started))
            { Seed(point, lineHeight, coreWidth); return false; }
            if (target == point) return false;
            double dx = (double)point.X - target.X, dy = (double)point.Y - target.Y;
            float limit = LengthLimit(height);
            if (Math.Abs(dy) > height * 1.5 || dx * dx + dy * dy > limit * limit * 9)
            { Seed(point, lineHeight, coreWidth); return false; } // 远跳/跨多行不横穿编辑区。
            CaretTrailFrame previous;
            PointF tail = TryFrame(now, out previous) ? previous.Tail : Center(target);
            target = point;
            PointF head = Center(point);
            dx = tail.X - head.X; dy = tail.Y - head.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < .5) { Active = false; return false; }
            if (distance > limit)
            { tail = new PointF(head.X + (float)(dx / distance * limit), head.Y + (float)(dy / distance * limit)); distance = limit; }
            origin = tail; started = now;
            duration = Math.Min(MaximumLifetime, 65 + distance * 1.15);
            Active = true; return true;
        }
        internal bool TryFrame(double now, out CaretTrailFrame frame)
        {
            frame = new CaretTrailFrame();
            if (!Active) return false;
            if (double.IsNaN(now) || double.IsInfinity(now) || now < started || now - started >= duration)
            { Active = false; return false; }
            double progress = (now - started) / duration, remaining = Math.Pow(1 - progress, 3);
            PointF head = Center(target);
            var tail = new PointF(head.X + (float)((origin.X - head.X) * remaining), head.Y + (float)((origin.Y - head.Y) * remaining));
            double dx = head.X - tail.X, dy = head.Y - tail.Y;
            if (dx * dx + dy * dy < .16) { Active = false; return false; }
            frame = new CaretTrailFrame
            {
                Tail = tail, Head = head, Height = height, Core = core,
                Opacity = (float)(1 - progress),
                Bend = (float)(Math.Min(3, height * .12) * Math.Sin(progress * Math.PI) * (dx < 0 ? -1 : 1))
            };
            return true;
        }
        // 意外的自动滚屏也只清理原彩带和位移后的小区域，不为光标整窗重绘。
        internal static Rectangle ScrollDamage(Rectangle dirty, Point before, Point after, Rectangle client)
        {
            if (dirty.IsEmpty) return Rectangle.Empty;
            long x = (long)dirty.X + before.X - after.X, y = (long)dirty.Y + before.Y - after.Y;
            long left = Math.Max(client.Left, x), top = Math.Max(client.Top, y);
            long right = Math.Min(client.Right, x + dirty.Width), bottom = Math.Min(client.Bottom, y + dirty.Height);
            return right > left && bottom > top ? new Rectangle((int)left, (int)top, (int)(right - left), (int)(bottom - top)) : Rectangle.Empty;
        }
    }

    struct CaretTrailFrame
    {
        internal PointF Tail, Head;
        internal float Height, Core, Opacity, Bend;
        internal float TipWidth { get { return Math.Min(8, Math.Max(2.5f, Height * .26f)); } }
        internal PointF At(float t)
        {
            float dx = Head.X - Tail.X, dy = Head.Y - Tail.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            float curve = 4 * t * (1 - t) * Bend / (float)Math.Max(.01, distance);
            return new PointF(Tail.X + dx * t - dy * curve, Tail.Y + dy * t + dx * curve);
        }
        internal Rectangle Bounds
        {
            get
            {
                float margin = (TipWidth + 5) * .5f + Math.Abs(Bend) + 2;
                int left = (int)Math.Floor(Math.Min(Tail.X, Head.X) - margin), top = (int)Math.Floor(Math.Min(Tail.Y, Head.Y) - margin);
                int right = (int)Math.Ceiling(Math.Max(Tail.X, Head.X) + margin), bottom = (int)Math.Ceiling(Math.Max(Tail.Y, Head.Y) + margin);
                return Rectangle.FromLTRB(left, top, right, bottom);
            }
        }
        internal RectangleF CaretBounds
        {
            get { return new RectangleF(Head.X - Core * .5f - 1, Head.Y - Height * .5f - 1, Core + NeonCaretVisual.HaloWidth((int)Core) + 2, Height + 2); }
        }
    }

    static class RainbowCaretTrailRenderer
    {
        internal static void Paint(Graphics graphics, CaretTrailFrame frame, bool dark)
        {
            var state = graphics.Save();
            try
            {
                graphics.SetClip(frame.Bounds, CombineMode.Intersect);
                // 绝不画到系统 XOR caret 上，避免叠画/擦除造成闪烁或改变真实输入位置。
                graphics.ExcludeClip(Rectangle.Ceiling(frame.CaretBounds));
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var halo = new Pen(Color.Transparent))
                using (var core = new Pen(Color.Transparent))
                {
                    halo.StartCap = halo.EndCap = core.StartCap = core.EndCap = LineCap.Round;
                    PointF from = frame.Tail;
                    for (int i = 1; i <= RainbowCaretTrail.Segments; i++)
                    {
                        float t = (float)i / RainbowCaretTrail.Segments;
                        PointF to = frame.At(t);
                        Color color = NeonCaretVisual.ColorAt(1 - t, dark);
                        float width = .8f + (frame.TipWidth - .8f) * t;
                        halo.Color = Color.FromArgb((int)(22 * frame.Opacity), color); halo.Width = width + 5;
                        core.Color = Color.FromArgb((int)((40 + 105 * t) * frame.Opacity), color); core.Width = width;
                        graphics.DrawLine(halo, from, to); graphics.DrawLine(core, from, to);
                        from = to;
                    }
                }
            }
            finally { graphics.Restore(state); }
        }
    }
}
