﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace JsonRightClick
{
    static class Perf
    {
        /// <summary>剪贴板完整备份 / 编码识别 / 终端识别 / 英文界面</summary>
        public static string Round(string sample)
        {
            var o = new StringBuilder();
            // 剪贴板：放入 文字 + HTML + 自定义格式，备份 → 覆盖 → 恢复
            var dobj = new DataObject();
            dobj.SetData(DataFormats.UnicodeText, "original text 原文");
            dobj.SetData(DataFormats.Html, "Version:0.9\r\nStartHTML:0\r\n<b>bold</b>");
            dobj.SetData("JsonRightClick.Test", "custom-format-data");
            Clipboard.SetDataObject(dobj, true);
            using (var f = new Form())
            {
                var h = f.Handle;
                var snap = ClipSnapshot.Capture(h);
                o.Append("snapshot formats=" + snap.Count + " complete=" + snap.Complete + "\r\n");
                Clipboard.SetText("TEMP COPY");
                bool ok = snap.Restore(h);
                System.Threading.Thread.Sleep(100);
                var back = Clipboard.GetDataObject();
                o.Append("restore=" + ok + " text=[" + Clipboard.GetText() + "] html=" + (back.GetData(DataFormats.Html) as string ?? "").Contains("<b>bold</b>") +
                         " custom=" + (back.GetDataPresent("JsonRightClick.Test")) + " excludeFlag=" + back.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") + "\r\n");
            }
            // 编码
            string zh = "{\"msg\":\"中文日志 ok\"}";
            string n;
            o.Append("utf8: " + (FileLoader.Decode(Encoding.UTF8.GetBytes(zh), false, out n) == zh) + " " + n + "\r\n");
            o.Append("utf8bom: " + (FileLoader.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(zh)).ToArray(), false, out n) == zh) + " " + n + "\r\n");
            o.Append("gbk: " + (FileLoader.Decode(Encoding.GetEncoding(936).GetBytes(zh), false, out n) == zh) + " " + n + "\r\n");
            o.Append("utf16: " + (FileLoader.Decode(Encoding.Unicode.GetBytes(zh), false, out n) == zh) + " " + n + "\r\n");
            byte[] cut = Encoding.UTF8.GetBytes(zh); Array.Resize(ref cut, cut.Length - 9); // 截在中文中间
            o.Append("utf8 truncated mid-char: " + FileLoader.Decode(cut, true, out n).Replace("\uFFFD", "?") + " " + n + "\r\n");
            // 英文界面
            L.En = true;
            var r = JsonTools.Process("{'a': 1,}", new FormatOptions { Lenient = true });
            o.Append("en warn: " + r.Warning + "\r\n");
            o.Append("en err: " + JsonTools.Process("{\"a\":1,", new FormatOptions()).Error + "\r\n");
            o.Append("en time: " + TimeHint.Describe("1792057728").Replace("\n", " | ") + "\r\n");
            o.Append("en header: " + JsonTools.BuildView(JsonTools.Process("x {\"a\":1} y {\"b\":2}", false).Pieces, true).Text.Split('\n')[0] + "\r\n");
            L.En = false;
            o.Append("zh header merges en view: " + (JsonTools.MergeOnlyJson(JsonTools.Process("x {\"a\":1} y {\"b\":2}", false).Pieces, "// ===== JSON fragment 1 / 2 =====\n{\"a\":9}\n// ===== JSON fragment 2 / 2 =====\n{\"b\":2}") ?? "NULL").Replace("\r\n", " ") + "\r\n");
            o.Append("dict entries: " + L.DictCount + "\r\n");
            return o.ToString();
        }

        public static string TermCheck(string hwnd)
        {
            var h = new IntPtr(long.Parse(hwnd));
            return "class=" + Native.ClassName(h) + " terminal=" + Terminal.Is(h) + " notepadLike=" + Terminal.Is(IntPtr.Zero);
        }

        /// <summary>在同一进程里反复打开 / 关闭查看窗口，看内存是否回落；同时测打开耗时</summary>
        public static string Leak(string text, int rounds)
        {
            var o = new StringBuilder();
            Func<string> mem = () => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); var p = Process.GetCurrentProcess(); p.Refresh(); return "managed " + (GC.GetTotalMemory(true) / 1048576.0).ToString("0.0") + " MB, private " + (p.PrivateMemorySize64 / 1048576.0).ToString("0.0") + " MB, GDI " + Native.GetGuiResources(p.Handle, 0) + ", USER " + Native.GetGuiResources(p.Handle, 1); };
            o.Append("before: " + mem() + "\r\n");
            for (int i = 0; i < rounds; i++)
            {
                var sw = Stopwatch.StartNew();
                var v = new JsonViewer(text);
                v.Show();
                Application.DoEvents();
                long open = sw.ElapsedMilliseconds;
                v.Close(); v.Dispose();
                Application.DoEvents();
                o.Append("round " + (i + 1) + ": open " + open + " ms; after close " + mem() + "\r\n");
            }
            return o.ToString();
        }

        public static string Run(string text)
        {
            var o = new StringBuilder();
            var sw = Stopwatch.StartNew();
            Action<string> lap = name => { o.Append(name + ": " + sw.ElapsedMilliseconds + " ms\r\n"); sw.Restart(); };
            var r = JsonTools.Process(text, new FormatOptions { Lenient = true }); lap("Process(lenient)");
            var r2 = JsonTools.Process(text, new FormatOptions { Lenient = false }); lap("Process(strict)");
            var v = JsonTools.BuildView(r.Pieces, false); lap("BuildView  len=" + v.Text.Length);
            var ranges = JsonTools.FindJsonRanges(v.Text); lap("FindJsonRanges");
            var dom = Dom.Parse(v.Text, ranges.Count > 0 ? ranges[0][0] : 0); lap("Dom.Parse");
            string rtf = Rtf.Build(v.Text, v.JsonRanges, Theme.Light, "Consolas", 10.5f); lap("Rtf.Build len=" + rtf.Length);
            int n; Mask.Apply(v.Text, Mask.ParseKeys(Mask.DefaultKeys), out n); lap("Mask.Apply n=" + n);
            using (var f = new Form { Width = 1000, Height = 800, ShowInTaskbar = false })
            {
                var b1 = new RichTextBox { Dock = DockStyle.Fill, MaxLength = int.MaxValue, WordWrap = true };
                f.Controls.Add(b1); var h = f.Handle; h = b1.Handle; sw.Restart();
                b1.Rtf = rtf; lap("RichTextBox.Rtf (no wordbreak)");
                Action<string, Func<string, string>> variant = (name, fn) =>
                {
                    try { string x = fn(rtf); b1.Clear(); sw.Restart(); b1.Rtf = x; lap(name + " len=" + x.Length); }
                    catch (Exception ex) { lap(name + " FAILED " + ex.GetType().Name + ": " + ex.Message); }
                };
                variant("colors only (no indent)", x => System.Text.RegularExpressions.Regex.Replace(x, "\\\\li-?[0-9]+\\\\fi-?[0-9]+ ?", ""));
                variant("indent only (no colors)", x => System.Text.RegularExpressions.Regex.Replace(x, "\\\\cf[0-9]+ ?", ""));
                variant("no colors no indent", x => System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(x, "\\\\li-?[0-9]+\\\\fi-?[0-9]+ ?", ""), "\\\\cf[0-9]+ ?", ""));
                // 方案 A：先放纯文字，再只给前 300 行着色（模拟只着色可见区域）
                b1.Clear(); sw.Restart();
                b1.Text = v.Text; lap("A1 plain");
                int end = b1.GetFirstCharIndexFromLine(300); if (end < 0) end = b1.TextLength;
                Native.SendMessage(b1.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); // WM_SETREDRAW off
                int runs = 0;
                string txt = b1.Text;
                for (int i = 0; i < end; )
                {
                    char c = txt[i]; int j = i + 1; Color col;
                    if (c == '"') { while (j < end && txt[j] != '"') j += txt[j] == '\\' ? 2 : 1; j = Math.Min(j + 1, end); col = Color.FromArgb(163, 21, 21); }
                    else if (c >= '0' && c <= '9') { while (j < end && txt[j] >= '0' && txt[j] <= '9') j++; col = Color.FromArgb(9, 134, 88); }
                    else { while (j < end && txt[j] != '"' && !(txt[j] >= '0' && txt[j] <= '9')) j++; col = Color.FromArgb(60, 60, 60); }
                    b1.Select(i, j - i); b1.SelectionColor = col; runs++; i = j;
                }
                Native.SendMessage(b1.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
                lap("A2 color first 300 lines, runs=" + runs + " canUndo=" + b1.CanUndo);

                // 方案 B：分块追加 RTF
                b1.Clear(); sw.Restart();
                var lines = v.Text.Split('\n');
                var chunk = new StringBuilder(); var lst = new System.Collections.Generic.List<int[]>();
                Native.SendMessage(b1.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
                for (int li = 0; li < lines.Length; li++)
                {
                    chunk.Append(lines[li]); if (li < lines.Length - 1) chunk.Append('\n');
                    if (chunk.Length > 60000 || li == lines.Length - 1)
                    {
                        string ct = chunk.ToString();
                        var rr = new System.Collections.Generic.List<int[]> { new[] { 0, ct.Length } };
                        b1.Select(b1.TextLength, 0);
                        b1.SelectedRtf = Rtf.Build(ct, rr, Theme.Light, "Consolas", 10.5f);
                        chunk.Clear();
                    }
                }
                Native.SendMessage(b1.Handle, 0x000B, (IntPtr)1, IntPtr.Zero);
                lap("B chunked SelectedRtf len=" + b1.TextLength);

                b1.Clear(); WordBreak.Attach(b1); sw.Restart();
                b1.Rtf = rtf; lap("RichTextBox.Rtf (wordbreak)");
                b1.Clear(); sw.Restart();
                b1.Text = v.Text; lap("RichTextBox.Text plain (wordbreak)");
                string t = b1.Text; lap("read box.Text");
            }
            return o.ToString();
        }
    }
}
