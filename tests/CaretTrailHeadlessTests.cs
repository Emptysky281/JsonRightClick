using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace JsonRightClick
{
    // This entry point deliberately never constructs a Form/Control, starts App.Main,
    // activates a window, sends input, accesses the clipboard, or persists user settings.
    static class CaretTrailHeadlessTests
    {
        static int assertions;
        static void Check(bool value, string message)
        { assertions++; if (!value) throw new Exception(message); }
        static Color Back(bool dark) { return dark ? Color.FromArgb(30, 30, 30) : Color.White; }
        static int Distance(Color a, Color b) { return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); }
        static Color Decode(Color back, Color mask) { return Color.FromArgb(back.R ^ mask.R, back.G ^ mask.G, back.B ^ mask.B); }
        static double Length(PointF a, PointF b)
        { double x = a.X - b.X, y = a.Y - b.Y; return Math.Sqrt(x * x + y * y); }
        static void Run(string name, Action action)
        { int before = assertions; action(); Console.WriteLine("PASS " + name + " (" + (assertions - before) + " assertions)"); }
        static int Main(string[] args)
        {
            try
            {
                Run("purple hue family and smooth light-to-deep gradient", PurplePaletteTests);
                Run("cached XOR purple-gradient bitmap and theme contrast", BitmapTests);
                Run("bounded, retargetable easing and finite lifetime", MotionTests);
                Run("interrupt/reset policy and pixel-scroll damage", InterruptionTests);
                Run("dense movement geometry and bounded state", StressTests);
                Run("clipped alpha rendering, caret exclusion, graphics state", RenderingTests);
                Run("offscreen render cost and GDI resource lifetime", CostTests);
                if (args.Length > 0) Previews(Path.GetFullPath(args[0]));
                Console.WriteLine("PASS total=" + assertions + "; headless: no windows, focus changes, input, clipboard or settings writes.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        static void PurplePaletteTests()
        {
            foreach (bool dark in new[] { true, false })
            {
                Color previous = NeonCaretVisual.ColorAt(0, dark);
                for (int i = 0; i <= 256; i++)
                {
                    Color color = NeonCaretVisual.ColorAt(i / 256.0, dark);
                    Check(color.B > color.R && color.R > color.G && color.GetHue() >= 260 && color.GetHue() <= 285,
                        "caret and ribbon share only violet/lavender hues, never rainbow cyan, green, orange or red");
                    Check(color.R <= previous.R && color.G <= previous.G && color.B <= previous.B,
                        "purple gradient smoothly changes from light lavender to deep violet");
                    Check(Distance(color, previous) <= 4, "adjacent purple samples have no abrupt color bands");
                    previous = color;
                }
                Check(Distance(NeonCaretVisual.ColorAt(0, dark), previous) > 200, "both themes retain a visible purple lightness gradient");
            }
        }
        static void BitmapTests()
        {
            foreach (bool dark in new[] { true, false })
                foreach (int h in new[] { 1, 10, 19, 28, 44, 96, 160, 512 })
                    foreach (int native in new[] { 1, 3, 6, 20, 64 })
                    {
                        Color back = Back(dark);
                        using (var bitmap = NeonCaretVisual.Build(h, native, back, dark))
                        {
                            int core = NeonCaretVisual.CoreWidth(h, native), halo = NeonCaretVisual.HaloWidth(core);
                            Check(core >= native && core >= 2 && halo <= 8 && bitmap.Width == core + halo && bitmap.Height == h, "geometry respects system width and caps glow");
                            for (int y = 0; y < h; y++)
                            {
                                Color color = NeonCaretVisual.ColorAt((double)y / Math.Max(1, h - 1), dark);
                                Check(Distance(back, color) > 250, "every purple-gradient stop remains readable in both themes");
                                double end = y == 0 || y == h - 1 ? .62 : 1;
                                for (int x = 0; x < bitmap.Width; x++)
                                {
                                    double glow = x < core ? 1 : .48 * Math.Pow((double)(bitmap.Width - x) / (halo + 1), 2);
                                    Color expected = NeonCaretVisual.Mix(back, color, end * glow);
                                    Check(Decode(back, bitmap.GetPixel(x, y)).ToArgb() == expected.ToArgb(), "native XOR decodes to the intended gradient and falloff");
                                }
                                int previous = Distance(back, Decode(back, bitmap.GetPixel(core - 1, y)));
                                for (int x = core; x < bitmap.Width; x++)
                                { int next = Distance(back, Decode(back, bitmap.GetPixel(x, y))); Check(next < previous, "halo monotonically fades right"); previous = next; }
                            }
                            if (h > 1) Check(Distance(NeonCaretVisual.ColorAt(0, dark), NeonCaretVisual.ColorAt(1, dark)) > 200, "purple palette has visibly distinct light and deep ends instead of a flat color");
                        }
                    }
            foreach (bool dark in new[] { true, false })
            {
                Check(NeonCaretVisual.ColorAt(double.NaN, dark) == NeonCaretVisual.ColorAt(0, dark), "invalid palette position clamps safely");
                Check(NeonCaretVisual.ColorAt(-100, dark) == NeonCaretVisual.ColorAt(0, dark), "negative palette position clamps");
                Check(NeonCaretVisual.ColorAt(100, dark) == NeonCaretVisual.ColorAt(1, dark), "large palette position clamps");
            }
            bool rejected = false; try { using (var image = NeonCaretVisual.Build(0, 1, Color.White, false)) { } } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "zero-height native masks cannot be allocated");
        }
        static void MotionTests()
        {
            var model = new RainbowCaretTrail(); CaretTrailFrame frame;
            Check(!model.Observe(new Point(20, 40), 24, 3, 0) && !model.Active, "first focus seeds without a phantom trail");
            Check(model.Observe(new Point(60, 40), 24, 3, 10), "local movement starts an effect");
            Check(model.TryFrame(10, out frame) && Math.Abs(frame.Head.X - 61.5) < .001 && Math.Abs(frame.Head.Y - 52) < .001, "head immediately equals the real insertion point");
            double previous = double.MaxValue;
            for (int t = 10; t < 170; t += 5)
            {
                if (!model.TryFrame(t, out frame)) continue;
                double length = Length(frame.Tail, frame.Head);
                Check(length <= previous + .001, "cubic tail contracts monotonically instead of moving the real caret");
                Check(frame.Head.X == 61.5f && frame.Head.Y == 52, "true head never eases, lags or overshoots");
                Check(frame.Opacity >= 0 && frame.Opacity <= 1, "fade is bounded"); previous = length;
            }
            Check(!model.TryFrame(170, out frame) && !model.Active, "animation ends without polling forever");
            Check(model.Observe(new Point(70, 40), 24, 3, 300), "a keystroke after idle still animates (baseline survives expiry)");
            model.TryFrame(315, out frame); PointF tail = frame.Tail;
            Check(model.Observe(new Point(80, 40), 24, 3, 315), "new movement retargets an in-flight trail");
            Check(model.TryFrame(315, out frame) && Length(tail, frame.Tail) < .001, "retargeting is position-continuous");
            Check(!model.Observe(new Point(80, 40), 24, 3, 320), "same-position notifications do not restart the clock");
            Check(!model.TryFrame(315 + RainbowCaretTrail.MaximumLifetime, out frame), "repeated paint/key-up events cannot keep an idle timer alive");
            model.Observe(new Point(90, 40), 24, 3, 500);
            Check(!model.TryFrame(490, out frame) && !model.Active, "backward time cancels rather than producing invalid alpha");
            Check(!model.Observe(new Point(90, 40), 30, 3, 600) && !model.Active, "font/height change resets geometry");
            model.Seed(new Point(20, 40), 24, 3);
            Check(!model.Observe(new Point(5000, 40), 24, 3, 0) && !model.Active, "distant jumps do not smear across the whole window");
            Check(!model.Observe(new Point(5000, 400), 24, 3, 10) && !model.Active, "multi-line jumps do not smear over unrelated text");
            Check(model.Observe(new Point(5000, 424), 24, 3, 20), "adjacent-line motion can use a short vertical ribbon");
            Check(model.TryFrame(20, out frame) && Length(frame.Tail, frame.Head) <= RainbowCaretTrail.LengthLimit(24), "vertical ribbon also has a strict length cap");
        }
        static void InterruptionTests()
        {
            foreach (int msg in new[] { 0x020A, 0x020E, 0x0114, 0x0115, 0x00B5, 0x00B6, 0x04DE, 0x000B })
                Check(RainbowCaretTrail.IsViewportMessage(msg), "scroll/redraw interrupts are cleared before native pixel movement");
            foreach (int msg in new[] { 0x0005, 0x0007, 0x0008, 0x000C, 0x0018, 0x001A, 0x0030, 0x010D, 0x010F, 0x00B1, 0x00C2, 0x0437, 0x0449, 0x04E1, 0x0300, 0x0302, 0x0303, 0x0304, 0x00C7 })
                Check(RainbowCaretTrail.ResetsMotion(msg), "focus, selection, IME, layout and document replacements cancel animation");
            Check(!RainbowCaretTrail.IsViewportMessage(0x000F) && !RainbowCaretTrail.ResetsMotion(0x000F), "paint itself does not reset ongoing movement");
            var model = new RainbowCaretTrail(); CaretTrailFrame frame;
            model.Seed(new Point(10, 10), 20, 2); model.Observe(new Point(40, 10), 20, 2, 0); model.Reset();
            Check(!model.Active && !model.TryFrame(10, out frame), "interrupt removes all animation state");
            Check(!model.Observe(new Point(200, 200), 20, 2, 20), "resuming after scroll/focus reset first seeds a fresh baseline");
            var client = new Rectangle(0, 0, 300, 200); var dirty = new Rectangle(100, 80, 60, 20);
            Check(RainbowCaretTrail.ScrollDamage(dirty, Point.Empty, new Point(0, 30), client) == new Rectangle(100, 50, 60, 20), "vertical native blit damage is translated correctly");
            Check(RainbowCaretTrail.ScrollDamage(dirty, Point.Empty, new Point(120, 0), client) == new Rectangle(0, 80, 40, 20), "horizontal damage is locally clipped");
            Check(RainbowCaretTrail.ScrollDamage(dirty, Point.Empty, new Point(int.MaxValue, int.MaxValue), client).IsEmpty, "huge jumps outside the viewport do not overflow or invalidate the full screen");
            Check(RainbowCaretTrail.ScrollDamage(Rectangle.Empty, Point.Empty, new Point(0, 20), client).IsEmpty, "inactive caret adds no scroll damage");
        }
        static void StressTests()
        {
            var random = new Random(813); var model = new RainbowCaretTrail(); var at = new Point(300, 100);
            model.Seed(at, 24, 3); double now = 0;
            for (int i = 0; i < 12000; i++)
            {
                now += 1; at.Offset(random.Next(-90, 91), random.Next(-1, 2) * 12);
                model.Observe(at, 24, 3, now);
                CaretTrailFrame frame;
                if (!model.TryFrame(now, out frame)) continue;
                Check(Length(frame.Tail, frame.Head) <= RainbowCaretTrail.LengthLimit(24) + .005, "dense input cannot accumulate an unbounded trail/history");
                Check(frame.Bounds.Width <= 100 && frame.Bounds.Height <= 100, "dirty rectangle stays small even under sustained input");
                Rectangle bounds = frame.Bounds;
                for (int s = 0; s <= 16; s++)
                {
                    PointF point = frame.At(s / 16f);
                    Check(bounds.Contains((int)Math.Floor(point.X), (int)Math.Floor(point.Y)), "curved samples remain inside their invalidation bounds");
                }
            }
            CaretTrailFrame last;
            Check(!model.TryFrame(now + RainbowCaretTrail.MaximumLifetime, out last), "even dense movement settles within the same bounded lifetime");
            Check(RainbowCaretTrail.Segments <= 8 && RainbowCaretTrail.FrameInterval >= 25, "constant segment budget and capped frame cadence");
            foreach (int height in new[] { 10, 19, 28, 44, 96, 160, 512 })
            {
                model.Seed(new Point(0, 0), height, NeonCaretVisual.CoreWidth(height, 1));
                model.Observe(new Point((int)(RainbowCaretTrail.LengthLimit(height) * 2), 0), height, NeonCaretVisual.CoreWidth(height, 1), 0);
                Check(model.TryFrame(0, out last) && Length(last.Tail, last.Head) <= 96.001 && last.Bounds.Width <= 120,
                    "large fonts cannot turn a short ribbon into a full-width effect");
            }
        }
        static CaretTrailFrame ExampleFrame(double time)
        {
            var model = new RainbowCaretTrail(); model.Seed(new Point(65, 55), 28, 3);
            model.Observe(new Point(145, 55), 28, 3, 0);
            CaretTrailFrame frame; Check(model.TryFrame(time, out frame), "example frame remains live"); return frame;
        }
        static void RenderingTests()
        {
            foreach (bool dark in new[] { true, false })
                foreach (double time in new[] { 0.0, 25.0, 50.0 })
                {
                    Color back = Back(dark); var frame = ExampleFrame(time);
                    using (var bitmap = new Bitmap(240, 140, PixelFormat.Format32bppPArgb))
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(back); var clip = new Rectangle(20, 20, 180, 100); g.SetClip(clip);
                        SmoothingMode old = g.SmoothingMode; RectangleF oldClip = g.ClipBounds;
                        RainbowCaretTrailRenderer.Paint(g, frame, dark);
                        Check(g.SmoothingMode == old && g.ClipBounds == oldClip, "renderer restores caller clipping and smoothing state");
                        int changed = 0;
                        for (int y = 0; y < bitmap.Height; y++)
                            for (int x = 0; x < bitmap.Width; x++)
                            {
                                bool different = bitmap.GetPixel(x, y).ToArgb() != back.ToArgb();
                                if (different) changed++;
                                if (!clip.Contains(x, y) || !frame.Bounds.Contains(x, y) || Rectangle.Ceiling(frame.CaretBounds).Contains(x, y))
                                    Check(!different, "paint is local and never touches native XOR caret pixels");
                            }
                        Check(changed > 20, "the alpha ribbon is actually visible in both themes");
                        Check(changed < 2000, "glow is a narrow short effect, not a large expensive overlay");
                        g.Clear(back); var narrow = new Rectangle(105, 62, 12, 12); g.SetClip(narrow);
                        RainbowCaretTrailRenderer.Paint(g, frame, dark);
                        for (int y = 0; y < bitmap.Height; y++)
                            for (int x = 0; x < bitmap.Width; x++)
                                if (!narrow.Contains(x, y)) Check(bitmap.GetPixel(x, y).ToArgb() == back.ToArgb(), "renderer obeys even a tiny partial WM_PAINT region");
                    }
                }
        }
        [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);
        static void CostTests()
        {
            var frame = ExampleFrame(25);
            using (var bitmap = new Bitmap(240, 140))
            using (var g = Graphics.FromImage(bitmap))
            {
                for (int i = 0; i < 50; i++) RainbowCaretTrailRenderer.Paint(g, frame, true);
                uint before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                var clock = Stopwatch.StartNew();
                for (int i = 0; i < 1000; i++) { g.Clear(Back(true)); RainbowCaretTrailRenderer.Paint(g, frame, true); }
                clock.Stop(); uint after = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                Check(after <= before + 2, "temporary GDI pens/graphics state are disposed, not retained per frame");
                Console.WriteLine("  Offscreen 1000 local renders: " + clock.ElapsedMilliseconds + " ms; GDI delta=" + ((long)after - before) + ". Not an interactive FPS benchmark.");
            }
        }
        static Bitmap VisibleCaret(int height, bool dark)
        {
            Color back = Back(dark);
            using (var mask = NeonCaretVisual.Build(height, 1, back, dark))
            {
                var visible = new Bitmap(mask.Width, mask.Height);
                for (int y = 0; y < mask.Height; y++) for (int x = 0; x < mask.Width; x++) visible.SetPixel(x, y, Decode(back, mask.GetPixel(x, y)));
                return visible;
            }
        }
        static void Previews(string path)
        {
            Directory.CreateDirectory(path);
            var model = new RainbowCaretTrail(); var at = new Point(305, 67); int height = 28, core = NeonCaretVisual.CoreWidth(height, 1);
            model.Seed(at, height, core);
            using (var darkCaret = VisibleCaret(height, true))
            using (var lightCaret = VisibleCaret(height, false))
            using (var font = new Font("Consolas", 13))
            using (var label = new Font("Segoe UI", 10))
            {
                for (int i = 0; i < 64; i++)
                {
                    if (i >= 8 && i < 24 && i % 2 == 0) at.Offset(10, 0);
                    if (i == 28) at.Offset(100, 0);
                    if (i == 37) at.Offset(-100, 0);
                    if (i == 45) at.Offset(0, 28);
                    model.Observe(at, height, core, i * 25);
                    CaretTrailFrame frame; bool active = model.TryFrame(i * 25, out frame);
                    using (var image = new Bitmap(820, 304))
                    using (var g = Graphics.FromImage(image))
                    {
                        foreach (bool dark in new[] { true, false })
                        {
                            int offset = dark ? 0 : 152;
                            var state = g.Save(); g.TranslateTransform(0, offset);
                            using (var bg = new SolidBrush(Back(dark))) g.FillRectangle(bg, 0, 0, 820, 152);
                            using (var fore = new SolidBrush(dark ? Color.FromArgb(212, 212, 212) : Color.FromArgb(40, 40, 40)))
                            using (var muted = new SolidBrush(dark ? Color.FromArgb(140, 148, 162) : Color.FromArgb(95, 103, 117)))
                            {
                                g.DrawString((dark ? "DARK" : "LIGHT") + "  /  PURPLE GRADIENT CARET + BRIEF MOVEMENT RIBBON", label, muted, 18, 12);
                                g.DrawString("{", font, fore, 24, 44);
                                g.DrawString("  \"timeout\": 1200,  \"state\": \"ready\",", font, fore, 24, 69);
                                g.DrawString("  \"mode\": \"local\"", font, fore, 24, 97);
                                g.DrawString("}", font, fore, 24, 122);
                                g.DrawString(active ? "movement only" : "idle: animation stopped", label, muted, 610, 118);
                            }
                            if (active) RainbowCaretTrailRenderer.Paint(g, frame, dark);
                            g.DrawImageUnscaled(dark ? darkCaret : lightCaret, at.X, at.Y);
                            g.Restore(state);
                        }
                        image.Save(Path.Combine(path, "frame-" + i.ToString("D3") + ".png"), ImageFormat.Png);
                        if (i == 29) image.Save(Path.Combine(path, "purple-caret-preview.png"), ImageFormat.Png);
                    }
                }
            }
            Console.WriteLine("Offscreen preview frames: " + path + " (synthetic sample; not desktop screenshots).");
        }
    }
}
