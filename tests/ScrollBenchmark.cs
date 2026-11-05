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
        [STAThread]
        static int Main()
        {
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                Native.SetProcessDPIAware(); Application.EnableVisualStyles();
                ViewSettings.NoPersist = true; ViewSettings.Dark = false; ViewSettings.ShowTree = false;
                ViewSettings.FontName = "Consolas"; ViewSettings.FontSize = 10.5f;
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
