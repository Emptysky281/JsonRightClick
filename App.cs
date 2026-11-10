using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace JsonRightClick
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // 自检：JsonRightClick.exe --selftest 输入文件 输出文件
            if (args.Length == 3 && args[0] == "--selftest")
            {
                var o = new StringBuilder();
                var r = JsonTools.Process(System.IO.File.ReadAllText(args[1]), false);
                o.Append("COUNT=" + r.Count + " ERROR=" + r.Error + " WARN=" + r.Warning + "\r\n");
                string full = JsonTools.BuildView(r.Pieces, false).Text;
                o.Append("== FULL ==\r\n" + full + "\r\n");
                string only = JsonTools.BuildView(r.Pieces, true).Text;
                o.Append("== ONLY ==\r\n" + only + "\r\n");
                string edited = only.Replace("\"uid\": 10001", "\"uid\": 999").Replace("\r\n", "\n");
                string merged = JsonTools.MergeOnlyJson(r.Pieces, edited);
                o.Append("== MERGED -> MINIFY ==\r\n" + (merged == null ? "NULL" : JsonTools.BuildView(JsonTools.Process(merged, true).Pieces, false).Text) + "\r\n");
                string again = JsonTools.BuildView(JsonTools.Process(full, false).Pieces, false).Text;
                o.Append("== REFORMAT STABLE == " + (again == full) + "\r\n");
                o.Append("== BROKEN HEADER MERGE == " + (JsonTools.MergeOnlyJson(r.Pieces, edited.Replace("片段 2 / 2", "x")) == null) + "\r\n");
                var vw = JsonTools.BuildView(r.Pieces, false); string rtf = Rtf.Build(vw.Text, vw.JsonRanges, Theme.Light, "Consolas", 10.5f);
                o.Append("== RTF ==\r\n" + rtf.Substring(0, Math.Min(300, rtf.Length)));
                System.IO.File.WriteAllText(args[2], o.ToString());
                return 0;
            }

            // 诊断：--probe 窗口句柄 输出文件   /   --floatdemo
            if (args.Length == 3 && args[0] == "--probe")
            {
                System.IO.File.WriteAllText(args[2], SelectionProbe.Check(new IntPtr(long.Parse(args[1]))).ToString() + " " + SelectionProbe.LastInfo);
                return 0;
            }
            if (args.Length == 3 && args[0] == "--copytest")
            {
                System.IO.File.WriteAllText(args[2], JsonViewer.SelfTestCopy(System.IO.File.ReadAllText(args[1])));
                return 0;
            }
            try { Native.SetProcessDPIAware(); } catch { }
            FontStore.LoadAll();
            if (args.Length == 3 && args[0] == "--uitest")
            {
                Application.EnableVisualStyles();
                ViewSettings.NoPersist = true;
                System.IO.File.WriteAllText(args[2], JsonViewer.SelfTestUi(System.IO.File.ReadAllText(args[1])));
                return 0;
            }
            if (args.Length == 4 && args[0] == "--leak")
            {
                Application.EnableVisualStyles();
                ViewSettings.NoPersist = true;
                System.IO.File.WriteAllText(args[2], Perf.Leak(System.IO.File.ReadAllText(args[1]), int.Parse(args[3])));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--round")
            {
                ViewSettings.NoPersist = true;
                string lang = Settings.GetString("Language", "");
                try { System.IO.File.WriteAllText(args[1], Perf.Round("")); }
                finally { Settings.SetString("Language", lang); }
                return 0;
            }
            if (args.Length == 3 && args[0] == "--term")
            {
                System.IO.File.WriteAllText(args[2], Perf.TermCheck(args[1]));
                return 0;
            }
            if (args.Length == 3 && args[0] == "--perf")
            {
                System.IO.File.WriteAllText(args[2], Perf.Run(System.IO.File.ReadAllText(args[1])));
                return 0;
            }
            if (args.Length == 3 && args[0] == "--importtest")
            {
                string n = FontStore.Import(args[1]);
                System.IO.File.WriteAllText(args[2], "name=" + n + " exists=" + FontStore.Exists(n) + " width=" + Native.FontCharWidthTwips(n, 12) + " imported=" + string.Join(",", new List<string>(FontStore.Imported).ToArray()));
                return 0;
            }
            // 诊断截图：--demo dark|light 字体 字号 文件 [选项]（不写入设置）
            if (args.Length >= 5 && args[0] == "--demo")
            {
                ViewSettings.NoPersist = true;
                ViewSettings.Dark = args[1] == "dark"; ViewSettings.FontName = args[2];
                ViewSettings.FontSize = float.Parse(args[3], CultureInfo.InvariantCulture);
                Application.EnableVisualStyles();
                JsonViewer.Demo(System.IO.File.ReadAllText(args[4]), args.Length > 5 ? args[5] : "");
                return 0;
            }
            if (args.Length == 1 && args[0] == "--floatdemo")
            {
                var fb = new FloatButton();
                fb.ShowAt(new Point(400, 400));
                var t = new System.Windows.Forms.Timer { Interval = 3000 };
                t.Tick += delegate { Application.Exit(); };
                t.Start();
                Application.Run();
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            CrashLog.Install();

            // 诊断：--autostart on|off 输出文件（和设置菜单里的开关走同一段代码）
            if (args.Length == 3 && args[0] == "--autostart")
            {
                Settings.AutoStart = args[1] == "on";
                System.IO.File.WriteAllText(args[2], "autostart=" + Settings.AutoStart + " ini=" + Settings.Get("AutoStart", false));
                return 0;
            }
            // 诊断：--setfont 字号 输出文件（模拟在窗口里改字号，再读回来）
            if (args.Length == 3 && args[0] == "--setfont")
            {
                ViewSettings.FontSize = float.Parse(args[1], CultureInfo.InvariantCulture);
                System.IO.File.WriteAllText(args[2], "pkg=" + Native.HasPackageIdentity + " readback=" + ViewSettings.FontSize);
                return 0;
            }

            // 提权子进程：只打开开发人员模式后退出
            if (args.Length == 1 && args[0] == "--devmode-on") return DevMode.EnableHere();

            // 被资源管理器（Win11 第一层右键菜单）以 COM 方式拉起
            if (args.Length >= 1 && (args[0].Equals("-Embedding", StringComparison.OrdinalIgnoreCase) || args[0].Equals("/Embedding", StringComparison.OrdinalIgnoreCase)))
            {
                Application.Run(new EmbeddingContext());
                return 0;
            }

            // 诊断：--shell off|classic|modern 输出文件（不经过托盘菜单切换右键菜单模式）
            if (args.Length == 3 && args[0] == "--shell")
            {
                string msg;
                bool ok = TrayContext.SwitchShell(args[1], false, out msg); // 诊断进程不重启托盘
                System.IO.File.WriteAllText(args[2], (ok ? "OK " : "FAIL ") + msg + " mode=" + ShellMenu.Mode + " devmode=" + DevMode.IsOn + " installed=" + SparsePackage.Installed);
                return 0;
            }
            // 诊断：--comtest 输出文件（像资源管理器那样创建菜单对象并读取标题）
            if (args.Length == 2 && args[0] == "--comtest")
            {
                string r;
                try
                {
                    var t = Type.GetTypeFromCLSID(ExplorerMenu.Clsid);
                    var cmd = (IExplorerCommand)Activator.CreateInstance(t);
                    IntPtr p; int hr = cmd.GetTitle(IntPtr.Zero, out p);
                    r = "hr=" + hr + " title=" + Marshal.PtrToStringUni(p);
                    IntPtr ic; cmd.GetIcon(IntPtr.Zero, out ic); r += " icon=" + Marshal.PtrToStringUni(ic);
                }
                catch (Exception ex) { r = "ERR " + ex.GetType().Name + ": " + ex.Message; }
                System.IO.File.WriteAllText(args[1], r);
                return 0;
            }

            // --clip：只打开查看窗口显示剪贴板内容
            if (args.Length == 1 && args[0] == "--clip")
            {
                Application.Run(new JsonViewer(ClipboardUtil.GetText() ?? ""));
                return 0;
            }

            // 文件路径（命令行 / 拖到 exe 上）：托盘在运行就交给它，否则独立打开
            if (args.Length > 0 && !args[0].StartsWith("--"))
            {
                if (IpcWindow.Send(args)) return 0;
                JsonViewer lastViewer = null;
                foreach (var f in args) { var v = FileLoader.Open(f, null); if (v != null) lastViewer = v; }
                if (lastViewer != null) Native.ForceForeground(lastViewer.Handle);
                bool any = false;
                foreach (Form f in Application.OpenForms) if (f is JsonViewer) any = true;
                if (any) Application.Run(new ViewersContext());
                return 0;
            }

            // 菜单程序（JsonRightClickMenu.exe）只给资源管理器用；被直接双击时转去启动主程序
            if (string.Equals(Path.GetFileName(Application.ExecutablePath), SparsePackage.MenuExe, StringComparison.OrdinalIgnoreCase))
            {
                try { if (File.Exists(SparsePackage.MainExePath)) Process.Start(SparsePackage.MainExePath); } catch { }
                return 0;
            }

            if (!TakeMutex())
            {
                MessageBox.Show(L.T("JSON 解析助手已在运行（见右下角托盘图标）。", "JSON Viewer is already running (see the tray icon)."), L.T("JSON 解析助手", "JSON Viewer"));
                return 0;
            }
            Application.Run(new TrayContext());
            ReleaseMutex();
            return 0;
        }

        static Mutex mutex;

        public static bool TakeMutex()
        {
            bool created;
            mutex = new Mutex(true, "JsonRightClick_SingleInstance", out created);
            if (!created) { mutex.Dispose(); mutex = null; }
            return created;
        }

        public static void ReleaseMutex()
        {
            if (mutex == null) return;
            try { mutex.ReleaseMutex(); } catch { }
            mutex.Dispose();
            mutex = null;
        }
    }

    /// <summary>
    /// 设置保存在程序目录的 settings.ini（UTF-8，key=value）。
    /// 不用注册表：右键菜单程序带应用包身份，在包里读注册表只能看到旧的私有副本，设置会“丢”；
    /// 程序目录里的文件在包内外是同一个。首次运行会从旧版本的注册表设置里导入。
    /// 程序目录不可写时（如放在 Program Files）退回到注册表。
    /// </summary>
    static class Settings
    {
        const string Key = @"Software\JsonRightClick";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "JsonRightClick";
        static readonly object gate = new object();
        static Dictionary<string, string> cache;
        static DateTime cacheTime;
        static int useFile = -1;

        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini"); } }

        static bool UseFile
        {
            get
            {
                if (useFile < 0)
                {
                    try
                    {
                        if (!File.Exists(FilePath)) { File.WriteAllText(FilePath, ""); MigrateFromRegistry(); }
                        using (File.Open(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
                        useFile = 1;
                    }
                    catch { useFile = 0; }
                }
                return useFile == 1;
            }
        }

        /// <summary>导入旧版本保存在注册表里的设置（只在包外做：包里读到的是旧副本）</summary>
        static void MigrateFromRegistry()
        {
            if (Native.HasPackageIdentity) return;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                    if (k != null) foreach (var n in k.GetValueNames()) { var v = k.GetValue(n); if (v != null && n.Length > 0) d[n] = v.ToString(); }
            }
            catch { }
            if (d.Count > 0) Save(d);
        }

        static Dictionary<string, string> Load()
        {
            var t = File.GetLastWriteTimeUtc(FilePath);
            if (cache != null && t == cacheTime) return cache;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                        d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Replace("\\n", "\n").Replace("\\\\", "\\");
                    }
                    break;
                }
                catch (IOException) { Thread.Sleep(20); }
            }
            cache = d; cacheTime = t;
            return d;
        }

        static void Save(Dictionary<string, string> d)
        {
            var sb = new StringBuilder("; JSON 解析助手设置（可删除此文件恢复默认）\r\n");
            var keys = new List<string>(d.Keys); keys.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var k in keys) sb.Append(k).Append('=').Append((d[k] ?? "").Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n")).Append("\r\n");
            string tmp = FilePath + "." + Process.GetCurrentProcess().Id + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            for (int i = 0; i < 10; i++)
            {
                try { File.Copy(tmp, FilePath, true); break; }
                catch (IOException) { Thread.Sleep(20); }
            }
            try { File.Delete(tmp); } catch { }
        }

        public static string GetString(string name, string def)
        {
            if (!UseFile)
                using (var k = Registry.CurrentUser.OpenSubKey(Key)) { object v = k == null ? null : k.GetValue(name); return v == null ? def : v.ToString(); }
            lock (gate)
            {
                string v;
                return Load().TryGetValue(name, out v) ? v : def;
            }
        }

        public static void SetString(string name, string val)
        {
            if (!UseFile) { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, val ?? "", RegistryValueKind.String); return; }
            using (var m = new Mutex(false, "JsonRightClick_Settings"))
            {
                bool owned = false;
                try { owned = m.WaitOne(2000); } catch (AbandonedMutexException) { owned = true; }
                try
                {
                    lock (gate)
                    {
                        cache = null; // 重新读一次，合并其他进程刚写的内容
                        var d = new Dictionary<string, string>(Load(), StringComparer.OrdinalIgnoreCase);
                        string old;
                        if (d.TryGetValue(name, out old) && old == (val ?? "")) return;
                        d[name] = val ?? "";
                        Save(d);
                        cache = null;
                    }
                }
                finally { if (owned) m.ReleaseMutex(); }
            }
        }

        public static bool Get(string name, bool def)
        {
            string v = GetString(name, null);
            if (v == null) return def;
            int n;
            if (int.TryParse(v, out n)) return n != 0;
            bool b;
            return bool.TryParse(v, out b) ? b : def;
        }

        public static void Set(string name, bool val) { SetString(name, val ? "1" : "0"); }

        /// <summary>开机自启（写注册表 Run 项，系统读；状态同时记在设置里，包内也能正确显示）</summary>
        public static bool AutoStart
        {
            get
            {
                if (Native.HasPackageIdentity) return Get("AutoStart", false);
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(AppName) != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(AppName, "\"" + SparsePackage.MainExePath + "\"");
                    else k.DeleteValue(AppName, false);
                }
                Set("AutoStart", value);
            }
        }

        /// <summary>托盘启动时同步一次开机自启状态（用户可能在任务管理器里改过）</summary>
        public static void SyncAutoStart()
        {
            if (Native.HasPackageIdentity) return;
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) Set("AutoStart", k != null && k.GetValue(AppName) != null); } catch { }
        }
    }

    static class AppIcon
    {
        static Icon icon;
        public static Icon Get()
        {
            if (icon != null) return icon;
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var path = Rounded(new Rectangle(1, 1, 29, 29), 7))
                using (var b = new SolidBrush(Color.FromArgb(37, 99, 235))) g.FillPath(b, path);
                using (var f = new Font("Consolas", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("{}", f, Brushes.White, new RectangleF(0, 1, 32, 31), sf);
                icon = Icon.FromHandle(bmp.GetHicon());
            }
            return icon;
        }

        public static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ===================== 托盘主程序 =====================
    sealed class TrayContext : ApplicationContext
    {
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly FloatButton floatBtn = new FloatButton();
        readonly InputHooks hooks = new InputHooks();
        HotkeyWindow hotkeyWnd = new HotkeyWindow();
        IpcWindow ipc;
        readonly System.Windows.Forms.Timer watchTimer = new System.Windows.Forms.Timer { Interval = 150 };
        readonly uint myPid = (uint)Process.GetCurrentProcess().Id;

        IntPtr targetRoot;
        DateTime shownAt;
        bool busy, floatEnabled, restoreClip, hotkeyOk;
        int gen;           // 每次右键/点击递增，用于作废过期的异步检测
        SelectionTarget rightClickTarget;
        uint rightClickClipboardSequence, targetClipboardSequence;

        public TrayContext()
        {
            floatEnabled = Settings.Get("FloatButton", true);
            restoreClip = Settings.Get("RestoreClipboard", true);

            menu.Opening += delegate { BuildMenu(); };
            BuildMenu();
            tray = new NotifyIcon { Icon = AppIcon.Get(), ContextMenuStrip = menu, Visible = true };
            UpdateTip();
            tray.DoubleClick += delegate { ShowViewer(SafeGetText()); };

            floatBtn.Clicked += delegate { Trigger(); };
            watchTimer.Tick += delegate { Watch(); };

            // 钩子运行在独立线程：本程序界面忙（加载大文本、等剪贴板）时也不会拖慢全局鼠标键盘
            hooks.Ui = floatBtn;
            hooks.OnMouse = OnMouse;
            // 按键只使旧检测/按钮失效，不再把 Ctrl+A、Shift 或游戏按键当作文本选区证据。
            hooks.OnKey = delegate { gen++; rightClickTarget = null; HideFloat(); };
            hooks.OnButtonClick = Trigger;
            hooks.Start();

            ipc = new IpcWindow(floatBtn);
            ipc.FilesReceived += OpenFiles;

            hotkeyWnd.Pressed += delegate { if (!busy) CaptureAndShow(false); };
            hotkeyOk = ApplyHotkey(Hotkey.Current);

            // 资源管理器右键菜单：按当前模式修正注册。Win11 第一层菜单由资源管理器拉起的 -Embedding 进程响应，
            // 再通过进程间消息交给托盘打开（托盘始终在包外运行，见 Main）
            Settings.SyncAutoStart();
            ShellMenu.Sync();
            if (ShellMenu.Mode == "modern") CheckModernOnStartup();
            ShowPendingShellResult();

            string hk = Hotkey.Display(Hotkey.Current);
            tray.ShowBalloonTip(3000, L.T("JSON 解析助手已启动", "JSON Viewer is running"),
                L.F("选中 JSON 文本后右键，点光标旁的「解析 JSON」按钮；或按 {0}。", hk)
                + (hotkeyOk || Hotkey.Current == "" ? "" : "\n" + L.F("（{0} 已被占用，可在托盘菜单里修改快捷键）", hk)),
                ToolTipIcon.Info);
        }

        void UpdateTip()
        {
            string t = L.T("JSON 解析助手", "JSON Viewer") + " " + Application.ProductVersion + (Hotkey.Current == "" ? "" : " (" + Hotkey.Display(Hotkey.Current) + ")");
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        // ---------- 托盘菜单（每次打开时重建，语言 / 勾选状态都是最新的） ----------
        void BuildMenu()
        {
            L.Refresh();
            menu.Items.Clear();
            menu.Items.Add(L.T("解析剪贴板中的 JSON", "Parse JSON from clipboard"), null, delegate { ShowViewer(SafeGetText()); });
            menu.Items.Add(L.T("打开空白窗口（粘贴 JSON）", "Open blank window (paste JSON)"), null, delegate { ShowViewer(""); });
            menu.Items.Add(L.T("打开文件…", "Open file…"), null, delegate { PickFile(); });
            menu.Items.Add(new ToolStripSeparator());

            var miFloat = new ToolStripMenuItem(L.T("选中文字右键时显示「解析 JSON」按钮", "Show \"Parse JSON\" button on right-click")) { Checked = floatEnabled };
            miFloat.Click += delegate { floatEnabled = !floatEnabled; Settings.Set("FloatButton", floatEnabled); if (!floatEnabled) HideFloat(); };
            var miRestore = new ToolStripMenuItem(L.T("取词后恢复原剪贴板内容", "Restore clipboard after capturing")) { Checked = restoreClip };
            miRestore.Click += delegate { restoreClip = !restoreClip; Settings.Set("RestoreClipboard", restoreClip); };
            var miHotkey = new ToolStripMenuItem(L.F("快捷键：{0}…", Hotkey.Display(Hotkey.Current)) + (hotkeyOk || Hotkey.Current == "" ? "" : L.T("（被占用）", " (in use)")));
            miHotkey.Click += delegate { EditHotkey(); };
            var miAuto = new ToolStripMenuItem(L.T("开机自动启动", "Start with Windows")) { Checked = Settings.AutoStart };
            miAuto.Click += delegate
            {
                try { Settings.AutoStart = !Settings.AutoStart; }
                catch (Exception ex) { MessageBox.Show(L.T("设置失败：", "Failed: ") + ex.Message); }
            };
            menu.Items.Add(miFloat);
            menu.Items.Add(miRestore);
            menu.Items.Add(miHotkey);
            menu.Items.Add(miAuto);
            var miShell = new ToolStripMenuItem(L.T("资源管理器右键菜单", "File Explorer context menu"));
            string cur = ShellMenu.Mode;
            Action<string, string> addMode = (id, text) =>
            {
                var it = new ToolStripMenuItem(text) { Checked = cur == id };
                it.Click += delegate { if (ShellMenu.Mode != id) ChangeShellMode(id); };
                miShell.DropDownItems.Add(it);
            };
            if (ShellMenu.IsWin11)
            {
                addMode("modern", L.T("Win11 第一层菜单（需要开发人员模式）", "Windows 11 top-level menu (needs Developer Mode)"));
                addMode("classic", L.T("经典菜单（在「显示更多选项」里）", "Classic menu (under \"Show more options\")"));
            }
            else addMode("classic", L.T("显示「用 JSON 解析助手打开」", "Show \"Open with JSON Viewer\""));
            addMode("off", L.T("关闭", "Off"));
            menu.Items.Add(miShell);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.En ? "中文界面" : "English", null, delegate
            {
                L.En = !L.En;
                ShellMenu.Sync();
                UpdateTip();
                JsonViewer.ReopenAll();
            });
            if (!Elevation.IsSelfElevated)
                menu.Items.Add(L.T("以管理员身份重新启动", "Restart as administrator"), null, delegate { RestartElevated(); });
            menu.Items.Add(L.T("使用说明", "Help"), null, delegate { ShowHelp(); });
            menu.Items.Add(L.T("退出", "Exit"), null, delegate { ExitThread(); });
            menu.Renderer = new ThemeRenderer(Theme.Current);
        }

        bool ApplyHotkey(string s)
        {
            hotkeyWnd.Unregister();
            uint mods; Keys key;
            if (!Hotkey.Parse(s, out mods, out key)) return s == "";
            Hotkey.CurrentKey = key;
            return hotkeyWnd.Register(1, mods | Native.MOD_NOREPEAT, (uint)key);
        }

        void EditHotkey()
        {
            using (var d = new HotkeyDialog(Hotkey.Current))
            {
                if (d.ShowDialog() != DialogResult.OK || d.Result == Hotkey.Current && hotkeyOk) return;
                string old = Hotkey.Current;
                if (ApplyHotkey(d.Result))
                {
                    Hotkey.Current = d.Result; hotkeyOk = true; UpdateTip();
                    if (d.Result != "") tray.ShowBalloonTip(2000, L.T("快捷键已更新", "Hotkey updated"), Hotkey.Display(d.Result), ToolTipIcon.Info);
                }
                else
                {
                    MessageBox.Show(L.F("{0} 已被其他程序占用，请换一个组合。", Hotkey.Display(d.Result)), L.T("设置快捷键", "Set hotkey"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    hotkeyOk = ApplyHotkey(old);
                }
            }
        }

        void PickFile()
        {
            using (var dlg = new OpenFileDialog { Filter = L.T("日志 / JSON / 文本", "Log / JSON / Text") + "|*.log;*.json;*.jsonl;*.ndjson;*.txt;*.har;*.out|" + L.T("所有文件", "All files") + "|*.*", Multiselect = true })
                if (dlg.ShowDialog() == DialogResult.OK) OpenFiles(dlg.FileNames);
        }

        // ---------- 资源管理器右键菜单模式 ----------
        void ChangeShellMode(string mode)
        {
            if (mode == "modern" && !DevMode.IsOn)
            {
                var ans = MessageBox.Show(
                    L.T("Win11 第一层右键菜单需要打开 Windows 的「开发人员模式」。\n\n点「确定」后会弹出管理员确认窗口，同意后自动打开并注册菜单。\n开发人员模式允许安装未签名的应用，以后可以在「设置 → 系统 → 开发者选项」里关闭（关闭后第一层菜单会消失）。",
                        "The Windows 11 top-level menu requires Windows Developer Mode.\n\nAfter clicking OK an administrator prompt will appear; approve it to turn Developer Mode on and register the menu.\nDeveloper Mode allows installing unsigned apps; you can turn it off later in Settings → System → For developers (the top-level menu will then disappear)."),
                    L.T("开启 Win11 第一层菜单", "Enable Windows 11 top-level menu"), MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (ans != DialogResult.OK) return;
            }
            string msg;
            bool ok = SwitchShell(mode, true, out msg);
            if (ok && RestartPending) { ExitThread(); return; } // 后台脚本会在完成后重新启动托盘并显示结果
            tray.ShowBalloonTip(4000, ok ? L.T("右键菜单已更新", "Context menu updated") : L.T("右键菜单设置失败", "Context menu setup failed"), msg, ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        /// <summary>切换后本进程需要退出（包操作会结束本进程，由脚本完成后重新启动托盘）</summary>
        public static bool RestartPending;

        /// <summary>切换右键菜单模式。fromTray=true 时如果操作会结束本进程，就交给后台脚本并重新启动托盘</summary>
        public static bool SwitchShell(string mode, bool fromTray, out string msg)
        {
            msg = "";
            string err;
            bool inPackage = Native.HasPackageIdentity;
            if (mode == "modern")
            {
                if (!ShellMenu.IsWin11) { msg = L.T("只有 Windows 11 才有第一层菜单", "The top-level menu only exists on Windows 11"); return false; }
                if (!DevMode.IsOn && !DevMode.EnableElevated()) { msg = L.T("没有打开开发人员模式（管理员确认被取消或失败）", "Developer Mode was not enabled (the administrator prompt was cancelled or failed)"); return false; }
                ShellMenu.SetClassic(false);
                bool relaunch = inPackage && fromTray;
                if (!SparsePackage.Run(true, relaunch, out err))
                {
                    ShellMenu.SetClassic(ShellMenu.Mode == "classic");
                    msg = L.T("注册失败：", "Registration failed: ") + err;
                    return false;
                }
                ShellMenu.Mode = "modern";
                RestartPending = relaunch;
                msg = L.T("已加到 Win11 右键菜单第一层。右键 .log / .json 等文件即可看到「用 JSON 解析助手打开」。", "Added to the Windows 11 top-level context menu. Right-click a .log / .json file to see \"Open with JSON Viewer\".");
                return true;
            }
            string old = ShellMenu.Mode;
            ShellMenu.Mode = mode;
            ShellMenu.SetClassic(mode == "classic");
            if (old == "modern" || SparsePackage.Installed)
            {
                bool relaunch = inPackage && fromTray;
                if (!SparsePackage.Run(false, relaunch, out err)) { msg = L.T("移除第一层菜单失败：", "Failed to remove the top-level menu: ") + err; return false; }
                RestartPending = relaunch;
            }
            msg = mode == "classic" ? L.T("已改为经典右键菜单", "Switched to the classic context menu") : L.T("已关闭资源管理器右键菜单", "File Explorer context menu turned off");
            return true;
        }

        /// <summary>第一层菜单模式：程序挪了位置 / 包被删掉时自动重新注册</summary>
        void CheckModernOnStartup()
        {
            if (Native.HasPackageIdentity) // 本进程在包里，说明包就注册在当前目录
            {
                if (SparsePackage.Moved) Settings.SetString("ShellPackagePath", AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
                return;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                if (!SparsePackage.Moved && SparsePackage.Installed) return;
                string err = null;
                bool ok = DevMode.IsOn && SparsePackage.Run(true, false, out err);
                if (ok) return;
                floatBtn.BeginInvoke(new Action(() =>
                    tray.ShowBalloonTip(5000, L.T("Win11 第一层右键菜单不可用", "Windows 11 top-level menu unavailable"),
                        (DevMode.IsOn ? L.T("注册失败：", "Registration failed: ") + err : L.T("开发人员模式已关闭。", "Developer Mode is off. ")) +
                        L.T("可在托盘菜单 →「资源管理器右键菜单」里重新设置。", " Change it in tray menu → \"File Explorer context menu\"."), ToolTipIcon.Warning)));
            });
        }

        /// <summary>上一次由后台脚本完成的切换结果（托盘重启后显示）</summary>
        void ShowPendingShellResult()
        {
            string r = Settings.GetString("ShellResult", "");
            if (r == "") return;
            Settings.SetString("ShellResult", "");
            bool ok = r == "OK";
            string mode = ShellMenu.Mode;
            tray.ShowBalloonTip(4000, ok ? L.T("右键菜单已更新", "Context menu updated") : L.T("右键菜单设置失败", "Context menu setup failed"),
                ok ? (mode == "modern" ? L.T("已加到 Win11 右键菜单第一层。", "Added to the Windows 11 top-level context menu.")
                     : mode == "classic" ? L.T("已改为经典右键菜单", "Switched to the classic context menu") : L.T("已关闭资源管理器右键菜单", "File Explorer context menu turned off"))
                   : r, ok ? ToolTipIcon.Info : ToolTipIcon.Warning);
        }

        void OpenFiles(string[] files)
        {
            JsonViewer last = null;
            foreach (var f in files) { var v = FileLoader.Open(f, null); if (v != null) last = v; }
            if (last != null) { last.Activate(); Native.ForceForeground(last.Handle); }
        }

        void RestartElevated()
        {
            hotkeyWnd.Dispose(); ipc.Dispose(); hooks.Stop();
            Program.ReleaseMutex();
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath) { Verb = "runas", UseShellExecute = true });
                ExitThread();
            }
            catch (Exception) // 用户在 UAC 中点了“否”
            {
                Program.TakeMutex();
                hooks.Start();
                ipc = new IpcWindow(floatBtn); ipc.FilesReceived += OpenFiles;
                hotkeyWnd = new HotkeyWindow();
                hotkeyWnd.Pressed += delegate { if (!busy) CaptureAndShow(false); };
                hotkeyOk = ApplyHotkey(Hotkey.Current);
            }
        }

        // ---------- 鼠标事件（由钩子线程转发到 UI 线程） ----------
        void OnMouse(int msg, Point pt, IntPtr root, IntPtr control, uint clipboardSequence)
        {
            switch (msg)
            {
                case Native.WM_RBUTTONDOWN:
                    gen++;
                    HideFloat();
                    rightClickTarget = null;
                    uint pid;
                    Native.GetWindowThreadProcessId(root, out pid);
                    if (floatEnabled && !busy && IsEligible(root, pid))
                    {
                        rightClickTarget = SelectionTarget.Capture(root, control, pt);
                        hooks.WatchingSelection = rightClickTarget != null;
                        rightClickClipboardSequence = clipboardSequence;
                        // 菜单出现前读取真实选区，避免菜单遮挡和虚拟文本焦点切换造成漏识别。
                        if (rightClickTarget != null) rightClickTarget.BeforeMenu = SelectionProbe.Begin(rightClickTarget);
                    }
                    break;
                case Native.WM_RBUTTONUP:
                    var target = rightClickTarget;
                    rightClickTarget = null;
                    if (target != null && target.IsCurrent && floatEnabled && !busy)
                        ProbeAndShow(target, rightClickClipboardSequence, pt);
                    else hooks.WatchingSelection = false;
                    break;
                case Native.WM_LBUTTONDOWN:
                case Native.WM_MOUSEWHEEL:
                    gen++;
                    rightClickTarget = null;
                    HideFloat();
                    break;
            }
        }

        // ---------- 右键后：只有提供程序确认非空文本选区才显示按钮 ----------
        async void ProbeAndShow(SelectionTarget target, uint clipboardSequence, Point pt)
        {
            int g = ++gen;
            try
            {
                await Task.Delay(80);
                if (g != gen || busy || !floatEnabled || !target.IsCurrent) return;
                var probe = target.BeforeMenu ?? SelectionProbe.Begin(target);
                await Task.WhenAny(probe, Task.Delay(550));
                if (g != gen || busy || !floatEnabled || !target.IsCurrent) return;
                if (probe.Status == TaskStatus.RanToCompletion && (SelectionProbe.RecheckAfterMenu(target) || !target.Fresh || probe.Result != Sel.Yes))
                {
                    // 原生输入框须复核右键是否取消选区；冷启动提供程序也可从原控件再确认。
                    probe = SelectionProbe.Begin(target);
                    await Task.WhenAny(probe, Task.Delay(300));
                }
                if (g != gen || busy || !floatEnabled || !target.IsCurrent) return;
                Sel selection = probe.Status == TaskStatus.RanToCompletion ? probe.Result : Sel.Unknown;
                if (!SelectionProbe.ShouldShow(selection)) return; // 未知、超时、不支持均不弹出。

                targetRoot = target.Root;
                targetClipboardSequence = clipboardSequence;
                floatBtn.ShowAt(pt);
                hooks.ButtonRect = floatBtn.ScreenBounds;
                shownAt = DateTime.Now;
                watchTimer.Start();
            }
            finally { if (g == gen) hooks.WatchingSelection = false; }
        }

        bool IsEligible(IntPtr root, uint pid)
        {
            if (root == IntPtr.Zero || pid == myPid) return false;
            switch (Native.ClassName(root))
            {
                case "Shell_TrayWnd": case "Shell_SecondaryTrayWnd": case "Progman": case "WorkerW":
                case "NotifyIconOverflowWindow": case "TopLevelWindowForOverflowXamlIsland":
                case "CabinetWClass": case "ExploreWClass": case "#32768":
                    return false;
            }
            return true;
        }

        void Watch()
        {
            if (!floatBtn.IsShown) { watchTimer.Stop(); return; }
            if ((DateTime.Now - shownAt).TotalSeconds > 8) { HideFloat(); return; }
            if (!SelectionTarget.ForegroundBelongsTo(targetRoot)) { HideFloat(); return; }
            Native.TopMost(floatBtn.Handle); // 只在原目标窗口仍在前台时置顶。
        }

        void HideFloat()
        {
            hooks.WatchingSelection = false;
            hooks.ButtonRect = Rectangle.Empty;
            watchTimer.Stop();
            floatBtn.HideButton();
        }

        void Trigger()
        {
            if (busy) return;
            HideFloat();
            CaptureAndShow(true);
        }

        // ---------- 取选中文本 ----------
        async void CaptureAndShow(bool fromButton)
        {
            busy = true;
            try
            {
                IntPtr fg = fromButton && targetRoot != IntPtr.Zero ? targetRoot : Native.Root(Native.GetForegroundWindow());
                uint fgPid;
                Native.GetWindowThreadProcessId(fg, out fgPid);
                if (fgPid == myPid) return; // 在本程序窗口里按快捷键，不处理

                // 终端：从不模拟按键（Ctrl+C 会中断正在运行的程序），只用终端自己复制的内容
                if (Terminal.Is(fg))
                {
                    if (fromButton) Native.Key(Keys.Escape, fg); // 只关闭终端可能弹出的菜单（发给菜单，不进 shell）
                    if (!fromButton || Native.GetClipboardSequenceNumber() != targetClipboardSequence)
                    {
                        string t = SafeGetText();
                        if (t.Length > 0) { ShowViewer(t); return; }
                    }
                    tray.ShowBalloonTip(3500, L.T("终端里请先复制", "Copy first in terminals"),
                        L.T("为避免 Ctrl+C 中断正在运行的程序，终端里不会自动复制。请先选中并复制（右键或 Ctrl+Shift+C），再点按钮或按快捷键。",
                            "To avoid Ctrl+C interrupting the running program, text is never auto-copied in terminals. Select and copy it first (right-click or Ctrl+Shift+C), then click the button or press the hotkey."),
                        ToolTipIcon.Info);
                    return;
                }

                // 以管理员身份运行的程序：Windows 不允许普通程序向它发送按键
                if (!Elevation.IsSelfElevated && Elevation.IsElevated(fgPid))
                {
                    tray.ShowBalloonTip(4000, L.T("目标程序以管理员身份运行", "Target app runs as administrator"),
                        L.T("Windows 不允许普通程序读取它的选中内容。请手动复制后双击托盘图标，或在托盘菜单里「以管理员身份重新启动」。",
                            "Windows blocks reading its selection from a normal app. Copy manually and double-click the tray icon, or use \"Restart as administrator\" in the tray menu."),
                        ToolTipIcon.Warning);
                    return;
                }

                if (fromButton)
                {
                    Native.Key(Keys.Escape); // 关闭目标程序的右键菜单
                    await Task.Delay(120);
                    if (targetRoot != IntPtr.Zero && Native.GetForegroundWindow() != targetRoot)
                    {
                        Native.SetForegroundWindow(targetRoot);
                        await Task.Delay(60);
                    }
                }

                // 等待用户松开快捷键，否则 Ctrl+C 会变成 Ctrl+Alt+C。
                for (int i = 0; i < 100 && Native.ModifiersDown(); i++) await Task.Delay(20);
                if (Native.ModifiersDown() || Native.Root(Native.GetForegroundWindow()) != fg)
                {
                    tray.ShowBalloonTip(2500, L.T("没有取到选中的文本", "No selected text"),
                        L.T("目标窗口已失去焦点或快捷键尚未松开，请回到原窗口重试；也可手动复制后解析剪贴板。",
                            "The target lost focus or shortcut keys are still held. Return to it and retry, or copy manually and parse the clipboard."), ToolTipIcon.Warning);
                    return;
                }

                var backup = restoreClip ? ClipSnapshot.Capture(ipc.Handle) : null;
                uint seq = Native.GetClipboardSequenceNumber();
                Native.KeyCombo(Keys.ControlKey, Keys.C);
                var copied = await SelectionCapture.ReadCopiedTextAsync(seq);
                string text = copied.Text;

                if (string.IsNullOrEmpty(text))
                {
                    tray.ShowBalloonTip(2500, L.T("没有取到选中的文本", "No selected text"),
                        L.T("请先选中 JSON 文本再试。也可以手动复制后双击托盘图标解析。", "Select some JSON text first, or copy it manually and double-click the tray icon."), ToolTipIcon.Warning);
                    return;
                }

                // 在创建窗口之前恢复原剪贴板；若用户/别的程序又复制了内容，不覆盖新内容。
                if (backup != null && !backup.IsEmpty) backup.RestoreIfUnchanged(ipc.Handle, copied.Sequence);
                ShowViewer(text);
            }
            finally { busy = false; }
        }

        static string SafeGetText()
        {
            return ClipboardUtil.GetText() ?? "";
        }

        static void ShowViewer(string text)
        {
            var v = new JsonViewer(text);
            v.Show();
            v.Activate();
            Native.SetForegroundWindow(v.Handle);
        }

        static void ShowHelp()
        {
            string hk = Hotkey.Display(Hotkey.Current);
            MessageBox.Show(
                L.T("用法一：在记事本 / VS Code / Notepad++ 等任意编辑器里选中一段 JSON（或带 JSON 的日志），点鼠标右键，光标旁会出现「{ } 解析 JSON」按钮，点它即可。没有选中文字时不会出现按钮。",
                    "1) Select JSON (or a log containing JSON) in any editor such as Notepad, VS Code or Notepad++, right-click, then click the \"{ } Parse JSON\" button next to the cursor. The button only appears when text is selected.") + "\n\n" +
                L.F("用法二：选中后按 {0}（可在托盘菜单里修改）。", hk) + "\n\n" +
                L.T("用法三：复制后双击托盘图标。", "3) Copy, then double-click the tray icon.") + "\n\n" +
                L.T("用法四：在资源管理器里右键 .log / .json / .txt 等文件 →「用 JSON 解析助手打开」；也可以用托盘菜单 →「打开文件…」。",
                    "4) In File Explorer, right-click a .log / .json / .txt file → \"Open with JSON Viewer\", or use tray menu → \"Open file…\".") + "\n\n" +
                L.T("说明：", "Notes:") + "\n" +
                L.T("• 悬浮按钮仅在确认非空文本选区时显示；无法确认的游戏、画布或文本提供程序不会弹出。请手动复制后从托盘解析剪贴板。",
                    "• The floating button requires a confirmed nonempty text selection. Games, canvases and unsupported text providers stay hidden; copy manually and parse from the tray instead.") + "\n" +
                L.T("• 终端（cmd、PowerShell、Windows Terminal、PuTTY 等）里不会自动复制，避免 Ctrl+C 中断程序；请先复制再解析。",
                    "• In terminals (cmd, PowerShell, Windows Terminal, PuTTY…) nothing is auto-copied, so Ctrl+C never interrupts a program; copy first, then parse.") + "\n" +
                L.T("• 取词后会恢复原来的剪贴板内容（包括图片、文件等），且不会出现在 Win+V 历史里。",
                    "• The previous clipboard content (including images and files) is restored after capturing and kept out of Win+V history.") + "\n" +
                L.T("• 以管理员身份运行的程序，需要本软件也以管理员身份运行才能取词。",
                    "• For apps running as administrator, this app must also run as administrator to capture text.") + "\n" +
                L.T("• Windows 不允许第三方程序修改其它软件自带的右键菜单，所以用悬浮按钮代替。",
                    "• Windows doesn't let apps modify other apps' context menus, so a floating button is used instead."),
                L.T("JSON 解析助手 - 使用说明", "JSON Viewer - Help"), MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override void ExitThreadCore()
        {
            hooks.Stop();
            hotkeyWnd.Dispose();
            ipc.Dispose();
            tray.Visible = false;
            tray.Dispose();
            floatBtn.Dispose();
            base.ExitThreadCore();
            Application.Exit();
        }
    }

    // ===================== 选中文字检测 =====================
    // ===================== 悬浮按钮（逐像素透明，不抢焦点，按显示器 DPI 缩放） =====================
    /// <summary>
    /// 窗口在“每显示器 DPI 感知 v2”模式下创建，坐标与钩子给出的物理像素一致，
    /// 多显示器、不同缩放比例时位置和大小都正确。不使用 WinForms 的 Show / Bounds，直接用 Win32 定位。
    /// </summary>
    sealed class FloatButton : Form
    {
        public event EventHandler Clicked;
        bool hover, shown, tracking;
        Rectangle bounds;
        int dpi = 96;

        public bool IsShown { get { return shown; } }
        public Rectangle ScreenBounds { get { return bounds; } }

        public FloatButton()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Cursor = Cursors.Hand;
            IntPtr old = Native.EnterPerMonitorDpi();
            try { CreateHandle(); }
            finally { Native.LeaveDpi(old); }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST | Native.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_MOUSEACTIVATE: m.Result = (IntPtr)Native.MA_NOACTIVATE; return;
                case 0x02E0: return; // WM_DPICHANGED：自己按显示器 DPI 绘制，不让 WinForms 缩放
                case 0x0200: // WM_MOUSEMOVE
                    if (!hover) { hover = true; Redraw(); }
                    if (!tracking) { tracking = true; Native.TrackLeave(Handle); }
                    break;
                case 0x02A3: // WM_MOUSELEAVE
                    tracking = false;
                    if (hover) { hover = false; Redraw(); }
                    break;
                case 0x0202: // WM_LBUTTONUP（正常情况下点击已被鼠标钩子处理，这里只是兜底）
                    if (Clicked != null) Clicked(this, EventArgs.Empty);
                    return;
            }
            base.WndProc(ref m);
        }

        public void ShowAt(Point cursor)
        {
            Rectangle work;
            dpi = Native.DpiAt(cursor, out work);
            float scale = dpi / 96f;
            int w = (int)(120 * scale), h = (int)(32 * scale);
            // 放在光标左上方，避开通常向右下方展开的右键菜单
            int x = cursor.X - w - (int)(6 * scale), y = cursor.Y - h - (int)(6 * scale);
            if (x < work.Left) x = cursor.X + (int)(6 * scale);
            if (y < work.Top) y = cursor.Y + (int)(6 * scale);
            if (x + w > work.Right) x = work.Right - w;
            if (y + h > work.Bottom) y = work.Bottom - h;
            hover = false;
            bounds = new Rectangle(x, y, w, h);
            Redraw();
            Native.TopMost(Handle);
            shown = true;
        }

        public void HideButton()
        {
            if (!shown) return;
            shown = false;
            Native.ShowWindow(Handle, 0 /* SW_HIDE */);
        }

        // 用 32 位 ARGB 位图 + UpdateLayeredWindow 绘制，圆角边缘平滑透明，没有色键残留的杂边
        void Redraw()
        {
            if (bounds.Width <= 0) return;
            float scale = dpi / 96f;
            using (var bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                using (var font = new Font("Microsoft YaHei UI", 12.5f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    var r = new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1);
                    // 深色按钮 + 浅色细边框，在深色 / 浅色界面上都看得清
                    using (var path = AppIcon.Rounded(r, bounds.Height / 4))
                    using (var b = new SolidBrush(hover ? Color.FromArgb(58, 58, 62) : Color.FromArgb(32, 32, 35)))
                    using (var pen = new Pen(Color.FromArgb(hover ? 130 : 90, 255, 255, 255), Math.Max(1f, scale)))
                    {
                        g.FillPath(b, path);
                        g.DrawPath(pen, path);
                    }
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(L.T("{ }  解析 JSON", "{ }  Parse JSON"), font, Brushes.White, new RectangleF(0, 0, bounds.Width, bounds.Height), sf);
                }
                Native.SetLayeredBitmap(Handle, bmp, bounds.Location);
            }
        }
    }

    // ===================== Win32 =====================
    /// <summary>
    /// 全局鼠标/键盘钩子，运行在独立线程上。
    /// 只在钩子线程里做“是否吞掉点击”这种必须同步的判断，其他事件异步转发到 UI 线程。
    /// </summary>
    sealed class InputHooks
    {
        public Control Ui;
        public Action<int, Point, IntPtr, IntPtr, uint> OnMouse;
        public Action OnKey;
        public Action OnButtonClick;

        readonly object gate = new object();
        Rectangle buttonRect;
        Native.HookProc mouseProc, keyProc; // 保持引用，防止被 GC 回收
        IntPtr mouseHook, keyHook;
        bool swallowUp;
        uint threadId;

        bool watchingSelection;
        public bool WatchingSelection
        {
            get { lock (gate) return watchingSelection; }
            set { lock (gate) watchingSelection = value; }
        }
        public Rectangle ButtonRect
        {
            get { lock (gate) return buttonRect; }
            set { lock (gate) buttonRect = value; }
        }

        public void Start()
        {
            var ready = new ManualResetEvent(false);
            var th = new Thread(delegate()
            {
                threadId = Native.GetCurrentThreadId();
                mouseProc = MouseCallback;
                keyProc = KeyCallback;
                IntPtr mod = Native.GetModuleHandle(null);
                mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, mod, 0);
                keyHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyProc, mod, 0);
                ready.Set();
                Native.MSG msg;
                while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
                Native.UnhookWindowsHookEx(mouseHook);
                Native.UnhookWindowsHookEx(keyHook);
            });
            th.IsBackground = true;
            th.Name = "InputHooks";
            th.Start();
            ready.WaitOne(3000);
        }

        public void Stop()
        {
            if (threadId != 0) Native.PostThreadMessage(threadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        }

        void Post(Delegate d, params object[] args)
        {
            try { if (Ui != null && Ui.IsHandleCreated) Ui.BeginInvoke(d, args); } catch { }
        }

        IntPtr MouseCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                int msg = (int)wParam;
                // 仅按钮可见/选区检测在途时才转发滚轮，以取消旧检测；其余时间不打扰滚动。
                if (msg == Native.WM_LBUTTONDOWN || msg == Native.WM_LBUTTONUP || msg == Native.WM_RBUTTONDOWN
                    || msg == Native.WM_RBUTTONUP || (msg == Native.WM_MOUSEWHEEL && (!ButtonRect.IsEmpty || WatchingSelection)))
                {
                    var info = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    if ((info.flags & 1) == 0) // 忽略程序模拟的鼠标事件
                    {
                        var pt = new Point(info.pt.X, info.pt.Y);
                        if (msg == Native.WM_LBUTTONDOWN && ButtonRect.Contains(pt))
                        {
                            swallowUp = true;
                            ButtonRect = Rectangle.Empty;
                            Post(OnButtonClick);
                            return (IntPtr)1; // 吞掉这次点击，避免右键菜单把它当成“点在菜单外”
                        }
                        if (msg == Native.WM_LBUTTONUP && swallowUp) { swallowUp = false; return (IntPtr)1; }
                        IntPtr control = msg == Native.WM_MOUSEWHEEL ? IntPtr.Zero : Native.WindowFromPoint(info.pt);
                        IntPtr root = control == IntPtr.Zero ? IntPtr.Zero : Native.Root(control);
                        // 仅保存 HWND 与版本号等廉价元数据；选中文字及所有可访问性查询仍在后台。
                        uint sequence = msg == Native.WM_RBUTTONDOWN ? Native.GetClipboardSequenceNumber() : 0;
                        Post(OnMouse, msg, pt, root, control, sequence);
                    }
                }
            }
            return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        IntPtr KeyCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            int msg = (int)wParam;
            // 不再追踪全局选区手势；只有检测在途/按钮可见时，按键才需要通知 UI 作废结果。
            if (code >= 0 && (msg == 0x0100 || msg == 0x0104) && (WatchingSelection || !ButtonRect.IsEmpty))
            {
                int flags = Marshal.ReadInt32(lParam, 8);
                if ((flags & 0x10) == 0) Post(OnKey); // 忽略模拟按键；不查询/记录用户输入的具体键值。
            }
            return Native.CallNextHookEx(keyHook, code, wParam, lParam);
        }
    }

    /// <summary>剪贴板读写：被其他程序（剪贴板管理器、微信、远程桌面等）短暂占用时自动重试</summary>
    static class ClipboardUtil
    {
        public static bool SetText(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < 10; i++)
            {
                try { Clipboard.SetDataObject(text, true, 5, 50); return true; }
                catch (ExternalException) { Thread.Sleep(50); }
                catch (ThreadStateException) { return false; }
            }
            return false;
        }

        // 轮询复制结果时不能在 UI 线程 Sleep；被占用/格式尚未发布由异步读取流程重试。
        public static string TryGetText()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch (ExternalException) { return null; }
        }

        /// <summary>返回剪贴板文本；没有文本返回 ""；一直被占用返回 null</summary>
        public static string GetText()
        {
            for (int i = 0; i < 10; i++)
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
                catch (ExternalException) { Thread.Sleep(50); }
            }
            return null;
        }
    }

    sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public event EventHandler Pressed;
        int id = -1;

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(int hotkeyId, uint mods, uint vk)
        {
            id = hotkeyId;
            return Native.RegisterHotKey(Handle, id, mods, vk);
        }

        public void Unregister()
        {
            if (id >= 0) Native.UnregisterHotKey(Handle, id);
            id = -1;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed(this, EventArgs.Empty);
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (id >= 0) Native.UnregisterHotKey(Handle, id);
            DestroyHandle();
        }
    }

    static class Native
    {
        public const int WH_MOUSE_LL = 14, WH_KEYBOARD_LL = 13;
        public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        public const int WM_MOUSEWHEEL = 0x020A, WM_HOTKEY = 0x0312, WM_MOUSEACTIVATE = 0x0021, MA_NOACTIVATE = 3;
        public const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000;
        public const uint GA_ROOT = 2;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
        const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        const uint KEYEVENTF_KEYUP = 2;

        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize; public uint flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public int l, t, r, b;
        }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }
        [DllImport("user32.dll")] public static extern int GetMessage(out MSG msg, IntPtr h, uint min, uint max);
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint tid, uint msg, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO info);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, ref POINT pt, ref SIZE size, IntPtr src, ref POINT srcPt, int key, ref BLENDFUNCTION blend, int flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [DllImport("user32.dll")] public static extern int GetUpdateRgn(IntPtr h, IntPtr rgn, bool erase);

        public static bool Send(IntPtr h, uint msg, out IntPtr result)
        {
            return SendMessageTimeout(h, msg, IntPtr.Zero, IntPtr.Zero, 2 /* SMTO_ABORTIFHUNG */, 200, out result) != IntPtr.Zero;
        }

        public static IntPtr Root(IntPtr h) { return GetAncestor(h, GA_ROOT); }

        public static bool IsKeyDown(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

        public static void SetLayeredBitmap(IntPtr h, Bitmap bmp, Point location)
        {
            IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen);
            IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = SelectObject(mem, hbmp);
            try
            {
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT();
                var dst = new POINT { X = location.X, Y = location.Y };
                var blend = new BLENDFUNCTION { Op = 0, Flags = 0, Alpha = 255, Format = 1 /* AC_SRC_ALPHA */ };
                UpdateLayeredWindow(h, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
            }
            finally
            {
                SelectObject(mem, old); DeleteObject(hbmp); DeleteDC(mem); ReleaseDC(IntPtr.Zero, screen);
            }
        }


        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr process, uint flags);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);

        [StructLayout(LayoutKind.Sequential)]
        public struct SCROLLINFO { public uint cbSize, fMask; public int nMin, nMax; public uint nPage; public int nPos, nTrackPos; }
        [DllImport("user32.dll")] public static extern bool GetScrollInfo(IntPtr h, int bar, ref SCROLLINFO si);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref POINT l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int AddFontResourceEx(string file, uint fl, IntPtr res);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetFontResourceInfoW(string file, ref int size, IntPtr buf, int type);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike,
            uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr dc, string s, int n, out SIZE size);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TEXTMETRICW
        {
            public int tmHeight, tmAscent, tmDescent, tmInternalLeading, tmExternalLeading, tmAveCharWidth, tmMaxCharWidth,
                tmWeight, tmOverhang, tmDigitizedAspectX, tmDigitizedAspectY;
            public char tmFirstChar, tmLastChar, tmDefaultChar, tmBreakChar;
            public byte tmItalic, tmUnderlined, tmStruckOut, tmPitchAndFamily, tmCharSet;
        }
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextMetricsW(IntPtr dc, out TEXTMETRICW tm);

        public static void SetCueBanner(IntPtr edit, string text) { SendMessage(edit, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, text); }

        public static void DarkTitleBar(IntPtr h, bool dark)
        {
            int v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(h, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref v, 4) != 0)
                DwmSetWindowAttribute(h, 19, ref v, 4); // Windows 10 早期版本
            SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | 0x4 /* NOZORDER */ | SWP_NOACTIVATE | 0x20 /* FRAMECHANGED */);
        }

        static readonly Dictionary<string, float[]> metricsCache = new Dictionary<string, float[]>();

        /// <summary>用 GDI 测量字体（支持仅在本程序内注册的导入字体）：[字符宽度(缇), 行高(像素)]</summary>
        static float[] Metrics(string face, float pt)
        {
            string key = face + "|" + pt.ToString(CultureInfo.InvariantCulture);
            float[] m;
            if (metricsCache.TryGetValue(key, out m)) return m;
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                int dpi = GetDeviceCaps(dc, 90 /* LOGPIXELSY */);
                IntPtr font = CreateFontW(-(int)Math.Round(pt * dpi / 72f), 0, 0, 0, 400, 0, 0, 0, 1 /* DEFAULT_CHARSET */, 0, 0, 5, 0, face);
                IntPtr old = SelectObject(dc, font);
                SIZE sz; TEXTMETRICW tm;
                GetTextExtentPoint32W(dc, new string('0', 100), 100, out sz);
                GetTextMetricsW(dc, out tm);
                SelectObject(dc, old); DeleteObject(font);
                m = new[] { sz.cx / 100f * 1440f / dpi, (float)(tm.tmHeight + tm.tmExternalLeading) };
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            metricsCache[key] = m;
            return m;
        }

        public static float FontCharWidthTwips(string face, float pt) { return Metrics(face, pt)[0]; }
        public static float FontLineHeight(string face, float pt) { return Metrics(face, pt)[1]; }

        /// <summary>从字体文件读取字体名称（GDI+ 无法识别的 OTF 等）</summary>
        public static string FontResourceName(string file)
        {
            int size = 0;
            if (!GetFontResourceInfoW(file, ref size, IntPtr.Zero, 2 /* QFR_LOGFONTS */) || size < 92) return null;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetFontResourceInfoW(file, ref size, buf, 2)) return null;
                return Marshal.PtrToStringUni(buf + 28); // LOGFONTW.lfFaceName
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct CHARFORMAT2W
        {
            public int cbSize; public uint dwMask, dwEffects; public int yHeight, yOffset, crTextColor;
            public byte bCharSet, bPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szFaceName;
            public short wWeight, sSpacing; public int crBackColor, lcid, dwReserved; public short sStyle, wKerning;
            public byte bUnderlineType, bAnimation, bRevAuthor, bReserved1;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref CHARFORMAT2W cf);

        /// <summary>给 RichEdit 全部文字设定字体 / 字号 / 颜色（不经过 GDI+，私有字体也可用）</summary>
        public static void SetAllCharFormat(IntPtr h, string face, float pt, Color color)
        {
            var cf = new CHARFORMAT2W
            {
                cbSize = Marshal.SizeOf(typeof(CHARFORMAT2W)),
                dwMask = 0x20000000 /* CFM_FACE */ | 0x80000000 /* CFM_SIZE */ | 0x40000000 /* CFM_COLOR */ | 0x08000000 /* CFM_CHARSET */,
                yHeight = (int)Math.Round(pt * 20), crTextColor = color.R | (color.G << 8) | (color.B << 16),
                bCharSet = 1 /* DEFAULT_CHARSET */, szFaceName = face
            };
            SendMessage(h, 0x0444 /* EM_SETCHARFORMAT */, (IntPtr)4 /* SCF_ALL */, ref cf);
        }

        // ----- 每显示器 DPI -----
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dx, out uint dy);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [StructLayout(LayoutKind.Sequential)] struct TRACKMOUSEEVENT { public int cbSize; public uint dwFlags; public IntPtr hwnd; public uint hover; }
        [DllImport("user32.dll")] static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);

        /// <summary>切换当前线程到“每显示器 DPI 感知 v2”（Win10 1703+；旧系统什么都不做），返回旧值</summary>
        public static IntPtr EnterPerMonitorDpi()
        {
            try { return SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch { return IntPtr.Zero; }
        }

        public static void LeaveDpi(IntPtr old)
        {
            if (old == IntPtr.Zero) return;
            try { SetThreadDpiAwarenessContext(old); } catch { }
        }

        /// <summary>光标所在显示器的 DPI 和工作区（物理像素）</summary>
        public static int DpiAt(Point pt, out Rectangle work)
        {
            IntPtr old = EnterPerMonitorDpi();
            try
            {
                IntPtr mon = MonitorFromPoint(new POINT { X = pt.X, Y = pt.Y }, 2 /* MONITOR_DEFAULTTONEAREST */);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
                    work = Rectangle.FromLTRB(mi.rcWork.L, mi.rcWork.T, mi.rcWork.R, mi.rcWork.B);
                else work = Screen.FromPoint(pt).WorkingArea;
                uint dx, dy;
                try { if (mon != IntPtr.Zero && GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0) return (int)dx; } catch { }
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)g.DpiX;
            }
            finally { LeaveDpi(old); }
        }

        public static void TrackLeave(IntPtr h)
        {
            var t = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf(typeof(TRACKMOUSEEVENT)), dwFlags = 2 /* TME_LEAVE */, hwnd = h };
            TrackMouseEvent(ref t);
        }

        // ----- 进程 / 权限 -----
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("advapi32.dll")] public static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr tok);
        [DllImport("advapi32.dll")] public static extern bool GetTokenInformation(IntPtr tok, int cls, ref int info, int len, out int ret);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint pid);

        // ----- 进程间通信 -----
        [StructLayout(LayoutKind.Sequential)] public struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, ref COPYDATASTRUCT l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern bool ChangeWindowMessageFilterEx(IntPtr h, uint msg, uint action, IntPtr info);
        [DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, uint flags, IntPtr a, IntPtr b);

        public static bool SendCopyData(IntPtr h, ref COPYDATASTRUCT cds, out IntPtr result)
        {
            return SendMessageTimeout(h, 0x004A, IntPtr.Zero, ref cds, 2 /* SMTO_ABORTIFHUNG */, 5000, out result) != IntPtr.Zero;
        }

        // ----- 剪贴板 -----
        [DllImport("user32.dll")] public static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] public static extern bool CloseClipboard();
        [DllImport("user32.dll")] public static extern bool EmptyClipboard();
        [DllImport("user32.dll")] public static extern uint EnumClipboardFormats(uint f);
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardData(uint f);
        [DllImport("user32.dll")] public static extern IntPtr SetClipboardData(uint f, IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalFree(IntPtr h);
        [DllImport("kernel32.dll")] public static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] public static extern UIntPtr GlobalSize(IntPtr h);

        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

        /// <summary>把按键直接发给指定窗口（只用于关闭终端的右键菜单，不经过前台输入）</summary>
        public static void Key(Keys k, IntPtr hwnd)
        {
            IntPtr menu = FindWindow("#32768", null);
            if (menu != IntPtr.Zero) PostMessage(menu, 0x0100, (IntPtr)(int)k, IntPtr.Zero);
        }

        // ----- 脱离包启动子进程 -----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb; public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)] struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
        [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attr, IntPtr value, IntPtr size, IntPtr prev, IntPtr retSize);
        [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);

        /// <summary>
        /// 用 PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY = 脱离包（BREAKAWAY_ENABLE_PROCESS_TREE）启动程序，
        /// 子进程不带本进程的包身份，读写的是真实注册表。
        /// </summary>
        public static bool StartBreakaway(string exe, string args, string workDir, out string error)
        {
            error = null;
            IntPtr size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            IntPtr list = Marshal.AllocHGlobal(size), policy = Marshal.AllocHGlobal(4);
            try
            {
                if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) { error = "init " + Marshal.GetLastWin32Error(); return false; }
                Marshal.WriteInt32(policy, 0x04 /* PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE */);
                if (!UpdateProcThreadAttribute(list, 0, (IntPtr)0x00020012 /* PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY */, policy, (IntPtr)4, IntPtr.Zero, IntPtr.Zero))
                { error = "attr " + Marshal.GetLastWin32Error(); return false; }
                var si = new STARTUPINFOEX { lpAttributeList = list };
                si.StartupInfo.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
                PROCESS_INFORMATION pi;
                var cmd = new StringBuilder("\"" + exe + "\" " + (args ?? "").Trim());
                if (!CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x00080000 /* EXTENDED_STARTUPINFO_PRESENT */, IntPtr.Zero, workDir, ref si, out pi))
                { error = "create " + Marshal.GetLastWin32Error(); return false; }
                CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
                return true;
            }
            finally { DeleteProcThreadAttributeList(list); Marshal.FreeHGlobal(list); Marshal.FreeHGlobal(policy); }
        }

        // ----- 应用包身份 -----
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref int len, StringBuilder name);

        /// <summary>本进程是否带有应用包身份（稀疏包注册后，从 exe 启动的进程都会带上）</summary>
        public static bool HasPackageIdentity
        {
            get
            {
                try { int len = 0; return GetCurrentPackageFullName(ref len, null) != 15700 /* APPMODEL_ERROR_NO_PACKAGE */; }
                catch (EntryPointNotFoundException) { return false; }
            }
        }

        // ----- COM 安全 -----
        [DllImport("ole32.dll")] static extern int CoInitializeSecurity(IntPtr sd, int cAuth, IntPtr auth, IntPtr res1, uint authnLevel, uint impLevel, IntPtr authList, uint caps, IntPtr res3);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint rev, out IntPtr sd, out uint size);
        static IntPtr comSd;
        public static int ComSecurityHr = -1;

        /// <summary>
        /// 进程级 COM 访问权限：允许本机任何用户和应用容器（ALL APPLICATION PACKAGES）调用，
        /// 否则 Win11 新右键菜单向已运行的托盘要菜单对象时会被拒绝（E_ACCESSDENIED）。只接受本机调用。
        /// </summary>
        public static void InitComSecurity()
        {
            try
            {
                // 0xB = COM_RIGHTS_EXECUTE | EXECUTE_LOCAL | ACTIVATE_LOCAL（只允许本机）
                // WD = Everyone，AC = ALL APPLICATION PACKAGES，S-1-15-2-2 = ALL RESTRICTED APPLICATION PACKAGES；S:(ML;;NX;;;LW) 允许低完整性调用方
                uint size;
                if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("O:BAG:BAD:(A;;0xB;;;WD)(A;;0xB;;;AC)(A;;0xB;;;S-1-15-2-2)S:(ML;;NX;;;LW)", 1, out comSd, out size)) { ComSecurityHr = Marshal.GetLastWin32Error(); return; }
                ComSecurityHr = CoInitializeSecurity(comSd, -1, IntPtr.Zero, IntPtr.Zero, 1 /* RPC_C_AUTHN_LEVEL_NONE */, 2 /* RPC_C_IMP_LEVEL_IDENTIFY */, IntPtr.Zero, 0, IntPtr.Zero);
            }
            catch { }
        }

        // ----- COM 类对象 -----
        [DllImport("ole32.dll")] public static extern int CoRegisterClassObject(ref Guid clsid, [MarshalAs(UnmanagedType.IUnknown)] object unk, uint ctx, uint flags, out uint cookie);
        [DllImport("ole32.dll")] public static extern int CoRevokeClassObject(uint cookie);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);

        /// <summary>资源管理器调用后把窗口带到前台（借用前台线程的输入状态）</summary>
        public static void ForceForeground(IntPtr h)
        {
            IntPtr fg = GetForegroundWindow();
            uint pid;
            uint fgThread = GetWindowThreadProcessId(fg, out pid), me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            try { SetForegroundWindow(h); ShowWindow(h, 9 /* SW_RESTORE */); }
            finally { if (attached) AttachThreadInput(me, fgThread, false); }
        }

        public static string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static void TopMost(IntPtr h)
        {
            SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public static void Key(Keys k)
        {
            keybd_event((byte)k, 0, 0, UIntPtr.Zero);
            keybd_event((byte)k, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void KeyCombo(Keys mod, Keys k)
        {
            keybd_event((byte)mod, 0, 0, UIntPtr.Zero);
            keybd_event((byte)k, 0, 0, UIntPtr.Zero);
            keybd_event((byte)k, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)mod, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static bool ModifiersDown()
        {
            int[] keys = { 0x10, 0x11, 0x12, 0x5B, 0x5C, (int)Hotkey.CurrentKey };
            foreach (int k in keys) if ((GetAsyncKeyState(k) & 0x8000) != 0) return true;
            return false;
        }
    }
}
