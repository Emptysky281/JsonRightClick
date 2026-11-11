using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace JsonRightClick
{
    // ===================== 查看 / 编辑窗口 =====================
    sealed class JsonViewer : Form
    {
        static readonly string[] MonoFonts = { "Consolas", "Cascadia Mono", "Cascadia Code", "JetBrains Mono", "Fira Code", "Source Code Pro", "Courier New", "Lucida Console", "Sarasa Mono SC", "等距更纱黑体 SC" };
        static readonly float[] Sizes = { 8, 9, 10, 10.5f, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32 };
        const int MaxMatches = 50000;
        const int LargeText = 150000;   // 超过这个长度：先显示纯文字，再按滚动位置给可见区域着色
        const int ColorBlock = 4096;     // 大文件着色按固定字符块缓存，不受自动换行或窗口宽度影响

        readonly CodeBox box = new CodeBox();
        readonly JsonTree tree = new JsonTree();
        readonly SplitContainer split = new SplitContainer();
        readonly EditorSurface editorSurface = new EditorSurface { Dock = DockStyle.Fill };
        readonly ToolStrip tools;
        readonly StatusStrip bar;
        readonly ToolStripStatusLabel status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolStripStatusLabel pathLabel = new ToolStripStatusLabel("") { IsLink = false, ToolTipText = L.T("当前位置的 JSON 路径（点击复制）") };
        string fileInfo = "";
        readonly ToolStripStatusLabel hint = new ToolStripStatusLabel("");
        readonly ToolStripButton btnWrap, btnOnly, btnTop, btnTheme, btnPrev, btnNext, btnMask, btnTree, btnRegex, btnCase;
        readonly ToolStripTextBox search;
        readonly ToolStripLabel searchCount = new ToolStripLabel("");
        readonly ToolStripDropDownButton btnSettings, btnFolding;
        readonly CursorToolTip tip = new CursorToolTip();
        readonly System.Windows.Forms.Timer matchTimer = new System.Windows.Forms.Timer { Interval = 180 };
        readonly System.Windows.Forms.Timer pathTimer = new System.Windows.Forms.Timer { Interval = 120 };
        readonly System.Windows.Forms.Timer treeTimer = new System.Windows.Forms.Timer { Interval = 600 };
        readonly System.Windows.Forms.Timer colorTimer = new System.Windows.Forms.Timer { Interval = 120 };

        // 大文件模式
        bool large, largeNoted, syntaxColoring;
        SyntaxIndex syntaxIndex;
        int syntaxVersion = -1;
        List<int[]> largeRanges = new List<int[]>();
        HashSet<int> coloredBlocks = new HashSet<int>();
        readonly Dictionary<int, int> colorProgress = new Dictionary<int, int>();
        readonly ToolStripMenuItem miDecode = new ToolStripMenuItem(L.T("解码 JWT"));
        readonly ToolStripMenuItem miNested = new ToolStripMenuItem(L.T("在新窗口解析此字符串（嵌套 JSON）"));
        readonly ToolStripMenuItem miPath = new ToolStripMenuItem(L.T("复制此处的 JSON 路径"));

        List<Piece> pieces = new List<Piece>(); // 当前完整内容（JSON 段 + 非 JSON 文字）
        bool shownOnly;   // 当前框里显示的是不是“只看 JSON”视图
        bool rendering, edited, lastMinify, masked;
        string realText;  // 打码时保存的真实内容（框内格式）
        string titleBase = L.T("JSON 解析");
        string statusKind = "muted";
        int wheelAcc;

        // 搜索
        List<int[]> matches = new List<int[]>();
        string matchError;
        bool matchSelectPending;

        // 文本缓存 / 结构缓存
        int textVersion = 1, cacheVersion, domVersion = -1;
        string textCache = "";
        List<JNode> domRoots = new List<JNode>();
        Font treeFont;
        BracketIndex bracketIndex;
        int bracketVersion = -1;

        // 格式化等操作的撤销记录
        sealed class Snapshot { public string Display; public List<Piece> Pieces; public bool ShownOnly; public string After; }
        readonly List<Snapshot> history = new List<Snapshot>();

        // 悬停提示
        int tipStart = -2, tipEnd, tipVersion = -1;
        string tipText;
        Point lastMouse = new Point(-1, -1);
        int menuIndex = -1;
        string menuJwt, menuNested;

        /// <summary>每创建一个查看窗口触发（独立查看模式用来判断何时退出）</summary>
        public static event Action<JsonViewer> ViewerCreated;
        readonly string filePath, fileEncoding;
        readonly bool fileTruncated;

        public JsonViewer(string text) : this(text, null, false, null) { }

        public JsonViewer(string text, string path, bool truncated, string encoding)
        {
            filePath = path; fileTruncated = truncated; fileEncoding = encoding;
            Text = titleBase;
            Icon = AppIcon.Get();
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1180, wa.Width * 4 / 5), Math.Min(840, wa.Height * 5 / 6));
            float scale;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f; // 不能用 CreateGraphics：会提前创建窗口句柄

            tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 3, 6, 3), Stretch = true };
            tools.Items.Add(new ToolStripButton(L.T("格式化"), null, delegate { Reprocess(false, false); }) { ToolTipText = L.T("重新格式化 (Ctrl+Enter)") });
            tools.Items.Add(new ToolStripButton(L.T("压缩"), null, delegate { Reprocess(true, false); }) { ToolTipText = L.T("压缩为一行") });
            tools.Items.Add(new ToolStripButton(L.T("展开嵌套"), null, delegate { Reprocess(false, true); }) { ToolTipText = L.T("把内容是 JSON 的字符串（如 \"data\":\"{\\\"a\\\":1}\"）展开成对象，可 Ctrl+Z 撤销") });
            btnFolding = new ToolStripDropDownButton(L.T("折叠", "Fold"));
            AddFoldingMenu(btnFolding.DropDownItems);
            btnFolding.DropDownOpening += delegate { ApplyRenderer(btnFolding.DropDown); };
            tools.Items.Add(btnFolding);
            tools.Items.Add(new ToolStripButton(L.T("复制全部"), null, delegate { CopyAll(); }));
            tools.Items.Add(new ToolStripSeparator());
            btnOnly = new ToolStripButton(L.T("只看 JSON")) { CheckOnClick = true, ToolTipText = L.T("暂时隐藏日志中 JSON 以外的文字（不会删除）") };
            btnOnly.CheckedChanged += delegate
            {
                if (rendering) return;
                if (!Reprocess(lastMinify, false)) { rendering = true; btnOnly.Checked = !btnOnly.Checked; rendering = false; }
            };
            btnMask = new ToolStripButton(L.T("打码")) { CheckOnClick = true, ToolTipText = L.T("把 password、token、手机号等敏感字段显示成 ******，方便截图分享（只读，复制的也是打码后的内容）") };
            btnMask.CheckedChanged += delegate { if (!rendering && !SetMasked(btnMask.Checked)) { rendering = true; btnMask.Checked = masked; rendering = false; } };
            btnTree = new ToolStripButton(L.T("树形")) { CheckOnClick = true, Checked = ViewSettings.ShowTree, ToolTipText = L.T("在右侧显示可折叠的树形结构") };
            btnTree.CheckedChanged += delegate { ViewSettings.ShowTree = btnTree.Checked; ShowTreePanel(btnTree.Checked); };
            btnWrap = new ToolStripButton(L.T("换行")) { CheckOnClick = true, Checked = true, ToolTipText = L.T("自动换行") };
            btnWrap.CheckedChanged += delegate { box.WordWrap = btnWrap.Checked; };
            btnTop = new ToolStripButton(L.T("置顶")) { CheckOnClick = true, ToolTipText = L.T("窗口置顶") };
            btnTop.CheckedChanged += delegate { TopMost = btnTop.Checked; };
            tools.Items.Add(btnOnly);
            tools.Items.Add(btnMask);
            tools.Items.Add(btnTree);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(btnWrap);
            tools.Items.Add(btnTop);

            // 右侧（从右往左添加）：设置、主题、搜索
            btnSettings = new ToolStripDropDownButton(L.T("⚙ 设置")) { Alignment = ToolStripItemAlignment.Right, ShowDropDownArrow = false, ToolTipText = L.T("字体、字号、宽松解析、打码字段等设置") };
            btnSettings.DropDownOpening += delegate { L.Refresh(); BuildSettingsMenu(); };
            BuildSettingsMenu();
            btnTheme = new ToolStripButton("") { Alignment = ToolStripItemAlignment.Right };
            btnTheme.Click += delegate { ViewSettings.Dark = !ViewSettings.Dark; ApplyAll(); };
            btnNext = new ToolStripButton("▼") { Alignment = ToolStripItemAlignment.Right, ToolTipText = L.T("下一个 (Enter / F3)") };
            btnNext.Click += delegate { Find(true); };
            btnPrev = new ToolStripButton("▲") { Alignment = ToolStripItemAlignment.Right, ToolTipText = L.T("上一个 (Shift+Enter / Shift+F3)") };
            btnPrev.Click += delegate { Find(false); };
            btnCase = new ToolStripButton("Aa") { Alignment = ToolStripItemAlignment.Right, CheckOnClick = true, ToolTipText = L.T("区分大小写") };
            btnCase.CheckedChanged += delegate { UpdateMatches(true); };
            btnRegex = new ToolStripButton(".*") { Alignment = ToolStripItemAlignment.Right, CheckOnClick = true, ToolTipText = L.T("使用正则表达式") };
            btnRegex.CheckedChanged += delegate { UpdateMatches(true); };
            searchCount.Alignment = ToolStripItemAlignment.Right;
            search = new ToolStripTextBox { Alignment = ToolStripItemAlignment.Right, AutoSize = false, Width = (int)(190 * scale), BorderStyle = BorderStyle.FixedSingle, ToolTipText = L.T("搜索 (Ctrl+F)，所有结果都会高亮") };
            search.TextBox.HandleCreated += delegate { Native.SetCueBanner(search.TextBox.Handle, L.T("搜索… (Ctrl+F)", "Search… (Ctrl+F)")); };
            search.TextChanged += delegate { matchSelectPending = true; matchTimer.Stop(); matchTimer.Start(); };
            search.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Find(!e.Shift); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { box.Focus(); e.Handled = e.SuppressKeyPress = true; }
            };
            tools.Items.Add(btnSettings);
            tools.Items.Add(btnTheme);
            tools.Items.Add(new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right });
            tools.Items.Add(btnNext);
            tools.Items.Add(btnPrev);
            tools.Items.Add(searchCount);
            tools.Items.Add(btnCase);
            tools.Items.Add(btnRegex);
            tools.Items.Add(search);

            box.Dock = DockStyle.Fill;
            box.BorderStyle = BorderStyle.None;
            box.DetectUrls = false;
            box.AcceptsTab = true;
            box.WordWrap = true;
            box.HideSelection = false;
            box.ScrollBars = RichTextBoxScrollBars.Both;
            box.MaxLength = int.MaxValue;
            box.Font = new Font("Consolas", 10.5f);
            box.ContextMenuStrip = BuildBoxMenu();
            box.HandleCreated += delegate { WordBreak.Attach(box); };
            box.TextChanged += delegate
            {
                if (box.FoldingFormatting || syntaxColoring) return;
                box.StopSmooth();
                textVersion++;
                syntaxIndex = null; syntaxVersion = -1;
                if (!rendering && large) { coloredBlocks.Clear(); colorProgress.Clear(); ScheduleColors(); }
                bracketIndex = null; bracketVersion = -1;
                box.ClearBracketMatch();
                HideTip();
                if (!rendering) { box.RebuildFolding(BoxText, true); UpdateBrackets(); }
                if (!rendering && !edited) { edited = true; UpdateTitle(); }
                if (search.Text.Length > 0) { matchTimer.Stop(); matchTimer.Start(); }
                if (btnTree.Checked) { treeTimer.Stop(); treeTimer.Start(); }
            };
            box.SelectionChanged += delegate
            {
                if (!rendering && !box.FoldingFormatting) { UpdateBrackets(); pathTimer.Stop(); pathTimer.Start(); }
            };
            box.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && !rendering && box.SelectionLength == 0)
                    UpdateBrackets(box.CharIndexAt(e.Location));
            };
            box.CtrlWheel += delegate(int delta)
            {
                wheelAcc += delta;
                while (wheelAcc >= 120) { wheelAcc -= 120; Zoom(1); }
                while (wheelAcc <= -120) { wheelAcc += 120; Zoom(-1); }
            };
            box.MouseMove += (s, e) => { if (e.Location != lastMouse) { lastMouse = e.Location; HoverAt(e.Location); } };
            box.MouseLeave += delegate { HideTip(); };
            box.MouseDown += delegate { HideTip(); };
            box.LostFocus += delegate { HideTip(); };
            box.KeyDown += delegate { HideTip(); };
            box.ViewportChanged += delegate
            {
                HideTip();
                if (large && !syntaxColoring && !rendering) ScheduleColors();
            };
            box.VScroll += delegate { HideTip(); };
            box.HScroll += delegate { HideTip(); };
            box.FoldingChanged += delegate
            {
                HideTip();
                if (!rendering) { UpdateBrackets(); pathTimer.Stop(); pathTimer.Start(); }
                if (large) { coloredBlocks.Clear(); colorProgress.Clear(); ScheduleColors(); }
            };
            box.Resize += delegate { HideTip(); };
            Deactivate += delegate { HideTip(); };

            matchTimer.Tick += delegate { matchTimer.Stop(); UpdateMatches(matchSelectPending); };
            pathTimer.Tick += delegate { pathTimer.Stop(); UpdatePath(); };
            treeTimer.Tick += delegate { treeTimer.Stop(); RebuildTree(); };
            colorTimer.Tick += delegate { colorTimer.Stop(); ColorVisible(); };
            box.Resize += delegate { if (large) ScheduleColors(); };

            tree.Dock = DockStyle.Fill;
            tree.NodePicked += OnTreePick;
            tree.PathPicked += delegate(string p) { pathLabel.Text = p; };

            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 5;
            editorSurface.Controls.Add(box);
            editorSurface.Controls.Add(box.FoldingGutter);
            split.Panel1.Controls.Add(editorSurface);
            split.Panel2.Controls.Add(tree);
            split.Panel2Collapsed = true;
            split.Panel2MinSize = 120;

            bar = new StatusStrip();
            pathLabel.Click += delegate { if (pathLabel.Text.Length > 0 && ClipboardUtil.SetText(pathLabel.Text)) SetStatus(L.F("已复制路径：{0}", pathLabel.Text), "ok"); };
            bar.Items.Add(status);
            bar.Items.Add(pathLabel);
            bar.Items.Add(hint);

            Controls.Add(split);
            Controls.Add(tools);
            Controls.Add(bar);

            ApplyChrome();
            LoadText(text ?? "", false, false);
            if (filePath != null)
            {
                string info = Path.GetFileName(filePath) + (fileEncoding != null ? " · " + fileEncoding : "");
                if (fileTruncated) SetStatus(L.T("文件较大，只加载了前 20 MB", "Large file: only the first 20 MB was loaded") + " · " + status.Text, "warn");
                fileInfo = info; hint.Text = info;
                hint.ToolTipText = filePath;
            }
            var h = ViewerCreated;
            if (h != null) h(this);
        }

        /// <summary>切换语言后重新打开所有查看窗口（保留内容和位置）</summary>
        public static void ReopenAll()
        {
            var list = new List<JsonViewer>();
            foreach (Form f in Application.OpenForms) { var v = f as JsonViewer; if (v != null) list.Add(v); }
            foreach (var v in list)
            {
                string t = v.FullText() ?? Crlf(v.RealText());
                var n = new JsonViewer(t, v.filePath, v.fileTruncated, v.fileEncoding) { StartPosition = FormStartPosition.Manual };
                n.Bounds = v.WindowState == FormWindowState.Normal ? v.Bounds : v.RestoreBounds;
                n.WindowState = v.WindowState == FormWindowState.Minimized ? FormWindowState.Normal : v.WindowState;
                n.TopMost = v.TopMost;
                n.Show();
                v.Close();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyChrome();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (btnTree.Checked) ShowTreePanel(true);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                matchTimer.Dispose(); pathTimer.Dispose(); treeTimer.Dispose(); colorTimer.Dispose(); tip.Dispose();
                if (treeFont != null) { treeFont.Dispose(); treeFont = null; }
            }
            base.Dispose(disposing);
        }

        string BoxText
        {
            get
            {
                if (cacheVersion != textVersion) { textCache = box.Text; cacheVersion = textVersion; }
                return textCache;
            }
        }

        // ---------- 设置菜单 ----------
        void BuildSettingsMenu()
        {
            var items = btnSettings.DropDownItems;
            items.Clear();
            string cur = ViewSettings.FontName;
            float size = ViewSettings.FontSize;

            var fontMenu = new ToolStripMenuItem(L.F("字体：{0}", cur));
            var names = new List<string>();
            foreach (var f in MonoFonts) if (FontStore.IsInstalled(f) && !names.Contains(f)) names.Add(f);
            foreach (var f in names) AddFontItem(fontMenu, f, cur);
            var imp = new List<string>(FontStore.Imported);
            if (imp.Count > 0)
            {
                fontMenu.DropDownItems.Add(new ToolStripSeparator());
                fontMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("已导入的字体")) { Enabled = false });
                foreach (var f in imp) AddFontItem(fontMenu, f, cur);
            }
            if (!names.Contains(cur) && !imp.Contains(cur))
            {
                fontMenu.DropDownItems.Add(new ToolStripSeparator());
                AddFontItem(fontMenu, cur, cur);
            }
            fontMenu.DropDownItems.Add(new ToolStripSeparator());
            fontMenu.DropDownItems.Add(L.T("其他系统字体…"), null, delegate { PickSystemFont(); });
            items.Add(fontMenu);

            items.Add(L.T("导入字体文件…"), null, delegate { ImportFont(); });

            var sizeMenu = new ToolStripMenuItem(L.F("字体大小：{0}", SizeText(size)));
            foreach (var s in Sizes)
            {
                float v = s;
                var it = new ToolStripMenuItem(SizeText(v)) { Checked = Math.Abs(v - size) < 0.01f };
                it.Click += delegate { SetFont(null, v); };
                sizeMenu.DropDownItems.Add(it);
            }
            sizeMenu.DropDownItems.Add(new ToolStripSeparator());
            sizeMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("放大"), null, delegate { Zoom(1); }) { ShortcutKeyDisplayString = L.T("Ctrl+= / Ctrl+滚轮") });
            sizeMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("缩小"), null, delegate { Zoom(-1); }) { ShortcutKeyDisplayString = "Ctrl+-" });
            sizeMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("恢复默认"), null, delegate { Zoom(0); }) { ShortcutKeyDisplayString = "Ctrl+0" });
            items.Add(sizeMenu);

            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem(L.T("宽松解析（无引号键/值、单引号、末尾逗号等）"), null, delegate
            {
                ViewSettings.Lenient = !ViewSettings.Lenient;
                SetStatus(ViewSettings.Lenient ? L.T("已开启宽松解析，点「格式化」重新解析") : L.T("已关闭宽松解析，只接受标准 JSON"), "muted");
            }) { Checked = ViewSettings.Lenient });
            items.Add(L.T("设置打码字段…"), null, delegate { EditMaskKeys(); });
            items.Add(new ToolStripMenuItem(L.En ? "Language: English → 切换为中文" : "界面语言：中文 → Switch to English", null, delegate
            {
                L.En = !L.En;
                ReopenAll();
            }));
            items.Add(new ToolStripMenuItem(L.T("黑夜模式"), null, delegate { ViewSettings.Dark = !ViewSettings.Dark; ApplyAll(); }) { Checked = ViewSettings.Dark });
            items.Add(new ToolStripMenuItem(L.T("开机自动启动", "Start with Windows"), null, delegate
            {
                try
                {
                    Settings.AutoStart = !Settings.AutoStart;
                    SetStatus(Settings.AutoStart ? L.T("已设置开机自动启动（托盘常驻）", "Will start with Windows (tray)") : L.T("已取消开机自动启动", "No longer starts with Windows"), "ok");
                }
                catch (Exception ex) { SetStatus(L.T("设置失败：", "Failed: ") + ex.Message, "err"); }
            }) { Checked = Settings.AutoStart });
            items.Add(L.T("打开已导入字体的文件夹"), null, delegate
            {
                Directory.CreateDirectory(FontStore.Dir);
                Process.Start("explorer.exe", "\"" + FontStore.Dir + "\"");
            });
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem(L.T("恢复默认字体和字号"), null, delegate { SetFont(ViewSettings.DefaultFont, ViewSettings.DefaultSize); }));

            ApplyRenderer(btnSettings.DropDown);
        }

        void AddFontItem(ToolStripMenuItem menu, string name, string cur)
        {
            var it = new ToolStripMenuItem(name) { Checked = string.Equals(name, cur, StringComparison.OrdinalIgnoreCase) };
            try { it.Font = new Font(name, 9f); } catch { }
            it.Click += delegate { SetFont(name, 0); };
            menu.DropDownItems.Add(it);
        }

        static string SizeText(float s) { return L.F("{0} 磅", s.ToString("0.#", CultureInfo.InvariantCulture)); }

        void ImportFont()
        {
            using (var dlg = new OpenFileDialog { Title = L.T("导入字体文件"), Filter = L.T("字体文件 (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc"), Multiselect = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string last = null; var errors = new List<string>();
                foreach (var f in dlg.FileNames)
                {
                    try { last = FontStore.Import(f); }
                    catch (Exception ex) { errors.Add(Path.GetFileName(f) + L.T("：") + ex.Message); }
                }
                if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors.ToArray()), L.T("部分字体导入失败"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (last != null) { SetFont(last, 0); SetStatus(L.F("已导入并使用字体：{0}", last), "ok"); }
            }
        }

        void PickSystemFont()
        {
            using (var dlg = new FontDialog { ShowEffects = false, AllowVerticalFonts = false, FontMustExist = true })
            {
                try { dlg.Font = new Font(ViewSettings.FontName, ViewSettings.FontSize); } catch { }
                DialogResult r;
                try { r = dlg.ShowDialog(this); }
                catch (ArgumentException) { MessageBox.Show(this, L.T("只支持 TrueType / OpenType 字体。"), L.T("无法使用该字体")); return; }
                if (r != DialogResult.OK) return;
                string name = dlg.Font.FontFamily.Name;
                try { string en = dlg.Font.FontFamily.GetName(1033); if (!string.IsNullOrEmpty(en)) name = en; } catch { }
                SetFont(name, (float)Math.Round(dlg.Font.SizeInPoints * 2) / 2f);
            }
        }

        void SetFont(string name, float size)
        {
            if (name != null) ViewSettings.FontName = name;
            if (size > 0) ViewSettings.FontSize = Math.Max(6, Math.Min(72, size));
            ApplyAll();
        }

        void Zoom(int dir)
        {
            float cur = ViewSettings.FontSize, next = ViewSettings.DefaultSize;
            if (dir > 0) { next = cur + 4; foreach (var s in Sizes) if (s > cur + 0.01f) { next = s; break; } }
            else if (dir < 0) { next = Math.Max(6, cur - 4); for (int i = Sizes.Length - 1; i >= 0; i--) if (Sizes[i] < cur - 0.01f) { next = Sizes[i]; break; } }
            SetFont(null, next);
            SetStatus(L.F("字号：{0}", SizeText(ViewSettings.FontSize)), "muted");
        }

        /// <summary>主题 / 字体变化后，刷新所有打开的查看窗口</summary>
        static void ApplyAll()
        {
            var list = new List<JsonViewer>();
            foreach (Form f in Application.OpenForms) { var v = f as JsonViewer; if (v != null) list.Add(v); }
            foreach (var v in list) v.Restyle();
        }

        void EditMaskKeys()
        {
            var th = Theme.Current;
            using (var f = new Form { Text = L.T("设置打码字段"), FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, Font = Font })
            {
                var lbl = new Label { Text = L.T("键名包含以下关键字时，值会显示为 ******（不区分大小写；4 个字母以上按“包含”匹配，例如 token 也会命中 access_token）。\n值本身是 JWT 的字符串无论键名都会打码。用逗号或换行分隔："), Dock = DockStyle.Top, Height = 64, Padding = new Padding(10, 10, 10, 0) };
                var tb = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Text = string.Join(", ", Mask.ParseKeys(ViewSettings.MaskKeys).ToArray()), Font = new Font("Consolas", 10f) };
                var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
                var ok = new Button { Text = L.T("确定"), DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = L.T("取消"), DialogResult = DialogResult.Cancel, AutoSize = true };
                var reset = new Button { Text = L.T("恢复默认"), AutoSize = true };
                reset.Click += delegate { tb.Text = Mask.DefaultKeys; };
                panel.Controls.AddRange(new Control[] { cancel, ok, reset });
                var mid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 4) };
                mid.Controls.Add(tb);
                f.Controls.Add(mid); f.Controls.Add(panel); f.Controls.Add(lbl);
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.ClientSize = new Size(520, 260);
                f.BackColor = th.Bar; lbl.ForeColor = th.BarFore; tb.BackColor = th.InputBack; tb.ForeColor = th.Fore;
                foreach (var b in new[] { ok, cancel, reset }) { b.FlatStyle = FlatStyle.Flat; b.BackColor = th.Menu; b.ForeColor = th.BarFore; b.FlatAppearance.BorderColor = th.Border; }
                f.HandleCreated += delegate { Native.DarkTitleBar(f.Handle, th.Dark); };
                if (f.ShowDialog(this) != DialogResult.OK) return;
                ViewSettings.MaskKeys = string.Join(", ", Mask.ParseKeys(tb.Text).ToArray());
                if (masked) { SetMasked(false); SetMasked(true); rendering = true; btnMask.Checked = masked; rendering = false; }
                else SetStatus(L.T("打码字段已更新"), "ok");
            }
        }

        // ---------- 主题 ----------
        static void ApplyRenderer(ToolStripDropDown dd)
        {
            dd.Renderer = new ThemeRenderer(Theme.Current);
            foreach (ToolStripItem it in dd.Items)
            {
                var mi = it as ToolStripDropDownItem;
                if (mi != null && mi.HasDropDownItems) ApplyRenderer(mi.DropDown);
            }
        }

        Font TreeFont()
        {
            string name = ViewSettings.FontName;
            if (!FontStore.IsInstalled(name)) name = "Consolas"; // 导入的私有字体 GDI+ 不认识，树形视图用 Consolas
            float size = Math.Max(8, Math.Min(16, ViewSettings.FontSize - 0.5f));
            if (treeFont == null || treeFont.Name != name || Math.Abs(treeFont.SizeInPoints - size) > 0.01f)
            {
                var old = treeFont;
                treeFont = new Font(name, size);
                if (old != null) old.Dispose();
            }
            return treeFont;
        }

        void ApplyChrome()
        {
            if (tools == null || bar == null || btnSettings == null || search == null || tree == null) return; // 构造过程中还没建好
            var th = Theme.Current;
            var r = new ThemeRenderer(th);
            tools.Renderer = r; bar.Renderer = r;
            tools.BackColor = bar.BackColor = th.Bar;
            tools.ForeColor = bar.ForeColor = th.BarFore;
            BackColor = th.Bar;
            split.BackColor = th.Border;
            editorSurface.BackColor = split.Panel1.BackColor = split.Panel2.BackColor = th.Back;
            box.BackColor = th.Back;
            box.HighlightColor = th.Highlight;
            if (box.ContextMenuStrip != null) ApplyRenderer(box.ContextMenuStrip);
            ApplyRenderer(btnSettings.DropDown);
            ApplyRenderer(btnFolding.DropDown);
            box.FoldingGutter.Invalidate();
            tree.ApplyTheme(th, TreeFont());
            search.BackColor = th.InputBack;
            search.ForeColor = th.Fore;
            hint.ForeColor = th.Muted;
            pathLabel.ForeColor = th.Accent;
            status.ForeColor = KindColor(statusKind);
            btnTheme.Text = th.Dark ? L.T("☀ 白天") : L.T("☾ 黑夜");
            btnTheme.ToolTipText = th.Dark ? L.T("切换到白天模式") : L.T("切换到黑夜模式");
            UpdateCount();
            if (IsHandleCreated)
            {
                Native.DarkTitleBar(Handle, th.Dark);
                Native.SetWindowTheme(box.Handle, th.Dark ? "DarkMode_Explorer" : "Explorer", null);
            }
        }

        Color KindColor(string kind)
        {
            var th = Theme.Current;
            switch (kind) { case "ok": return th.Ok; case "warn": return th.Warn; case "err": return th.Err; default: return th.Muted; }
        }

        void SetStatus(string text, string kind)
        {
            statusKind = kind;
            status.Text = text;
            status.ForeColor = KindColor(kind);
        }

        /// <summary>只换主题 / 字体 / 字号：保持当前文字（含未完成的编辑）、光标和滚动位置不变</summary>
        void Restyle()
        {
            ApplyChrome();
            ReplaceDisplay(box.Text);
            box.Invalidate();
            if (btnTree.Checked) RebuildTree();
        }

        /// <summary>替换框内文字并重新着色，尽量保持滚动位置和选区</summary>
        void ReplaceDisplay(string text)
        {
            int selStart = box.SelectionStart, selLen = box.SelectionLength;
            int firstChar = box.CharIndexAt(new Point(1, 1));
            bool keepEdited = edited;
            SetContent(text, JsonTools.FindJsonRanges(text));
            rendering = true;
            try
            {
                int fc = Math.Min(firstChar, box.TextLength);
                box.Select(fc, 0);
                var top = box.CharPosition(fc);
                var p = box.ScrollPos;
                box.ScrollPos = new Point(p.X, Math.Max(0, p.Y + top.Y));
                var keep = box.ScrollPos;
                int ss = Math.Min(selStart, box.TextLength);
                box.Select(ss, Math.Max(0, Math.Min(selLen, box.TextLength - ss)));
                box.ScrollPos = keep;
            }
            finally { rendering = false; }
            edited = keepEdited;
            UpdateTitle();
            UpdateBrackets();
        }

        void SetContent(string text, List<int[]> ranges)
        {
            var th = Theme.Current;
            rendering = true;
            try
            {
                box.StopSmooth();
                box.ForeColor = th.Syntax[1];
                large = text.Length > LargeText;
                largeNoted = false;
                coloredBlocks.Clear(); colorProgress.Clear();
                box.RefreshEditorMetrics();
                if (!large) box.Rtf = Rtf.Build(text, ranges, th, ViewSettings.FontName, ViewSettings.FontSize);
                else
                {
                    // 大文件：整段 RTF 着色很慢（RichEdit 解析 RTF 的耗时随长度超线性增长），改为纯文字 + 按需着色
                    box.Text = text;
                    Native.SetAllCharFormat(box.Handle, ViewSettings.FontName, ViewSettings.FontSize, th.Syntax[1]); // 直接设定字体（导入的字体也能用）
                    largeRanges = ranges;
                }
                box.ClearUndo();
            }
            finally { rendering = false; }
            box.RebuildFolding(BoxText, false);
            if (large) { ColorVisible(); box.ClearUndo(); }
            if (search.Text.Length > 0) UpdateMatches(false);
            UpdateBrackets();
        }

        void ScheduleColors(int delay = 120)
        {
            colorTimer.Stop(); colorTimer.Interval = delay; colorTimer.Start();
        }

        /// <summary>滚动时不改颜色；空闲时按约 6ms 时间片补色，不移动选区或使结构缓存失效。</summary>
        void ColorVisible()
        {
            if (!large || !box.IsHandleCreated || box.DocumentLength == 0 || syntaxColoring) return;
            if (box.IsSmoothScrolling) { ScheduleColors(); return; }
            string text = BoxText;
            if (syntaxIndex == null || syntaxVersion != textVersion)
            {
                // 当前文本已归一化为 LF，词法索引不能沿用加载前的 CRLF 位置。
                largeRanges = JsonTools.FindJsonRanges(text);
                syntaxIndex = new SyntaxIndex(text, largeRanges);
                syntaxVersion = textVersion;
            }
            int top = box.CharIndexAt(Point.Empty);
            int bottom = box.CharIndexAt(new Point(Math.Max(0, box.ClientSize.Width - 1), Math.Max(0, box.ClientSize.Height - 1)));
            var candidates = new List<int>(); var seen = new HashSet<int>();
            Action<int, int> addBlocks = (start, length) =>
            {
                int last = (start + length - 1) / ColorBlock;
                for (int block = start / ColorBlock; block <= last; block++)
                    if (!coloredBlocks.Contains(block) && seen.Add(block)) candidates.Add(block);
            };
            // 可见内容优先，上下预取其次；折叠块中间可能有数 MB 字符，不应逐块遍历。
            box.Folding.EachVisibleRange(top, bottom - top + 1, addBlocks);
            int from = Math.Max(0, top - ColorBlock), to = Math.Min(text.Length, bottom + ColorBlock + 1);
            box.Folding.EachVisibleRange(from, to - from, addBlocks);
            if (candidates.Count == 0) return;
            bool modified = box.Modified;
            var th = Theme.Current;
            syntaxColoring = true;
            try
            {
                using (var colors = RichEditFold.Colors(box))
                {
                    var budget = Stopwatch.StartNew();
                    foreach (int block in candidates)
                    {
                        int start; if (!colorProgress.TryGetValue(block, out start)) start = block * ColorBlock;
                        int end = Math.Min(text.Length, (block + 1) * ColorBlock);
                        int reached = syntaxIndex.VisitUntil(start, end, (i, length, color) =>
                        {
                            box.Folding.EachVisibleRange(i, length, (s, count) => colors.Set(s, count, th.Syntax[color]));
                            return budget.ElapsedMilliseconds < 6;
                        });
                        if (reached >= end) { coloredBlocks.Add(block); colorProgress.Remove(block); }
                        else { colorProgress[block] = reached; break; }
                        if (budget.ElapsedMilliseconds >= 6) break;
                    }
                }
            }
            finally { box.Modified = modified; syntaxColoring = false; }
            box.Invalidate();
            foreach (int block in candidates)
                if (!coloredBlocks.Contains(block)) { ScheduleColors(16); break; }
        }

        void AddFoldingMenu(ToolStripItemCollection items, bool context = false)
        {
            var collapse = new ToolStripMenuItem(L.T("折叠此层级", "Fold current block"), null, delegate { SetFold(true, false, context ? menuIndex : -1); });
            collapse.ShortcutKeyDisplayString = "Ctrl+Shift+[";
            var expand = new ToolStripMenuItem(L.T("展开此层级", "Expand current block"), null, delegate { SetFold(false, false, context ? menuIndex : -1); });
            expand.ShortcutKeyDisplayString = "Ctrl+Shift+]";
            items.Add(collapse); items.Add(expand);
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem(L.T("全部折叠", "Fold all"), null, delegate { SetFold(true, true); }) { ShortcutKeyDisplayString = "Ctrl+Alt+[" });
            items.Add(new ToolStripMenuItem(L.T("全部展开", "Expand all"), null, delegate { SetFold(false, true); }) { ShortcutKeyDisplayString = "Ctrl+Alt+]" });
        }

        void SetFold(bool collapsed, bool all, int position = -1)
        {
            HideTip();
            if (all) box.FoldAll(collapsed);
            else if (position >= 0) box.Folding.SetAt(position, collapsed);
            else box.FoldCurrent(collapsed);
            box.Focus();
        }

        // ---------- 右键菜单 ----------
        ContextMenuStrip BuildBoxMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add(L.T("撤销 (Ctrl+Z)"), null, delegate { Undo(); });
            m.Items.Add(L.T("重做 (Ctrl+Y)"), null, delegate { box.Redo(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("剪切 (Ctrl+X)"), null, delegate { Cut(); });
            m.Items.Add(L.T("复制 (Ctrl+C)"), null, delegate { CopySelection(); });
            m.Items.Add(L.T("粘贴 (Ctrl+V)"), null, delegate { PastePlain(); });
            m.Items.Add(L.T("全选 (Ctrl+A)"), null, delegate { box.SelectAll(); });
            m.Items.Add(new ToolStripSeparator());
            var folding = new ToolStripMenuItem(L.T("代码折叠", "Code folding"));
            AddFoldingMenu(folding.DropDownItems, true);
            m.Items.Add(folding);
            m.Items.Add(new ToolStripSeparator());
            miDecode.Click += delegate { var j = Jwt.Decode(menuJwt); if (j != null) new JsonViewer(j.Report()).Show(); };
            miNested.Click += delegate { if (menuNested != null) new JsonViewer(menuNested).Show(); };
            miPath.Click += delegate { string p = PathAt(menuIndex); if (p != null && ClipboardUtil.SetText(p)) SetStatus(L.F("已复制路径：{0}", p), "ok"); };
            m.Items.Add(miDecode);
            m.Items.Add(miNested);
            m.Items.Add(miPath);
            m.Items.Add(L.T("搜索选中内容"), null, delegate
            {
                if (box.SelectionLength > 0) { search.Text = box.SelectedText.Split('\n')[0]; search.Focus(); }
            });
            m.Items.Add(L.T("在新窗口解析选中内容"), null, delegate
            {
                if (box.SelectionLength > 0) new JsonViewer(Crlf(box.SelectedText)).Show();
            });
            m.Opening += delegate
            {
                var pt = box.PointToClient(Cursor.Position);
                menuIndex = box.CharIndexAt(pt);
                int s, e;
                string val;
                string kind = TokenAt(menuIndex, out s, out e, out val);
                var jwt = kind == "string" ? Jwt.Decode(val) : null;
                menuJwt = jwt != null ? val : null;
                string t = kind == "string" && jwt == null ? val.Trim() : null;
                menuNested = t != null && t.Length >= 2 && (t[0] == '{' || t[0] == '[') && JsonTools.Pretty(t) != null ? t : null;
                miDecode.Visible = menuJwt != null;
                miNested.Visible = menuNested != null;
                miPath.Visible = PathAt(menuIndex) != null;
            };
            return m;
        }

        // ---------- 解析 / 显示 ----------
        string RealText() { return masked ? realText : box.Text; }

        /// <summary>取得完整内容（含非 JSON 文字和所有编辑）。“只看 JSON”时把编辑合并回原文；失败返回 null。</summary>
        string FullText()
        {
            string t = Crlf(RealText());
            return shownOnly ? JsonTools.MergeOnlyJson(pieces, t) : t;
        }

        /// <summary>基于完整内容重新解析并按当前视图显示（可用 Ctrl+Z 撤销）</summary>
        bool Reprocess(bool minify, bool expand)
        {
            string full = FullText();
            if (full == null)
            {
                var ans = MessageBox.Show(this,
                    L.T("「只看 JSON」中的分隔行（// ===== JSON 片段 n / N =====）被修改或删除，无法把编辑合并回原文。\n\n") +
                    L.T("是：丢弃日志等非 JSON 文字，只保留当前窗口里的内容继续\n否：取消，先把分隔行改回来（可 Ctrl+Z 撤销）"),
                    L.T("无法合并编辑"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (ans != DialogResult.Yes) return false;
                full = JsonTools.StripHeaders(Crlf(RealText()));
            }
            var snap = new Snapshot { Display = RealText(), Pieces = pieces, ShownOnly = shownOnly };
            LoadText(full, minify, expand);
            if (RealText() != snap.Display)
            {
                snap.After = RealText();
                history.Add(snap);
                if (history.Count > 30) history.RemoveAt(0);
                if (statusKind != "err") status.Text += L.T("（Ctrl+Z 可撤销）");
            }
            return true;
        }

        void LoadText(string text, bool minify, bool expand)
        {
            text = text ?? "";
            lastMinify = minify;
            var r = JsonTools.Process(text, new FormatOptions { Minify = minify, Expand = expand, Lenient = ViewSettings.Lenient });
            // 解析失败只影响格式化结果，不影响打开窗口；零片段时始终使用完整输入。
            pieces = r.Count > 0 ? r.Pieces : new List<Piece> { new Piece { Text = text } };
            shownOnly = btnOnly.Checked && r.Count > 0;
            var view = JsonTools.BuildView(pieces, shownOnly);
            ShowReal(view.Text, view.JsonRanges);
            rendering = true;
            try { box.Select(0, 0); box.ScrollPos = Point.Empty; }
            finally { rendering = false; }

            if (r.Count > 0)
            {
                bool hidden = shownOnly && pieces.Exists(p => !p.IsJson && p.Text.Trim().Length > 0);
                string msg = r.Warning ?? L.F("已解析 {0} 段 JSON", r.Count);
                if (expand)
                {
                    msg = r.Expanded > 0 ? L.F("已展开 {0} 个嵌套 JSON 字符串", r.Expanded) : L.T("没有找到内容是 JSON 的字符串");
                    if (r.Warning != null) msg += L.T("；", "; ") + r.Warning;
                }
                SetStatus(msg + "", r.Warning != null ? "warn" : "ok");
                titleBase = r.Count == 1 ? L.T("JSON 解析") : L.F("JSON 解析（{0} 段）", r.Count);
            }
            else
            {
                bool empty = text.Length == 0;
                string message = empty ? L.T("在此粘贴任意文本，然后按 Ctrl+Enter 尝试格式化")
                    : L.T("未识别到 JSON 对象或数组，已保留全部原文，可编辑后重试") + (string.IsNullOrEmpty(r.Error) ? "" : " — " + r.Error);
                SetStatus(message, empty ? "muted" : "warn");
                titleBase = L.T("JSON 解析");
            }
            UpdateTitle();
            UpdateBrackets();
            UpdatePath();
            if (btnTree.Checked) RebuildTree();
        }

        /// <summary>显示真实内容；打码模式下显示打码后的内容，真实内容另存</summary>
        void ShowReal(string text, List<int[]> ranges)
        {
            if (!masked) { SetContent(text, ranges); return; }
            string boxForm = text.Replace("\r\n", "\n");
            realText = boxForm;
            int n;
            string m = Mask.Apply(boxForm, Mask.ParseKeys(ViewSettings.MaskKeys), out n);
            SetContent(m, JsonTools.FindJsonRanges(m));
        }

        void UpdateTitle()
        {
            if (filePath != null && !titleBase.StartsWith(Path.GetFileName(filePath))) titleBase = Path.GetFileName(filePath) + " - " + titleBase;
            if (large && statusKind != "err" && !largeNoted) { largeNoted = true; }
            Text = titleBase + (edited ? " *" : "") + (masked ? L.T(" · 已打码") : "");
            hint.Text = masked ? (fileInfo.Length > 0 ? fileInfo + " · " : "") + L.T("已打码 · 只读") : fileInfo;
        }

        // ---------- 撤销 ----------
        void Undo()
        {
            if (masked) { MaskedHint(); return; }
            if (box.CanUndo) { box.Undo(); return; }
            if (history.Count == 0) { SetStatus(L.T("没有可撤销的操作"), "muted"); return; }
            var s = history[history.Count - 1];
            if (box.Text != s.After)
            {
                var ans = MessageBox.Show(this, L.T("撤销到上一次格式化之前，会丢掉之后做的编辑。继续吗？"), L.T("撤销"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;
            }
            history.RemoveAt(history.Count - 1);
            pieces = s.Pieces;
            shownOnly = s.ShownOnly;
            rendering = true; btnOnly.Checked = s.ShownOnly; rendering = false;
            ReplaceDisplay(s.Display);
            SetStatus(L.T("已撤销上一次格式化操作"), "ok");
            if (btnTree.Checked) RebuildTree();
        }

        // ---------- 打码 ----------
        bool SetMasked(bool on)
        {
            if (on == masked) return true;
            if (on)
            {
                string real = box.Text;
                int n;
                string m = Mask.Apply(real, Mask.ParseKeys(ViewSettings.MaskKeys), out n);
                if (n == 0) { SetStatus(L.T("没有找到需要打码的字段"), "muted"); return false; }
                realText = real;
                masked = true;
                ReplaceDisplay(m);
                box.ReadOnly = true;
                box.BackColor = Theme.Current.Back;
                SetStatus(L.F("已打码 {0} 个字段", n), "ok");
            }
            else
            {
                masked = false;
                box.ReadOnly = false;
                ReplaceDisplay(realText);
                realText = null;
                SetStatus(L.T("已取消打码"), "muted");
            }
            UpdateTitle();
            if (btnTree.Checked) RebuildTree();
            return true;
        }

        void MaskedHint() { SetStatus(L.T("打码状态下只读，先点「打码」取消后再编辑"), "warn"); }

        // ---------- 搜索 ----------
        /// <summary>重新计算所有匹配并高亮；select=true 时选中光标后最近的一个</summary>
        void UpdateMatches(bool select)
        {
            if (box.IsSmoothScrolling && search.Text.Length > 0)
            { matchSelectPending |= select; matchTimer.Start(); return; }
            matchSelectPending = false;
            matches = new List<int[]>();
            matchError = null;
            string q = search.Text;
            if (q.Length > 0)
            {
                try
                {
                    var opt = RegexOptions.Multiline | (btnCase.Checked ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    var re = new Regex(btnRegex.Checked ? q : Regex.Escape(q), opt, TimeSpan.FromSeconds(1));
                    for (var m = re.Match(BoxText); m.Success && matches.Count < MaxMatches; m = m.NextMatch())
                        if (m.Length > 0) matches.Add(new[] { m.Index, m.Length });
                }
                catch (ArgumentException) { matchError = L.T("正则有误"); }
                catch (RegexMatchTimeoutException) { matchError = L.T("正则太慢，已停止"); }
            }
            box.Highlights = matches;
            box.Invalidate();
            if (select && matches.Count > 0)
            {
                int cur = box.SelectionStart;
                int idx = matches.FindIndex(x => x[0] >= cur);
                SelectMatch(idx < 0 ? 0 : idx);
            }
            UpdateCount();
        }

        int CurrentMatch()
        {
            int s = box.SelectionStart, l = box.SelectionLength;
            return matches.FindIndex(x => x[0] == s && x[1] == l);
        }

        void SelectMatch(int i)
        {
            rendering = true;
            try { box.RevealRange(matches[i][0], matches[i][1]); box.Select(matches[i][0], matches[i][1]); CenterOn(matches[i][0]); }
            finally { rendering = false; }
            UpdateBrackets();
            box.Invalidate();
            pathTimer.Stop(); pathTimer.Start();
        }

        void Find(bool forward)
        {
            if (matchTimer.Enabled || matchSelectPending) { matchTimer.Stop(); UpdateMatches(false); }
            if (matches.Count == 0) { UpdateCount(); return; }
            int cur = CurrentMatch(), idx;
            if (cur >= 0) idx = forward ? (cur + 1) % matches.Count : (cur - 1 + matches.Count) % matches.Count;
            else
            {
                int pos = box.SelectionStart;
                idx = forward ? matches.FindIndex(x => x[0] >= pos) : matches.FindLastIndex(x => x[0] < pos);
                if (idx < 0) idx = forward ? 0 : matches.Count - 1;
            }
            SelectMatch(idx);
            UpdateCount();
        }

        void UpdateCount()
        {
            var th = Theme.Current;
            if (search == null) return;
            if (matchError != null) { searchCount.Text = matchError; searchCount.ForeColor = th.Err; return; }
            if (search.Text.Length == 0) { searchCount.Text = ""; return; }
            if (matches.Count == 0) { searchCount.Text = L.T("无结果"); searchCount.ForeColor = th.Err; return; }
            int cur = CurrentMatch();
            searchCount.Text = (cur >= 0 ? (cur + 1).ToString() : "-") + "/" + matches.Count + (matches.Count >= MaxMatches ? "+" : "");
            searchCount.ForeColor = th.Muted;
        }

        void CenterOn(int charIndex)
        {
            box.StopSmooth();
            box.RevealRange(charIndex, 0);
            int y = box.CharPosition(charIndex).Y;
            if (y >= 0 && y < box.ClientSize.Height - 40) return; // 已经在可见区域
            var p = box.ScrollPos;
            box.ScrollPos = new Point(p.X, Math.Max(0, p.Y + y - box.ClientSize.Height / 3));
        }

        // ---------- 括号配对 ----------
        void UpdateBrackets(int clickedIndex = -1)
        {
            if (IsDisposed || Disposing || box.IsDisposed) return;
            if (box.SelectionLength != 0) { box.ClearBracketMatch(); return; }
            string text = BoxText;
            int caret = box.SelectionStart;
            // 输入光标是字符之间的位置：无论键盘移动还是鼠标落点，都检查右侧和左侧字符。
            // MouseUp 命中换行/逗号时，不能覆盖 SelectionChanged 已找到的相邻括号。
            if (!BracketIndex.IsBracket(text, caret) && !BracketIndex.IsBracket(text, caret - 1))
            { box.ClearBracketMatch(); return; }
            if (bracketIndex == null || bracketVersion != textVersion)
            {
                bracketIndex = box.FoldingIndexFor(text) ?? new BracketIndex(text);
                bracketVersion = textVersion;
            }
            int at = caret, other;
            // 两侧恰好都是括号时保留直接点击的层级；非相邻的鼠标命中不参与判断。
            if (clickedIndex >= 0 && (clickedIndex == caret || clickedIndex == caret - 1) && bracketIndex.TryGetPair(clickedIndex, out other))
                at = clickedIndex;
            else if (!bracketIndex.TryGetPair(at, out other))
            {
                at = caret - 1;
                if (!bracketIndex.TryGetPair(at, out other)) { box.ClearBracketMatch(); return; }
            }
            int open = Math.Min(at, other), close = Math.Max(at, other);
            box.SetBracketMatch(open, close, at, bracketIndex.ContextAt(open), bracketIndex.ContextAt(close));
        }

        // ---------- 结构 / 路径 / 树形 ----------
        List<JNode> GetDom()
        {
            if (domVersion == textVersion) return domRoots;
            domVersion = textVersion;
            domRoots = new List<JNode>();
            string t = BoxText;
            if (t.Length > 8000000) return domRoots;
            foreach (var r in JsonTools.FindJsonRanges(t))
            {
                var n = Dom.Parse(t, r[0]);
                if (n != null) domRoots.Add(n);
            }
            return domRoots;
        }

        string PathAt(int idx)
        {
            if (idx < 0) return null;
            var roots = GetDom();
            for (int i = 0; i < roots.Count; i++)
            {
                var n = Dom.NodeAt(roots[i], idx);
                if (n == null) continue;
                string p = Dom.PathOf(n);
                return roots.Count > 1 ? L.F("JSON 片段 {0}", i + 1) + " › " + p : p;
            }
            return null;
        }

        void UpdatePath()
        {
            if (box.IsSmoothScrolling) { pathTimer.Start(); return; }
            if (BoxText.Length > 3000000) { pathLabel.Text = ""; return; }
            pathLabel.Text = PathAt(box.SelectionStart) ?? "";
        }

        void ShowTreePanel(bool show)
        {
            split.Panel2Collapsed = !show;
            if (show)
            {
                int w = split.Width;
                if (w > 300) split.SplitterDistance = Math.Max(200, w * 60 / 100);
                RebuildTree();
            }
        }

        void RebuildTree()
        {
            if (!btnTree.Checked || split.Panel2Collapsed) return;
            if (box.IsSmoothScrolling) { treeTimer.Start(); return; }
            tree.SetRoots(GetDom());
        }

        void OnTreePick(JNode n)
        {
            var real = n;
            while (real != null && real.Virtual) real = real.Parent;
            if (real == null || domVersion != textVersion) return;
            rendering = true;
            try
            {
                box.RevealRange(real.Start, Math.Max(0, real.End - real.Start));
                box.Select(real.Start, Math.Max(0, Math.Min(real.End, box.TextLength) - real.Start)); CenterOn(real.Start);
            }
            finally { rendering = false; }
            UpdateBrackets();
            box.Invalidate();
        }

        // ---------- 悬停提示：时间戳 / JWT ----------
        /// <summary>找出 idx 所在的字符串或数字。返回 "string"/"number"/null；val 为字符串解码后的内容或数字原文</summary>
        string TokenAt(int idx, out int start, out int end, out string val)
        {
            start = end = -1; val = null;
            string t = BoxText;
            if (idx < 0 || idx >= t.Length) return null;
            int ls = t.LastIndexOf('\n', idx) + 1;
            int le = t.IndexOf('\n', idx); if (le < 0) le = t.Length;
            int i = ls;
            while (i < le)
            {
                if (i > idx) break;
                char c = t[i];
                if (c == '"')
                {
                    int j = i + 1;
                    while (j < le && t[j] != '"') j += t[j] == '\\' ? 2 : 1;
                    if (j >= le) return null;
                    if (idx >= i && idx <= j)
                    {
                        start = i; end = j + 1;
                        val = JsonTools.Unescape(t, i + 1, j - i - 1);
                        return "string";
                    }
                    i = j + 1; continue;
                }
                if (c >= '0' && c <= '9' && (i == ls || !char.IsLetterOrDigit(t[i - 1])))
                {
                    int j = i;
                    while (j < le && t[j] >= '0' && t[j] <= '9') j++;
                    if (idx >= i && idx < j && (j == le || !char.IsLetter(t[j])))
                    {
                        start = i; end = j; val = t.Substring(i, j - i);
                        return "number";
                    }
                    i = j; continue;
                }
                i++;
            }
            return null;
        }

        void HoverAt(Point pt)
        {
            if (box.IsSmoothScrolling) { HideTip(); return; }
            if (BoxText.Length == 0 || box.IsFoldPlaceholder(pt)) { HideTip(); return; }
            int idx = box.CharIndexAt(pt);
            var cp = box.CharPosition(idx);
            if (Math.Abs(cp.Y + box.LineHeight / 2 - pt.Y) > box.LineHeight || Math.Abs(cp.X - pt.X) > 40) { HideTip(); return; }
            // 同一段长字符串内移动时复用解析结果，但每次都跟随鼠标更新提示位置。
            if (tipVersion == textVersion && idx >= tipStart && idx < tipEnd)
            {
                if (tipText != null) tip.ShowNear(tipText, box, pt);
                return;
            }
            int s, e;
            string val;
            string kind = TokenAt(idx, out s, out e, out val);
            if (kind == null) { HideTip(); return; }
            string text = null;
            if (kind == "number") text = TimeHint.Describe(val);
            else if (val.Length <= 15) text = TimeHint.Describe(val);
            if (text == null && kind == "string" && val.Length >= 20)
            {
                var jwt = Jwt.Decode(val);
                if (jwt != null) text = jwt.Summary();
                else
                {
                    string tt = val.Trim();
                    if (tt.Length >= 2 && (tt[0] == '{' || tt[0] == '[') && JsonTools.Pretty(tt) != null)
                        text = L.T("这个字符串的内容是 JSON\n点工具栏「展开嵌套」可就地展开，或右键 → 在新窗口解析");
                }
            }
            tip.HideFrom(box);
            tipStart = s; tipEnd = e; tipVersion = textVersion; tipText = text;
            if (text != null) tip.ShowNear(text, box, pt);
        }

        void HideTip()
        {
            if (tipStart == -2) return;
            tipStart = -2; tipEnd = 0; tipVersion = -1; tipText = null;
            lastMouse = new Point(-1, -1);
            tip.HideFrom(box);
        }

        // ---------- 复制 / 粘贴 ----------
        static string Crlf(string s) { return s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n"); }

        void CopyAll()
        {
            if (box.TextLength == 0) return;
            string t = Crlf(box.Text);
            if (shownOnly) t = Regex.Replace(JsonTools.StripHeaders(t), @"(\r\n){3,}", "\r\n\r\n");
            if (!ClipboardUtil.SetText(t)) { ClipboardBusy(); return; }
            SetStatus(shownOnly ? L.T("已复制全部 JSON") : L.T("已复制全部内容"), "ok");
        }

        bool CopySelection()
        {
            if (box.SelectionLength == 0) return false;
            if (ClipboardUtil.SetText(Crlf(box.SelectedText))) return true;
            ClipboardBusy();
            return false;
        }

        void Cut() { if (masked) { MaskedHint(); return; } if (CopySelection()) { box.PrepareFoldEdit(); box.SelectedText = ""; } }

        void PastePlain()
        {
            if (masked) { MaskedHint(); return; }
            string t = ClipboardUtil.GetText();
            if (t == null) { ClipboardBusy(); return; }
            if (t.Length == 0) return;
            box.PrepareFoldEdit();
            // 粘贴进来的文字统一用普通样式，不继承光标处的颜色和缩进；Ctrl+Enter 可重新着色
            box.SelectionColor = Theme.Current.Syntax[2];
            box.SelectionIndent = 0;
            box.SelectionHangingIndent = 0;
            box.SelectedText = t;
        }

        void ClipboardBusy() { SetStatus(L.T("剪贴板正被其它程序占用，请稍后再试"), "err"); }

        // ---------- 快捷键 ----------
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.F: search.Focus(); search.SelectAll(); return true;
                case Keys.F3: Find(true); return true;
                case Keys.Shift | Keys.F3: Find(false); return true;
                case Keys.Control | Keys.Enter: Reprocess(false, false); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add: Zoom(1); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: Zoom(-1); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: Zoom(0); return true;
            }
            if (box.Focused)
            {
                switch (keyData)
                {
                    case Keys.Control | Keys.Shift | Keys.OemOpenBrackets: SetFold(true, false); return true;
                    case Keys.Control | Keys.Shift | Keys.Oem6: SetFold(false, false); return true;
                    case Keys.Control | Keys.Alt | Keys.OemOpenBrackets: SetFold(true, true); return true;
                    case Keys.Control | Keys.Alt | Keys.Oem6: SetFold(false, true); return true;
                    case Keys.Control | Keys.Z: Undo(); return true;
                    case Keys.Control | Keys.V:
                    case Keys.Shift | Keys.Insert: PastePlain(); return true;   // 只粘贴纯文本
                    case Keys.Control | Keys.C:
                    case Keys.Control | Keys.Insert: CopySelection(); return true;
                    case Keys.Control | Keys.X:
                    case Keys.Shift | Keys.Delete: Cut(); return true;
                    case Keys.Control | Keys.Y: box.Redo(); return true;
                    // 屏蔽 RichEdit 自带的对齐 / 行距 / 加粗等格式快捷键，避免误触改乱排版
                    case Keys.Control | Keys.E: case Keys.Control | Keys.L: case Keys.Control | Keys.R: case Keys.Control | Keys.J:
                    case Keys.Control | Keys.D1: case Keys.Control | Keys.D2: case Keys.Control | Keys.D5:
                    case Keys.Control | Keys.B: case Keys.Control | Keys.I: case Keys.Control | Keys.U:
                    case Keys.Control | Keys.Shift | Keys.L: case Keys.Control | Keys.Shift | Keys.A:
                    case Keys.Control | Keys.Shift | Keys.Oemplus: case Keys.Control | Keys.Shift | Keys.OemMinus:
                        return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- 诊断 ----------
        public static string SelfTestCopy(string input)
        {
            var o = new StringBuilder();
            var v = new JsonViewer(input);
            var h = v.Handle; h = v.box.Handle;
            string shown = Crlf(v.box.Text);
            string expect = Crlf(JsonTools.BuildView(JsonTools.Process(input, new FormatOptions { Lenient = true }).Pieces, false).Text);
            o.Append("shown==expected: " + (shown == expect) + "\r\n");
            v.box.SelectAll(); v.CopySelection();
            string clip = ClipboardUtil.GetText();
            o.Append("copy==shown: " + (clip == shown) + "\r\n");
            v.box.Clear(); v.PastePlain();
            o.Append("paste==copy: " + (Crlf(v.box.Text) == clip) + "\r\n");
            v.Reprocess(false, false);
            o.Append("reformat after paste stable: " + (Crlf(v.box.Text) == shown) + "\r\n");
            v.Dispose();
            return o.ToString();
        }

        public static string SelfTestUi(string input)
        {
            var o = new StringBuilder();
            ViewSettings.Dark = false; ViewSettings.FontName = "Consolas"; ViewSettings.FontSize = 10.5f; ViewSettings.ShowTree = false;

            // 宽松解析
            var lr = JsonTools.Process("{'a': 1, b: True, \"c\": [1, 2, ], /* x */ \"d\": None,}", new FormatOptions { Lenient = true });
            o.Append("lenient: count=" + lr.Count + " warn=" + lr.Warning + "\r\n" + (lr.Count > 0 ? lr.Pieces[0].Text.Replace("\r\n", " ") : "") + "\r\n");
            var sr = JsonTools.Process("{'a': 1}", new FormatOptions { Lenient = false });
            o.Append("strict rejects single quote: " + (sr.Count == 0) + "\r\n");
            var lg = JsonTools.Process("[INFO] 2026 [1] user=[abc] ok", new FormatOptions { Lenient = true });
            o.Append("log tags not json: " + (lg.Count == 0) + "\r\n");

            // 展开嵌套
            var er = JsonTools.Process("{\"data\":\"{\\\"a\\\":1,\\\"b\\\":\\\"[1,2]\\\"}\",\"s\":\"plain\"}", new FormatOptions { Expand = true });
            o.Append("expand: n=" + er.Expanded + "\r\n" + er.Pieces[0].Text + "\r\n");

            var v = new JsonViewer(input);
            v.Show();
            Application.DoEvents();

            // 搜索：普通 / 正则 / 大小写
            v.box.Select(0, 0);
            v.search.Text = "subscribe"; v.UpdateMatches(true);
            o.Append("search: " + v.searchCount.Text + " highlights=" + v.box.Highlights.Count + " sel=[" + v.box.SelectedText + "]\r\n");
            v.Find(true); o.Append("next: " + v.searchCount.Text + "\r\n");
            v.btnRegex.Checked = true; v.search.Text = "\"pc_\\w+\""; v.UpdateMatches(true);
            o.Append("regex: " + v.searchCount.Text + " sel=[" + v.box.SelectedText + "]\r\n");
            v.search.Text = "([bad"; v.UpdateMatches(true); o.Append("bad regex: " + v.searchCount.Text + "\r\n");
            v.btnRegex.Checked = false; v.btnCase.Checked = true; v.search.Text = "OFFICIAL"; v.UpdateMatches(true);
            o.Append("case: " + v.searchCount.Text + "\r\n");
            v.search.Text = "official"; v.UpdateMatches(true); o.Append("case lower: " + v.searchCount.Text + "\r\n");
            v.btnCase.Checked = false; v.search.Text = ""; v.UpdateMatches(false);

            // 打码
            string before = v.box.Text;
            v.btnMask.Checked = true; Application.DoEvents();
            o.Append("mask: on=" + v.masked + " readonly=" + v.box.ReadOnly + " tokenHidden=" + !v.box.Text.Contains("eyJ0eXAi") + " pwdHidden=" + !v.box.Text.Contains("5f4dcc3b") + " uidKept=" + v.box.Text.Contains("10001") + "\r\n");
            v.btnMask.Checked = false; Application.DoEvents();
            o.Append("unmask restores: " + (v.box.Text == before) + "\r\n");

            // 树形 + 路径
            v.btnTree.Checked = true; Application.DoEvents();
            o.Append("tree roots: " + v.tree.Nodes.Count + " first=" + (v.tree.Nodes.Count > 0 ? v.tree.Nodes[0].Text.Trim() : "") + "\r\n");
            if (v.tree.Nodes.Count > 0)
            {
                var r0 = v.tree.Nodes[0];
                o.Append("root children=" + r0.Nodes.Count + " visibleCount=" + v.tree.VisibleCount + " itemH=" + v.tree.ItemHeight + "\r\n");
                foreach (TreeNode c in r0.Nodes) o.Append("  " + c.Text.Trim() + " [" + c.Nodes.Count + (c.IsExpanded ? " exp" : "") + "]\r\n");
            }
            int ti = v.box.Text.IndexOf("subscribe/demo_app/OFFICIAL");
            v.box.Select(ti + 3, 0); v.UpdatePath();
            o.Append("path: " + v.pathLabel.Text + "\r\n");
            int tk = v.box.Text.IndexOf("eyJ0eXAi");
            o.Append("path token: " + v.PathAt(tk) + "\r\n");

            // JWT / 时间戳
            int s, e; string val;
            string kind = v.TokenAt(tk + 5, out s, out e, out val);
            var jwt = Jwt.Decode(val);
            o.Append("token kind=" + kind + " jwt=" + (jwt != null) + "\r\n" + (jwt != null ? jwt.Summary() : "") + "\r\n");
            int tt = v.box.Text.IndexOf("1792057728");
            kind = v.TokenAt(tt + 2, out s, out e, out val);
            o.Append("time: " + kind + " " + TimeHint.Describe(val).Replace("\n", " | ") + "\r\n");
            var jv = new JsonViewer(jwt.Report());
            o.Append("jwt report parsed: " + jv.status.Text + "\r\n");
            jv.Dispose();

            // 撤销格式化
            string fmt = v.box.Text;
            v.Reprocess(true, false);
            bool compressed = v.box.Text != fmt;
            v.Undo();
            o.Append("compress then undo: changed=" + compressed + " restored=" + (v.box.Text == fmt) + "\r\n");

            // 主题 / 字体
            ViewSettings.Dark = true; ViewSettings.FontSize = 14; ApplyAll(); Application.DoEvents();
            o.Append("restyle keeps text: " + (v.box.Text == fmt) + " dark=" + (v.box.BackColor == Theme.Night.Back) + "\r\n");
            v.Close();
            return o.ToString();
        }

        /// <summary>诊断截图：opts 用逗号分隔，如 "tree,mask,q=token,regex"</summary>
        public static void Demo(string text, string opts)
        {
            var v = new JsonViewer(text);
            v.Shown += delegate
            {
                foreach (var o in (opts ?? "").Split(','))
                {
                    if (o == "tree") v.btnTree.Checked = true;
                    else if (o == "mask") v.btnMask.Checked = true;
                    else if (o == "regex") v.btnRegex.Checked = true;
                    else if (o == "only") v.btnOnly.Checked = true;
                    else if (o.StartsWith("q=")) { v.box.Select(0, 0); v.search.Text = o.Substring(2); v.UpdateMatches(true); }
                    else if (o == "#menu") { v.btnSettings.ShowDropDown(); }
                    else if (o == "#mid") { int c = v.box.TextLength / 2; v.box.Select(c, 0); v.box.ScrollToCaret(); v.ColorVisible(); }
                }
            };
            Application.Run(v);
        }
    }
}
