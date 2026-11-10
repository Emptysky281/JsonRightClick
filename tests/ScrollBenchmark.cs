using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace JsonRightClick
{
    // 可与旧版源代码一起编译；比较的是真实 EM_LINESCROLL 位移，而不是不生效的超长文档像素请求。
    static class ScrollBenchmark
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static object Field(object obj, string name) { return obj.GetType().GetField(name, Private).GetValue(obj); }
        static void Call(object obj, string name) { obj.GetType().GetMethod(name, Private).Invoke(obj, null); }
        static JsonViewer Open(string text)
        {
            var viewer = new JsonViewer(text) { Size = new Size(860, 600) };
            viewer.Show(); Application.DoEvents();
            return viewer;
        }
        static double Percentile(List<double> values, double fraction)
        { values.Sort(); return values[(int)Math.Round((values.Count - 1) * fraction)]; }
        sealed class ClockWindow : NativeWindow, IDisposable
        {
            readonly object pump;
            readonly MethodInfo accept;
            readonly int messageId;
            readonly Stopwatch clock = Stopwatch.StartNew();
            readonly List<double> gaps = new List<double>();
            double last;
            public ClockWindow(Type type, int interval)
            {
                pump = Activator.CreateInstance(type, true);
                accept = type.GetMethod("Accept");
                messageId = (int)type.GetField("MessageId").GetRawConstantValue();
                CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
                type.GetMethod("Start").Invoke(pump, new object[] { Handle, interval });
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == messageId && pump != null)
                {
                    if ((bool)accept.Invoke(pump, new object[] { message.WParam }))
                    {
                        double now = clock.Elapsed.TotalMilliseconds;
                        if (last > 0) gaps.Add(now - last);
                        last = now;
                    }
                    return;
                }
                base.WndProc(ref message);
            }
            public void Measure(int interval)
            {
                var duration = Stopwatch.StartNew();
                while (duration.ElapsedMilliseconds < 1000)
                { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                pump.GetType().GetMethod("Stop").Invoke(pump, null);
                if (gaps.Count < 2) throw new Exception("Frame clock did not deliver measurable UI messages.");
                Console.WriteLine("FRAME_CLOCK requested_ms={0} frames={1} gap_p50_ms={2:F2} gap_p95_ms={3:F2}",
                    interval, gaps.Count + 1, Percentile(gaps, .5), Percentile(gaps, .95));
            }
            public void Dispose() { ((IDisposable)pump).Dispose(); DestroyHandle(); }
        }
        static void MeasureFrameClock()
        {
            // 反射使这个基准仍可与未引入 AnimationFramePump 的旧源码一起编译。
            var type = typeof(JsonViewer).Assembly.GetType("JsonRightClick.AnimationFramePump");
            if (type == null) { Console.WriteLine("FRAME_CLOCK not present in this baseline"); return; }
            Native.timeBeginPeriod(1); // 保持与旧版实际动画时的系统设置一致。
            try { foreach (int interval in new[] { 16, 8 }) using (var sink = new ClockWindow(type, interval)) sink.Measure(interval); }
            finally { Native.timeEndPeriod(1); }
        }
        static void MeasureWheel(CodeBox box)
        {
            var wheel = box.GetType().GetMethod("Wheel", Private);
            if (wheel == null || SystemInformation.MouseWheelScrollLines <= 0) return;
            box.StopSmooth(); box.Select(0, 0); box.ScrollPos = Point.Empty;
            var gaps = new List<double>(); var clock = Stopwatch.StartNew();
            double last = 0; int changes = 0, start = box.ScrollPos.Y;
            Action moved = delegate
            {
                double now = clock.Elapsed.TotalMilliseconds;
                if (last > 0) gaps.Add(now - last);
                last = now; changes++;
            };
            box.ViewportChanged += moved;
            int ticks = 0;
            try
            {
                while (ticks < 24 || box.IsSmoothScrolling)
                {
                    if (ticks < 24 && clock.ElapsedMilliseconds >= ticks * 55)
                    { wheel.Invoke(box, new object[] { -120 }); ticks++; }
                    Application.DoEvents(); System.Threading.Thread.Sleep(1);
                    if (clock.ElapsedMilliseconds > 5000) throw new Exception("Real wheel animation did not settle.");
                }
                int distance = box.ScrollPos.Y - start;
                int expected = ticks * SystemInformation.MouseWheelScrollLines * box.LineHeight;
                if (Math.Abs(distance - expected) > 1 || changes < ticks * 2) throw new Exception("Wheel benchmark lost movement or intermediate frames.");
                Console.WriteLine("WHEEL ticks={0} frames={1} duration_ms={2} gap_p50_ms={3:F2} gap_p95_ms={4:F2} moved_pixels={5} expected_pixels={6}",
                    ticks, changes, clock.ElapsedMilliseconds, Percentile(gaps, .5), Percentile(gaps, .95), distance, expected);
            }
            finally { box.ViewportChanged -= moved; box.StopSmooth(); }
        }
        [STAThread]
        static int Main()
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Native.SetProcessDPIAware(); Application.EnableVisualStyles();
                ViewSettings.NoPersist = true; ViewSettings.Dark = false; ViewSettings.ShowTree = false;
                ViewSettings.FontName = "Consolas"; ViewSettings.FontSize = 10.5f;
                MeasureFrameClock();
                var source = new StringBuilder("{\"items\":[");
                for (int i = 0; i < 5000; i++)
                {
                    if (i > 0) source.Append(',');
                    source.Append("{\"id\":").Append(i).Append(",\"profile\":{\"name\":\"中文 item ").Append(i)
                        .Append("\",\"enabled\":true,\"tags\":[\"first\",\"second\"]}}");
                }
                source.Append("]}");
                using (var viewer = Open(source.ToString()))
                {
                    var box = (CodeBox)Field(viewer, "box"); var timer = (Timer)Field(viewer, "colorTimer");
                    int selections = 0, viewports = 0;
                    box.SelectionChanged += delegate { selections++; }; box.ViewportChanged += delegate { viewports++; };
                    int version = (int)Field(viewer, "textVersion");
                    object blocks = Field(viewer, "coloredBlocks"); blocks.GetType().GetMethod("Clear").Invoke(blocks, null);
                    var progressField = viewer.GetType().GetField("colorProgress", Private);
                    if (progressField != null) ((IDictionary)progressField.GetValue(viewer)).Clear();
                    var slices = new List<double>();
                    for (int i = 0; i < 128; i++)
                    {
                        timer.Stop(); var slice = Stopwatch.StartNew(); Call(viewer, "ColorVisible"); slices.Add(slice.Elapsed.TotalMilliseconds);
                        if (!timer.Enabled) break;
                    }
                    timer.Stop();
                    double total = 0, max = 0; foreach (double ms in slices) { total += ms; max = Math.Max(max, ms); }
                    Console.WriteLine("COLOR total_ms={0:F2} max_slice_ms={1:F2} slices={2} selection_events={3} text_version_delta={4}",
                        total, max, slices.Count, selections, (int)Field(viewer, "textVersion") - version);
                    box.Select(2, 0); selections = 0; viewports = 0;
                    int first = (int)Native.SendMessage(box.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
                    var frames = new List<double>(); var all = Stopwatch.StartNew();
                    for (int i = 0; i < 40; i++)
                    {
                        var frame = Stopwatch.StartNew();
                        Native.SendMessage(box.Handle, 0x00B6, IntPtr.Zero, (IntPtr)1);
                        box.Invalidate(); box.Update();
                        frames.Add(frame.Elapsed.TotalMilliseconds);
                    }
                    int last = (int)Native.SendMessage(box.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero);
                    if (last - first < 40) throw new Exception("Benchmark did not actually scroll the document.");
                    Console.WriteLine("SCROLL frames=40 total_ms={0:F2} p50_ms={1:F2} p95_ms={2:F2} moved_lines={3} viewport_events={4} selection_events={5}",
                        all.Elapsed.TotalMilliseconds, Percentile(frames, .5), Percentile(frames, .95), last - first, viewports, selections);
                    MeasureWheel(box);
                    viewer.Close();
                }
                using (var viewer = Open("{\"long\":\"" + new string('x', 125000) + "\",\"padding\":\"" + new string('y', 40000) + "\"}"))
                {
                    var box = (CodeBox)Field(viewer, "box"); int start = box.Text.IndexOf('x');
                    box.Highlights = new List<int[]> { new[] { start, 125000 } };
                    var frames = new List<double>();
                    for (int i = 0; i < 3; i++) { var watch = Stopwatch.StartNew(); box.Invalidate(); box.Update(); frames.Add(watch.Elapsed.TotalMilliseconds); }
                    Console.WriteLine("LONG_MATCH chars=125000 paint_p50_ms={0:F2} paint_p95_ms={1:F2}", Percentile(frames, .5), Percentile(frames, .95));
                    viewer.Close();
                }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
