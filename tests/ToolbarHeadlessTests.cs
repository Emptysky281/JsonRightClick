using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace JsonRightClick
{
    // Real WinForms layout, but no Form, handles, Show, input, focus, clipboard or user settings.
    static class ToolbarHeadlessTests
    {
        static int checks;
        static void Check(bool value, string message)
        { checks++; if (!value) throw new Exception(message); }
        sealed class Fixture : IDisposable
        {
            internal readonly Panel Host, Editor;
            internal readonly WrappingToolStrip Strip;
            internal readonly ToolStripTextBox Search;
            internal readonly ToolStripLabel Count;
            internal readonly ToolStripButton Toggle, Next;
            internal readonly ToolStripSplitButton Smart;
            internal readonly ToolStripDropDownButton Settings;
            internal readonly ToolStripItem[] Original;
            readonly Font font;
            internal int SmartClicks, NextClicks;
            internal Fixture(bool english, float size)
            {
                font = new Font("Microsoft YaHei UI", size);
                Host = new Panel { Visible = false, Size = new Size(1800, 1800) };
                Editor = new Panel { Dock = DockStyle.Fill };
                Strip = new WrappingToolStrip { Font = font };
                Strip.Items.Add(new ToolStripButton(english ? "Format" : "格式化"));
                Strip.Items.Add(new ToolStripButton(english ? "Minify" : "压缩"));
                Smart = new ToolStripSplitButton(english ? "Smart compact" : "智能收紧");
                Smart.DropDownItems.Add(new ToolStripMenuItem(english ? "Smart compact (recommended)" : "智能收紧（推荐）"));
                Smart.DropDownItems.Add(new ToolStripMenuItem(english ? "All arrays on one line" : "数组全部单行"));
                Smart.DropDownItems.Add(new ToolStripMenuItem(english ? "Expand all" : "全部展开"));
                Smart.ButtonClick += delegate { SmartClicks++; };
                Strip.Items.Add(Smart);
                Strip.Items.Add(new ToolStripButton(english ? "Expand nested" : "展开嵌套"));
                Strip.Items.Add(new ToolStripDropDownButton(english ? "Fold" : "折叠"));
                Strip.Items.Add(new ToolStripButton(english ? "Copy all" : "复制全部"));
                Strip.Items.Add(new ToolStripSeparator());
                Strip.Items.Add(new ToolStripButton(english ? "JSON only" : "只看 JSON"));
                Strip.Items.Add(new ToolStripButton(english ? "Mask" : "打码"));
                Strip.Items.Add(new ToolStripButton(english ? "Tree" : "树形"));
                Strip.Items.Add(new ToolStripSeparator());
                Toggle = new ToolStripButton(english ? "Wrap" : "换行") { CheckOnClick = true, Checked = true };
                Strip.Items.Add(Toggle);
                Strip.Items.Add(new ToolStripButton(english ? "On top" : "置顶"));
                Search = new ToolStripTextBox { AutoSize = false, Width = (int)(190 * size / 9), Text = "original search" };
                Strip.Items.Add(Search);
                Strip.Items.Add(new ToolStripButton(".*"));
                Strip.Items.Add(new ToolStripButton("Aa"));
                Count = new ToolStripLabel(""); Strip.Items.Add(Count);
                Strip.Items.Add(new ToolStripButton("▲"));
                Next = new ToolStripButton("▼"); Next.Click += delegate { NextClicks++; }; Strip.Items.Add(Next);
                Strip.Items.Add(new ToolStripSeparator());
                Strip.Items.Add(new ToolStripButton(english ? "Dark" : "☀ 白天"));
                Settings = new ToolStripDropDownButton(english ? "Settings" : "⚙ 设置") { ShowDropDownArrow = false };
                Settings.DropDownItems.Add(new ToolStripMenuItem("retained setting") { Checked = true });
                Strip.Items.Add(Settings);
                Strip.ConfigureSearchGroup(Search);
                Original = new ToolStripItem[Strip.Items.Count]; Strip.Items.CopyTo(Original, 0);
                Host.Controls.Add(Editor); Host.Controls.Add(Strip);
            }
            internal void Width(int value)
            { Host.Width = value; Host.PerformLayout(); Strip.PerformLayout(); }
            public void Dispose() { Host.Dispose(); font.Dispose(); }
        }
        static int Verify(Fixture fixture)
        {
            var strip = fixture.Strip; var items = new List<ToolStripItem>(); var rows = new HashSet<int>();
            Check(!fixture.Host.Visible && !fixture.Host.IsHandleCreated && !strip.IsHandleCreated && !fixture.Search.Control.IsHandleCreated,
                "headless layout must not create or show any native window, including the hosted search input");
            Check(!strip.CanOverflow && strip.AutoSize && strip.LayoutStyle == ToolStripLayoutStyle.Flow, "items wrap rather than move into the overflow popup");
            for (int i = 0; i < strip.Items.Count; i++)
            {
                ToolStripItem item = strip.Items[i];
                Check(ReferenceEquals(item, fixture.Original[i]), "resizing preserves item identity, visual order and event bindings");
                if (!item.Available) continue;
                Check(item.Placement == ToolStripItemPlacement.Main, "every available item stays in the main toolbar");
                Rectangle at = item.Bounds;
                Check(at.Left >= strip.Padding.Left && at.Right <= strip.ClientSize.Width - strip.Padding.Right,
                    "all available controls remain horizontally inside the toolbar: " + item.Text + " " + at + " width=" + strip.Width);
                Check(at.Top >= strip.Padding.Top && at.Bottom <= strip.Height - strip.Padding.Bottom,
                    "auto-sized toolbar is tall enough for its last row: " + item.Text + " " + at + " height=" + strip.Height);
                if (at.Width > 0 && at.Height > 0) { items.Add(item); rows.Add(at.Top - item.Margin.Top); }
            }
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    Rectangle overlap = Rectangle.Intersect(items[i].Bounds, items[j].Bounds);
                    Check(overlap.Width == 0 || overlap.Height == 0, "flow rows never overlap or cover a neighboring control");
                }
            Check(fixture.Editor.Top == strip.Bottom && fixture.Editor.Bottom == fixture.Host.ClientSize.Height,
                "the editor moves below the extra rows and reclaims their height after widening");
            Check(fixture.Search.Text == "original search" && fixture.Toggle.Checked && ((ToolStripMenuItem)fixture.Settings.DropDownItems[0]).Checked,
                "search text and button/menu state survive layout changes");
            Check(fixture.Smart.DropDownItems.Count == 3, "all smart/compact/expanded array choices survive reflow");
            return rows.Count;
        }
        static void WidthAndScaleTests()
        {
            foreach (bool english in new[] { false, true })
                foreach (float size in new[] { 9f, 12f, 18f })
                    using (var fixture = new Fixture(english, size))
                    {
                        int wide = (int)(1800 * size / 9), narrow = (int)(500 * size / 9);
                        fixture.Width(wide); Check(Verify(fixture) == 1, "a sufficiently wide toolbar returns to exactly one row");
                        int initial = fixture.Strip.Height, searchWidth = fixture.Search.Width;
                        fixture.Width(narrow); Check(Verify(fixture) > 1 && fixture.Strip.Height > initial, "narrowing adds rows and increases toolbar height automatically");
                        foreach (int logical in new[] { 1180, 1000, 820, 600, 420, 280, 160, 1000, 1800 })
                        {
                            fixture.Width((int)(logical * size / 9)); Verify(fixture);
                        }
                        fixture.Width(wide);
                        Check(Verify(fixture) == 1 && fixture.Strip.Height == initial && fixture.Search.Width == searchWidth,
                            "widening removes all surplus rows and restores the search box's original width");
                        Check(fixture.Settings.Bounds.Right == fixture.Strip.Width - fixture.Strip.Padding.Right,
                            "wide windows retain right-aligned settings/search controls");
                        fixture.Smart.PerformButtonClick(); fixture.Next.PerformClick();
                        Check(fixture.SmartClicks == 1 && fixture.NextClicks == 1, "resizing does not detach the split-button or search navigation handlers");
                    }
        }
        static void DynamicAndStressTests()
        {
            using (var fixture = new Fixture(true, 9))
            {
                var random = new Random(17112026); var watch = Stopwatch.StartNew();
                for (int i = 0; i < 300; i++)
                {
                    fixture.Count.Text = i % 3 == 0 ? "50000/50000" : i % 3 == 1 ? "1/2" : "";
                    fixture.Width(random.Next(160, 1900)); Verify(fixture);
                    int height = fixture.Strip.Height;
                    for (int j = 0; j < 3; j++) fixture.Strip.PerformLayout();
                    Check(fixture.Strip.Height == height, "repeated layout passes do not oscillate or keep increasing toolbar height");
                }
                watch.Stop();
                fixture.Count.Text = ""; fixture.Width(1800); Check(Verify(fixture) == 1, "dynamic search counts do not leave a stale wrapped row");
                fixture.Original[0].Available = false; fixture.Width(600); Verify(fixture);
                fixture.Original[0].Available = true; fixture.Width(1800); Check(Verify(fixture) == 1, "availability changes do not corrupt row breaks or ordering");
                Console.WriteLine("  300 hidden, handle-free resizes with dynamic counters: " + watch.ElapsedMilliseconds + " ms. Not a desktop animation benchmark.");
            }
        }
        internal static int RunAll()
        {
            checks = 0; WidthAndScaleTests(); DynamicAndStressTests();
            Console.WriteLine("PASS wrapping toolbar: " + checks + " assertions; no windows shown or native handles created.");
            return checks;
        }
        [STAThread] static int Main()
        { try { RunAll(); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
    }
}
