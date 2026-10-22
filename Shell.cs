using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace JsonRightClick
{
    // ===================== 中英文 =====================
    /// <summary>
    /// 界面文字。代码里写中文，英文在 Lang.cs 的字典里（或用两参数写法直接给出）。
    /// 语言设置保存在注册表 Language（zh / en），默认跟随系统界面语言。
    /// </summary>
    static partial class L
    {
        static int cache = -1; // -1 未读取，0 中文，1 英文
        static Dictionary<string, string> D;
        static partial void Fill(Dictionary<string, string> d);

        public static bool En
        {
            get
            {
                if (cache < 0)
                {
                    string v = Settings.GetString("Language", "");
                    if (v == "") v = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
                    cache = v == "en" ? 1 : 0;
                }
                return cache == 1;
            }
            set { Settings.SetString("Language", value ? "en" : "zh"); cache = value ? 1 : 0; }
        }

        public static void Refresh() { cache = -1; }

        static Dictionary<string, string> Dict
        {
            get
            {
                if (D == null) { var d = new Dictionary<string, string>(StringComparer.Ordinal); Fill(d); D = d; }
                return D;
            }
        }

        public static string T(string zh)
        {
            if (!En || zh == null) return zh;
            string en;
            return Dict.TryGetValue(zh, out en) ? en : zh;
        }

        public static string T(string zh, string en) { return En ? en : zh; }

        public static string F(string zh, params object[] args) { return string.Format(CultureInfo.InvariantCulture, T(zh), args); }

        /// <summary>字典里缺少英文的中文文字（诊断用）</summary>
        public static int DictCount { get { return Dict.Count; } }
    }

    // ===================== 打开文件 =====================
    static class FileLoader
    {
        public const long AskAbove = 20L * 1024 * 1024; // 超过 20 MB 先询问

        /// <summary>读取文件文本（自动识别 UTF-8 / UTF-16 / GBK；允许其它程序正在写入）</summary>
        public static string Read(string path, long maxBytes, out bool truncated, out string encodingName)
        {
            var fi = new FileInfo(path);
            long len = fi.Length;
            truncated = maxBytes > 0 && len > maxBytes;
            int n = (int)Math.Min(len, truncated ? maxBytes : len);
            var buf = new byte[n];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                int off = 0, r;
                while (off < n && (r = fs.Read(buf, off, n - off)) > 0) off += r;
                if (off < n) Array.Resize(ref buf, off);
            }
            return Decode(buf, truncated, out encodingName);
        }

        public static string Decode(byte[] b, bool truncated, out string name)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { name = "UTF-8"; return Encoding.UTF8.GetString(b, 3, b.Length - 3); }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { name = "UTF-16 LE"; return Encoding.Unicode.GetString(b, 2, (b.Length - 2) & ~1); }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { name = "UTF-16 BE"; return Encoding.BigEndianUnicode.GetString(b, 2, (b.Length - 2) & ~1); }
            // 无 BOM 的 UTF-16：奇数或偶数位置上大量 0 字节
            int z0 = 0, z1 = 0, probe = Math.Min(b.Length, 4000) & ~1;
            for (int i = 0; i < probe; i += 2) { if (b[i] == 0) z0++; if (b[i + 1] == 0) z1++; }
            int half = probe / 2;
            if (half >= 8 && z1 * 10 >= half && z0 * 50 < half) { name = "UTF-16 LE"; return Encoding.Unicode.GetString(b, 0, b.Length & ~1); }
            if (half >= 8 && z0 * 10 >= half && z1 * 50 < half) { name = "UTF-16 BE"; return Encoding.BigEndianUnicode.GetString(b, 0, b.Length & ~1); }

            int len = b.Length;
            if (truncated) // 截断时去掉末尾不完整的 UTF-8 字符
            {
                int k = len - 1, back = 0;
                while (k >= 0 && back < 4 && (b[k] & 0xC0) == 0x80) { k--; back++; }
                if (k >= 0 && b[k] >= 0xC0) len = k;
            }
            try { name = "UTF-8"; return new UTF8Encoding(false, true).GetString(b, 0, len); }
            catch (DecoderFallbackException) { }
            try { name = "GBK"; return Encoding.GetEncoding(936).GetString(b, 0, b.Length); }
            catch (Exception) { name = Encoding.Default.WebName; return Encoding.Default.GetString(b); }
        }

        /// <summary>打开文件并显示查看窗口；失败或取消返回 null</summary>
        public static JsonViewer Open(string path, IWin32Window owner)
        {
            try
            {
                if (!File.Exists(path)) { MessageBox.Show(owner, L.F("找不到文件：{0}", path), "JSON Viewer"); return null; }
                long len = new FileInfo(path).Length;
                long max = 0;
                if (len > AskAbove)
                {
                    var ans = MessageBox.Show(owner,
                        L.F("文件较大（{0} MB）。全部加载需要较长时间和较多内存。\n\n是：只加载前 20 MB（推荐）\n否：全部加载\n取消：不打开", (len / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)),
                        Path.GetFileName(path), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (ans == DialogResult.Cancel) return null;
                    if (ans == DialogResult.Yes) max = AskAbove;
                }
                bool truncated; string enc;
                string text = Read(path, max, out truncated, out enc);
                var v = new JsonViewer(text, path, truncated, enc);
                v.Show();
                return v;
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, L.F("无法打开文件：{0}\n{1}", path, ex.Message), "JSON Viewer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }
    }

    // ===================== 资源管理器右键菜单 =====================
    /// <summary>
    /// 三种模式：off 关闭；classic 注册表菜单（Win10 直接显示，Win11 在「显示更多选项」里）；
    /// modern Win11 第一层菜单（IExplorerCommand + 稀疏包，需要开发人员模式）。
    /// </summary>
    static class ShellMenu
    {
        public static readonly string[] Exts = { ".log", ".txt", ".json", ".jsonl", ".ndjson", ".har", ".out", ".trace", ".conf" };
        const string Verb = "JsonRightClick";

        static string KeyFor(string ext) { return @"Software\Classes\SystemFileAssociations\" + ext + @"\shell\" + Verb; }
        public static string MenuText { get { return L.T("用 JSON 解析助手打开", "Open with JSON Viewer"); } }
        static string TitleFile { get { return Path.Combine(SparsePackage.Dir, "menu-title.txt"); } }

        /// <summary>主程序写下当前语言的菜单文字，给包里的菜单程序读</summary>
        public static void WriteTitle()
        {
            try { Directory.CreateDirectory(SparsePackage.Dir); File.WriteAllText(TitleFile, MenuText, new UTF8Encoding(false)); } catch { }
        }

        public static string TitleForMenu()
        {
            try { string t = File.ReadAllText(TitleFile, Encoding.UTF8).Trim(); if (t.Length > 0) return t; } catch { }
            return MenuText;
        }

        public static bool IsWin11
        {
            get
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        int b; return k != null && int.TryParse(k.GetValue("CurrentBuildNumber") as string, out b) && b >= 22000;
                    }
                }
                catch { return false; }
            }
        }

        /// <summary>off / classic / modern</summary>
        public static string Mode
        {
            get
            {
                string m = Settings.GetString("ShellMode", "");
                if (m == "off" || m == "classic" || m == "modern") return m;
                return IsWin11 && DevMode.IsOn ? "modern" : "classic"; // 首次运行：默认添加右键菜单
            }
            set { Settings.SetString("ShellMode", value); }
        }

        /// <summary>注册 / 删除注册表菜单（经典菜单）</summary>
        public static void SetClassic(bool on)
        {
            foreach (var ext in Exts)
            {
                try
                {
                    if (!on) { Registry.CurrentUser.DeleteSubKeyTree(KeyFor(ext), false); continue; }
                    using (var k = Registry.CurrentUser.CreateSubKey(KeyFor(ext)))
                    {
                        k.SetValue("", MenuText);
                        k.SetValue("Icon", "\"" + Application.ExecutablePath + "\",0");
                        k.SetValue("MultiSelectModel", "Player");
                        using (var c = k.CreateSubKey("command")) c.SetValue("", "\"" + Application.ExecutablePath + "\" \"%1\"");
                    }
                }
                catch { }
            }
            Native.SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
        }

        static bool ClassicUpToDate()
        {
            try
            {
                using (var c = Registry.CurrentUser.OpenSubKey(KeyFor(".log") + @"\command"))
                using (var k = Registry.CurrentUser.OpenSubKey(KeyFor(".log")))
                {
                    string cmd = c == null ? null : c.GetValue("") as string;
                    string text = k == null ? null : k.GetValue("") as string;
                    return cmd != null && cmd.IndexOf(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) >= 0 && text == MenuText;
                }
            }
            catch { return false; }
        }

        /// <summary>托盘启动 / 切换语言时调用：按当前模式修正注册（程序挪了位置会自动更新）</summary>
        public static void Sync()
        {
            Settings.SetString("ShellMenu", ""); // 旧版本的开关，不再使用
            WriteTitle();
            string m = Mode;
            if (m == "classic") { if (!ClassicUpToDate()) SetClassic(true); }
            else SetClassic(false);
        }
    }

    // ===================== Win11 第一层右键菜单：IExplorerCommand =====================
    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(IntPtr psiItemArray, out IntPtr ppszName);
        [PreserveSig] int GetIcon(IntPtr psiItemArray, out IntPtr ppszIcon);
        [PreserveSig] int GetToolTip(IntPtr psiItemArray, out IntPtr ppszInfotip);
        [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
        [PreserveSig] int GetState(IntPtr psiItemArray, int fOkToBeSlow, out uint pCmdState);
        [PreserveSig] int Invoke(IntPtr psiItemArray, IntPtr pbc);
        [PreserveSig] int GetFlags(out uint pFlags);
        [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemArray
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppvOut);
        void GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        void GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        void GetAttributes(int attribFlags, uint sfgaoMask, out uint psfgaoAttribs);
        void GetCount(out uint pdwNumItems);
        void GetItemAt(uint dwIndex, out IShellItem ppsi);
        void EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int LockServer(bool fLock);
    }

    /// <summary>资源管理器调用的菜单项：标题 / 图标 / 点击后打开选中的文件</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None), Guid(ExplorerMenu.ClsidText)]
    public sealed class ExplorerMenuCommand : IExplorerCommand
    {
        static IntPtr Str(string s) { return Marshal.StringToCoTaskMemUni(s); }

        public int GetTitle(IntPtr items, out IntPtr name) { ExplorerMenu.Touch(); name = Str(ShellMenu.TitleForMenu()); return 0; }
        public int GetIcon(IntPtr items, out IntPtr icon) { icon = Str(SparsePackage.MainExePath + ",0"); return 0; }
        public int GetToolTip(IntPtr items, out IntPtr tip) { tip = IntPtr.Zero; return unchecked((int)0x80004001); }
        public int GetCanonicalName(out Guid g) { g = ExplorerMenu.Clsid; return 0; }
        public int GetState(IntPtr items, int slow, out uint state) { ExplorerMenu.Touch(); state = 0; /* ECS_ENABLED */ return 0; }
        public int GetFlags(out uint flags) { flags = 0; return 0; }
        public int EnumSubCommands(out IntPtr e) { e = IntPtr.Zero; return unchecked((int)0x80004001); }

        public int Invoke(IntPtr items, IntPtr bindCtx)
        {
            ExplorerMenu.Touch();
            var files = new List<string>();
            try
            {
                if (items != IntPtr.Zero)
                {
                    var arr = (IShellItemArray)Marshal.GetObjectForIUnknown(items);
                    uint n; arr.GetCount(out n);
                    for (uint i = 0; i < n; i++)
                    {
                        IShellItem it; arr.GetItemAt(i, out it);
                        IntPtr p; it.GetDisplayName(0x80058000 /* SIGDN_FILESYSPATH */, out p);
                        if (p != IntPtr.Zero) { files.Add(Marshal.PtrToStringUni(p)); Marshal.FreeCoTaskMem(p); }
                    }
                }
            }
            catch (Exception ex) { CrashLog.Write(ex); }
            if (files.Count > 0) ExplorerMenu.Deliver(files.ToArray()); // 异步打开，立刻返回，不卡住资源管理器
            return 0;
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class ExplorerMenuFactory : IClassFactory
    {
        public int CreateInstance(IntPtr outer, ref Guid riid, out IntPtr ppv)
        {
            ppv = IntPtr.Zero;
            if (outer != IntPtr.Zero) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
            IntPtr unk = Marshal.GetIUnknownForObject(new ExplorerMenuCommand());
            try { return Marshal.QueryInterface(unk, ref riid, out ppv); }
            finally { Marshal.Release(unk); }
        }

        public int LockServer(bool fLock) { return 0; }
    }

    /// <summary>注册 COM 类对象（只在被资源管理器拉起的 -Embedding 进程里）</summary>
    static class ExplorerMenu
    {
        public const string ClsidText = "6C2E1F4A-9B3D-4E57-A8C1-5D7F2B0E9A31";
        public static readonly Guid Clsid = new Guid(ClsidText);
        static uint cookie;
        public static int RegisterHr;
        static Control ui;
        public static Action<string[]> Handler;
        public static DateTime LastUse = DateTime.Now;

        public static void Touch() { LastUse = DateTime.Now; }

        public static void Register(Control uiThread)
        {
            if (cookie != 0) return;
            ui = uiThread;
            var clsid = Clsid;
            try
            {
                RegisterHr = Native.CoRegisterClassObject(ref clsid, new ExplorerMenuFactory(), 4 /* CLSCTX_LOCAL_SERVER */, 1 /* REGCLS_MULTIPLEUSE */, out cookie);
            }
            catch (Exception ex) { CrashLog.Write(ex); cookie = 0; }
        }

        public static void Revoke()
        {
            if (cookie == 0) return;
            try { Native.CoRevokeClassObject(cookie); } catch { }
            cookie = 0;
        }

        public static void Deliver(string[] files)
        {
            var h = Handler;
            if (h == null || ui == null || !ui.IsHandleCreated) return;
            ui.BeginInvoke(h, new object[] { files });
        }
    }

    /// <summary>被资源管理器以 -Embedding 拉起时（托盘没运行）：提供菜单项，空闲 3 分钟且没有窗口时退出</summary>
    sealed class EmbeddingContext : ApplicationContext
    {
        readonly Control ui = new Control();
        readonly System.Windows.Forms.Timer idle = new System.Windows.Forms.Timer { Interval = 15000 };

        public EmbeddingContext()
        {
            ui.CreateControl();
            var h = ui.Handle;
            // 本进程在包里，读注册表只能看到包里的旧副本（设置会“丢”），所以不在这里开窗口：
            // 托盘在运行就交给托盘；否则在包外启动主程序打开
            ExplorerMenu.Handler = files =>
            {
                if (IpcWindow.Send(files)) return;
                var args = new StringBuilder();
                foreach (var f in files) args.Append(" \"").Append(f).Append('"');
                Native.AllowSetForegroundWindow(0xFFFFFFFF); // ASFW_ANY：让主程序能把窗口带到前台
                string err;
                // 用“脱离包”的方式启动主程序：子进程不带包身份，读写的是真实设置
                if (!Native.StartBreakaway(SparsePackage.MainExePath, args.ToString(), AppDomain.CurrentDomain.BaseDirectory, out err))
                {
                    CrashLog.Write(new Exception("breakaway start failed: " + err));
                    try { Process.Start(new ProcessStartInfo(SparsePackage.MainExePath, args.ToString().Trim()) { UseShellExecute = false }); } catch (Exception ex) { CrashLog.Write(ex); }
                }
            };
            ExplorerMenu.Register(ui);
            idle.Tick += delegate
            {
                foreach (Form f in Application.OpenForms) if (f is JsonViewer && f.Visible) { ExplorerMenu.Touch(); return; }
                if ((DateTime.Now - ExplorerMenu.LastUse).TotalMinutes > 3) { ExplorerMenu.Revoke(); ExitThread(); }
            };
            idle.Start();
        }
    }

    // ===================== 开发人员模式 / 稀疏包 =====================
    static class DevMode
    {
        const string Key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock";

        public static bool IsOn
        {
            get
            {
                try
                {
                    using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var k = b.OpenSubKey(Key))
                        return k != null && Convert.ToInt32(k.GetValue("AllowDevelopmentWithoutDevLicense", 0)) == 1;
                }
                catch { return false; }
            }
        }

        /// <summary>在管理员进程里执行：打开开发人员模式</summary>
        public static int EnableHere()
        {
            try
            {
                using (var b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = b.CreateSubKey(Key))
                    k.SetValue("AllowDevelopmentWithoutDevLicense", 1, RegistryValueKind.DWord);
                return 0;
            }
            catch (Exception ex) { CrashLog.Write(ex); return 1; }
        }

        /// <summary>弹出管理员确认（UAC），由提权的子进程打开开发人员模式</summary>
        public static bool EnableElevated()
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--devmode-on") { Verb = "runas", UseShellExecute = true }))
                    p.WaitForExit(30000);
            }
            catch (System.ComponentModel.Win32Exception) { return false; } // 用户点了“否”
            return IsOn;
        }
    }

    static class SparsePackage
    {
        public const string Name = "JsonRightClick.ExplorerMenu";
        public const string Publisher = "CN=JsonRightClick";
        public static string Dir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShellPackage"); } }
        public const string MenuExe = "JsonRightClickMenu.exe";
        public static string MainExePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "JsonRightClick.exe"); } }
        static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }

        public static bool Installed { get { string o, e; return RunPs("if (Get-AppxPackage -Name '" + Name + "') { 'yes' }", out o, out e) && o.Trim() == "yes"; } }

        /// <summary>程序挪了位置（注册时记录的目录和现在不同）</summary>
        public static bool Moved { get { return !string.Equals(Settings.GetString("ShellPackagePath", ""), ExeDir, StringComparison.OrdinalIgnoreCase); } }

        /// <summary>
        /// 注册 / 移除稀疏包。包被移除或更新时，Windows 会结束这个包里正在运行的进程（本程序注册后启动的进程都在包里），
        /// 所以操作放到一个由 WMI 创建、不属于包的 PowerShell 进程里执行。
        /// relaunch=true：本进程会被结束，脚本先等本进程退出，完成后重新启动托盘；调用方应立即退出。
        /// </summary>
        public static bool Run(bool register, bool relaunch, out string error)
        {
            error = null;
            if (register)
            {
                if (!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MenuExe))) { error = L.F("缺少 {0}（和主程序放在同一个文件夹）", MenuExe); return false; }
                try { WriteFiles(); } catch (Exception ex) { error = ex.Message; return false; }
            }
            // 用程序目录下的固定位置（WMI 创建的进程和本进程的 TEMP 目录不一定相同）
            Directory.CreateDirectory(Dir);
            string id = Guid.NewGuid().ToString("N");
            string result = Path.Combine(Dir, "result-" + id + ".txt");
            string script = Path.Combine(Dir, "op-" + id + ".ps1");
            var sb = new StringBuilder();
            sb.Append("$ErrorActionPreference = 'Stop'\r\n");
            if (relaunch) sb.Append("Wait-Process -Id " + Process.GetCurrentProcess().Id + " -Timeout 20 -ErrorAction SilentlyContinue\r\n");
            sb.Append("try {\r\n");
            sb.Append("  Get-AppxPackage -Name '" + Name + "' | Remove-AppxPackage\r\n");
            if (register)
            {
                sb.Append("  Add-AppxPackage -Register '" + Esc(Path.Combine(Dir, "AppxManifest.xml")) + "' -ExternalLocation '" + Esc(ExeDir) + "'\r\n");
            }
            sb.Append("  $r = 'OK'\r\n} catch { $r = 'ERR ' + $_.Exception.Message }\r\n");
            sb.Append("[IO.File]::WriteAllText('" + Esc(result) + "', $r, [Text.Encoding]::UTF8)\r\n");
            if (relaunch)
            {
                sb.Append("Start-Process -FilePath '" + Esc(MainExePath) + "'\r\n");
            }
            sb.Append("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -ErrorAction SilentlyContinue\r\n");
            File.WriteAllText(script, sb.ToString(), new UTF8Encoding(true));

            if (!StartOutsidePackage("powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"", out error))
            {
                try { File.Delete(script); } catch { }
                return false;
            }
            if (relaunch) { Settings.SetString("ShellPackagePath", register ? ExeDir : ""); Settings.SetString("ShellResult", "OK"); return true; }
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 120000)
            {
                if (File.Exists(result))
                {
                    Thread.Sleep(100);
                    string r = "";
                    try { r = File.ReadAllText(result).Trim(); File.Delete(result); } catch { continue; }
                    if (r == "OK") { Settings.SetString("ShellPackagePath", register ? ExeDir : ""); return true; }
                    error = r.StartsWith("ERR ") ? r.Substring(4) : r;
                    return false;
                }
                Thread.Sleep(200);
            }
            error = "timeout";
            return false;
        }

        /// <summary>通过 WMI 创建进程：父进程是 WMI 服务，不属于本程序的包，不会被包操作结束</summary>
        public static bool StartOutsidePackage(string cmd, out string error)
        {
            error = null;
            try
            {
                using (var startup = new System.Management.ManagementClass("Win32_ProcessStartup").CreateInstance())
                using (var proc = new System.Management.ManagementClass("Win32_Process"))
                {
                    startup["ShowWindow"] = (ushort)0;
                    var inParams = proc.GetMethodParameters("Create");
                    inParams["CommandLine"] = cmd;
                    inParams["ProcessStartupInformation"] = startup;
                    var outParams = proc.InvokeMethod("Create", inParams, null);
                    uint ret = Convert.ToUInt32(outParams["ReturnValue"]);
                    if (ret != 0) { error = "Win32_Process.Create = " + ret; return false; }
                    return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        static string Esc(string s) { return s.Replace("'", "''"); }

        static bool RunPs(string script, out string output, out string error)
        {
            output = ""; error = null;
            try
            {
                string full = "[Console]::OutputEncoding=[Text.Encoding]::UTF8; try { " + script + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
                var psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(full)))
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    var errTask = p.StandardError.ReadToEndAsync();
                    output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(60000);
                    string err = errTask.Result.Trim();
                    if (p.ExitCode != 0) { error = err.Length > 0 ? err : "PowerShell exit " + p.ExitCode; return false; }
                    return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        static void WriteFiles()
        {
            Directory.CreateDirectory(Path.Combine(Dir, "Assets"));
            SaveLogo(Path.Combine(Dir, "Assets", "Logo44.png"), 44);
            SaveLogo(Path.Combine(Dir, "Assets", "Logo150.png"), 150);
            SaveLogo(Path.Combine(Dir, "Assets", "StoreLogo.png"), 50);
            string exe = MenuExe; // 包里只放菜单程序，托盘 / 查看窗口不在包里
            string types = "";
            foreach (var e in ShellMenu.Exts) types += "            <desktop5:ItemType Type=\"" + e + "\"><desktop5:Verb Id=\"JsonRightClickOpen\" Clsid=\"" + ExplorerMenu.ClsidText + "\" /></desktop5:ItemType>\r\n";
            string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Package xmlns=""http://schemas.microsoft.com/appx/manifest/foundation/windows10""
         xmlns:uap=""http://schemas.microsoft.com/appx/manifest/uap/windows10""
         xmlns:uap10=""http://schemas.microsoft.com/appx/manifest/uap/windows10/10""
         xmlns:desktop4=""http://schemas.microsoft.com/appx/manifest/desktop/windows10/4""
         xmlns:desktop5=""http://schemas.microsoft.com/appx/manifest/desktop/windows10/5""
         xmlns:com=""http://schemas.microsoft.com/appx/manifest/com/windows10""
         xmlns:rescap=""http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities""
         xmlns:desktop6=""http://schemas.microsoft.com/appx/manifest/desktop/windows10/6""
         IgnorableNamespaces=""uap uap10 desktop4 desktop5 desktop6 com rescap"">
  <Identity Name=""" + Name + @""" Publisher=""" + Publisher + @""" Version=""1.0.1.0"" ProcessorArchitecture=""neutral"" />
  <Properties>
    <DisplayName>JSON Viewer</DisplayName>
    <PublisherDisplayName>JsonRightClick</PublisherDisplayName>
    <Logo>ShellPackage\Assets\StoreLogo.png</Logo>
    <uap10:AllowExternalContent>true</uap10:AllowExternalContent>
    <desktop6:RegistryWriteVirtualization>disabled</desktop6:RegistryWriteVirtualization>
    <desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>
  </Properties>
  <Resources><Resource Language=""zh-cn"" /><Resource Language=""en-us"" /></Resources>
  <Dependencies><TargetDeviceFamily Name=""Windows.Desktop"" MinVersion=""10.0.19041.0"" MaxVersionTested=""10.0.26100.0"" /></Dependencies>
  <Capabilities><rescap:Capability Name=""runFullTrust"" /><rescap:Capability Name=""unvirtualizedResources"" /></Capabilities>
  <Applications>
    <Application Id=""App"" Executable=""" + exe + @""" uap10:TrustLevel=""mediumIL"" uap10:RuntimeBehavior=""win32App"">
      <uap:VisualElements DisplayName=""JSON Viewer"" Description=""JSON Viewer"" BackgroundColor=""transparent""
                          Square150x150Logo=""ShellPackage\Assets\Logo150.png"" Square44x44Logo=""ShellPackage\Assets\Logo44.png"" AppListEntry=""none"" />
      <Extensions>
        <desktop4:Extension Category=""windows.fileExplorerContextMenus"">
          <desktop4:FileExplorerContextMenus>
" + types + @"          </desktop4:FileExplorerContextMenus>
        </desktop4:Extension>
        <com:Extension Category=""windows.comServer"">
          <com:ComServer>
            <com:ExeServer Executable=""" + exe + @""" DisplayName=""JSON Viewer Explorer Menu"">
              <com:Class Id=""" + ExplorerMenu.ClsidText + @""" DisplayName=""JSON Viewer Explorer Menu"" />
            </com:ExeServer>
          </com:ComServer>
        </com:Extension>
      </Extensions>
    </Application>
  </Applications>
</Package>
";
            File.WriteAllText(Path.Combine(Dir, "AppxManifest.xml"), xml, new UTF8Encoding(false));
        }

        static void SaveLogo(string path, int size)
        {
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                int m = Math.Max(1, size / 16);
                using (var p = AppIcon.Rounded(new Rectangle(m, m, size - 2 * m - 1, size - 2 * m - 1), size / 5))
                using (var b = new SolidBrush(Color.FromArgb(37, 99, 235))) g.FillPath(b, p);
                using (var f = new Font("Consolas", size * 0.46f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("{}", f, Brushes.White, new RectangleF(0, size * 0.03f, size, size), sf);
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }

    // ===================== 单实例转发：资源管理器 / 命令行打开的文件交给已运行的托盘程序 =====================
    sealed class IpcWindow : NativeWindow, IDisposable
    {
        public const string Title = "JsonRightClick_IPC_Window";
        const int Magic = 0x4A52;
        readonly Control ui;
        public event Action<string[]> FilesReceived;

        public IpcWindow(Control ui)
        {
            this.ui = ui;
            CreateHandle(new CreateParams { Caption = Title });
            try { Native.ChangeWindowMessageFilterEx(Handle, 0x004A /* WM_COPYDATA */, 1 /* MSGFLT_ALLOW */, IntPtr.Zero); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x004A)
            {
                try
                {
                    var cds = (Native.COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(Native.COPYDATASTRUCT));
                    if (cds.dwData == (IntPtr)Magic && cds.cbData > 0)
                    {
                        var files = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2).TrimEnd('\0').Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var h = FilesReceived;
                        if (h != null) ui.BeginInvoke(h, new object[] { files }); // 立刻返回，不让发送方等待
                        m.Result = (IntPtr)1;
                        return;
                    }
                }
                catch { }
            }
            base.WndProc(ref m);
        }

        /// <summary>把文件交给已运行的实例；没有实例或失败时返回 false</summary>
        public static bool Send(string[] files)
        {
            IntPtr h = Native.FindWindow(null, Title);
            if (h == IntPtr.Zero) return false;
            string s = string.Join("\n", files);
            IntPtr mem = Marshal.StringToHGlobalUni(s);
            try
            {
                var cds = new Native.COPYDATASTRUCT { dwData = (IntPtr)Magic, cbData = (s.Length + 1) * 2, lpData = mem };
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                Native.AllowSetForegroundWindow(pid); // 让对方能把窗口带到前台
                IntPtr res;
                return Native.SendCopyData(h, ref cds, out res) && res == (IntPtr)1;
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        public void Dispose() { DestroyHandle(); }
    }

    // ===================== 崩溃日志 =====================
    static class CrashLog
    {
        public static void Install()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception, true);
        }

        static void Report(Exception ex, bool fatal)
        {
            string file = Write(ex);
            try
            {
                MessageBox.Show(L.F("程序出错了，已记录到：\n{0}\n\n{1}", file ?? "-", ex == null ? "" : ex.Message),
                    "JSON Viewer", MessageBoxButtons.OK, fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
            }
            catch { }
        }

        public static string Write(Exception ex)
        {
            string text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  v" + Application.ProductVersion +
                "  " + Environment.OSVersion + (Environment.Is64BitProcess ? " x64" : " x86") + "\r\n" + ex + "\r\n\r\n";
            foreach (var dir in new[] { AppDomain.CurrentDomain.BaseDirectory, Path.GetTempPath() })
            {
                try
                {
                    string f = Path.Combine(dir, "JsonRightClick-error.log");
                    File.AppendAllText(f, text, Encoding.UTF8);
                    return f;
                }
                catch { }
            }
            return null;
        }
    }

    // ===================== 管理员权限 =====================
    static class Elevation
    {
        static int self = -1;

        public static bool IsSelfElevated
        {
            get
            {
                if (self < 0)
                    try { self = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator) ? 1 : 0; }
                    catch { self = 0; }
                return self == 1;
            }
        }

        /// <summary>目标进程是否以管理员身份运行（本程序不是管理员时，Windows 不允许向它发送模拟按键）</summary>
        public static bool IsElevated(uint pid)
        {
            IntPtr p = Native.OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (p == IntPtr.Zero) return false;
            try
            {
                IntPtr tok;
                if (!Native.OpenProcessToken(p, 0x0008 /* TOKEN_QUERY */, out tok)) return false;
                try
                {
                    int elevated = 0, ret;
                    return Native.GetTokenInformation(tok, 20 /* TokenElevation */, ref elevated, 4, out ret) && elevated != 0;
                }
                finally { Native.CloseHandle(tok); }
            }
            finally { Native.CloseHandle(p); }
        }
    }

    // ===================== 剪贴板完整备份 =====================
    /// <summary>
    /// 取词前备份剪贴板里的全部格式（文字、HTML、RTF、文件列表、图片 DIB 等），取完后原样恢复，
    /// 并标记“不进入剪贴板历史 / 云剪贴板”，避免 Win+V 里出现重复记录。
    /// </summary>
    sealed class ClipSnapshot
    {
        sealed class Item { public uint Format; public byte[] Data; }
        readonly List<Item> items = new List<Item>();
        public bool Complete = true; // 有格式没备份成功（太大 / 超时）

        public bool IsEmpty { get { return items.Count == 0; } }
        public int Count { get { return items.Count; } }

        static bool Open(IntPtr owner)
        {
            for (int i = 0; i < 10; i++) { if (Native.OpenClipboard(owner)) return true; Thread.Sleep(30); }
            return false;
        }

        public static ClipSnapshot Capture(IntPtr owner)
        {
            var s = new ClipSnapshot();
            if (!Open(owner)) { s.Complete = false; return s; }
            try
            {
                var sw = Stopwatch.StartNew();
                long total = 0;
                uint f = 0;
                while ((f = Native.EnumClipboardFormats(f)) != 0)
                {
                    // 句柄类格式（位图、调色板、图元文件、GDI 对象）不是全局内存，跳过；系统会用 CF_DIB 自动合成位图
                    if (f == 2 || f == 3 || f == 9 || f == 14 || f == 0x0082 || f == 0x0083 || f == 0x008E || (f >= 0x0300 && f <= 0x03FF)) continue;
                    if (sw.ElapsedMilliseconds > 800 || total > 200L * 1024 * 1024) { s.Complete = false; break; }
                    IntPtr h = Native.GetClipboardData(f);
                    if (h == IntPtr.Zero) continue;
                    long size = (long)Native.GlobalSize(h);
                    if (size <= 0 || size > 64L * 1024 * 1024) { s.Complete = false; continue; }
                    IntPtr p = Native.GlobalLock(h);
                    if (p == IntPtr.Zero) continue;
                    try
                    {
                        var data = new byte[size];
                        Marshal.Copy(p, data, 0, (int)size);
                        s.items.Add(new Item { Format = f, Data = data });
                        total += size;
                    }
                    finally { Native.GlobalUnlock(h); }
                }
            }
            finally { Native.CloseClipboard(); }
            return s;
        }

        public bool Restore(IntPtr owner)
        {
            if (items.Count == 0 || !Open(owner)) return false;
            try
            {
                Native.EmptyClipboard();
                foreach (var it in items) SetBytes(it.Format, it.Data);
                // 不进入 Win+V 剪贴板历史和云剪贴板；剪贴板管理工具（如 Ditto）也认这个标记
                SetBytes(Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing"), new byte[] { 0, 0, 0, 0 });
                SetBytes(Native.RegisterClipboardFormat("CanIncludeInClipboardHistory"), new byte[] { 0, 0, 0, 0 });
                SetBytes(Native.RegisterClipboardFormat("CanUploadToCloudClipboard"), new byte[] { 0, 0, 0, 0 });
                return true;
            }
            finally { Native.CloseClipboard(); }
        }

        static void SetBytes(uint format, byte[] data)
        {
            if (format == 0) return;
            IntPtr h = Native.GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)data.Length);
            if (h == IntPtr.Zero) return;
            IntPtr p = Native.GlobalLock(h);
            if (p == IntPtr.Zero) { Native.GlobalFree(h); return; }
            Marshal.Copy(data, 0, p, data.Length);
            Native.GlobalUnlock(h);
            if (Native.SetClipboardData(format, h) == IntPtr.Zero) Native.GlobalFree(h); // 成功后内存归系统所有
        }
    }

    // ===================== 终端识别 =====================
    /// <summary>
    /// 终端里 Ctrl+C 表示“中断正在运行的程序”，所以对终端从不模拟按键，
    /// 只使用终端自己复制到剪贴板的内容（右键复制、选中即复制等）。
    /// </summary>
    static class Terminal
    {
        static readonly HashSet<string> Procs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh", "putty", "kitty", "mintty", "MobaXterm",
            "ConEmu", "ConEmu64", "wezterm-gui", "alacritty", "Tabby", "Hyper", "SecureCRT", "Xshell", "termius", "FinalShell", "WindTerm", "mremoteng"
        };
        static readonly HashSet<string> Classes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PuTTY", "KiTTY", "mintty", "VirtualConsoleClass", "TMobaXtermForm", "org.wezfurlong.wezterm"
        };
        static readonly Dictionary<uint, string> names = new Dictionary<uint, string>();

        public static bool Is(IntPtr root)
        {
            if (root == IntPtr.Zero) return false;
            if (Classes.Contains(Native.ClassName(root))) return true;
            uint pid;
            Native.GetWindowThreadProcessId(root, out pid);
            string name;
            lock (names)
            {
                if (!names.TryGetValue(pid, out name))
                {
                    try { using (var p = Process.GetProcessById((int)pid)) name = p.ProcessName; } catch { name = ""; }
                    if (names.Count > 200) names.Clear();
                    names[pid] = name;
                }
            }
            return Procs.Contains(name);
        }
    }

    // ===================== 快捷键 =====================
    static class Hotkey
    {
        public const string Default = "Ctrl+Alt+J";
        public static Keys CurrentKey = Keys.J; // 取词时等待这个键松开

        public static string Current
        {
            get { return Settings.GetString("Hotkey", Default); }
            set { Settings.SetString("Hotkey", value ?? ""); }
        }

        /// <summary>"Ctrl+Alt+J" → 修饰键标志 + 虚拟键码；空字符串表示不使用快捷键</summary>
        public static bool Parse(string s, out uint mods, out Keys key)
        {
            mods = 0; key = Keys.None;
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var part in s.Split('+'))
            {
                string p = part.Trim();
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= 4; break;
                    case "win": mods |= 8; break;
                    default:
                        try { key = (Keys)Enum.Parse(typeof(Keys), p, true); } catch { return false; }
                        break;
                }
            }
            return key != Keys.None;
        }

        public static string Format(uint mods, Keys key)
        {
            var parts = new List<string>();
            if ((mods & Native.MOD_CONTROL) != 0) parts.Add("Ctrl");
            if ((mods & Native.MOD_ALT) != 0) parts.Add("Alt");
            if ((mods & 4) != 0) parts.Add("Shift");
            if ((mods & 8) != 0) parts.Add("Win");
            parts.Add(key.ToString());
            return string.Join("+", parts.ToArray());
        }

        public static string Display(string s)
        {
            if (string.IsNullOrEmpty(s)) return L.T("（未设置）", "(none)");
            var parts = s.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                switch (parts[i])
                {
                    case "Oemplus": parts[i] = "="; break;
                    case "OemMinus": parts[i] = "-"; break;
                    case "Oemcomma": parts[i] = ","; break;
                    case "OemPeriod": parts[i] = "."; break;
                    case "OemQuestion": parts[i] = "/"; break;
                    case "OemOpenBrackets": parts[i] = "["; break;
                    case "Oem6": parts[i] = "]"; break;
                    case "Oem1": parts[i] = ";"; break;
                    case "Oem7": parts[i] = "'"; break;
                    case "Oem5": parts[i] = "\\"; break;
                    case "Oemtilde": parts[i] = "`"; break;
                    default:
                        if (parts[i].Length == 2 && parts[i][0] == 'D' && char.IsDigit(parts[i][1])) parts[i] = parts[i].Substring(1);
                        break;
                }
            }
            return string.Join("+", parts);
        }
    }

    /// <summary>录制快捷键的对话框：直接按下想要的组合键</summary>
    sealed class HotkeyDialog : Form
    {
        readonly TextBox box = new TextBox { ReadOnly = true, TextAlign = HorizontalAlignment.Center, Font = new Font("Consolas", 12f), Dock = DockStyle.Fill };
        readonly Label hint = new Label { Dock = DockStyle.Fill, AutoSize = false };
        public string Result;

        public HotkeyDialog(string current)
        {
            Text = L.T("设置快捷键", "Set hotkey");
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen; TopMost = true; Font = new Font("Microsoft YaHei UI", 9f);
            ClientSize = new Size(460, 176);
            Result = current;
            var tip = new Label
            {
                Dock = DockStyle.Fill, AutoSize = false,
                Text = L.T("在下面的框里直接按下新的组合键（需要包含 Ctrl / Alt / Win 之一，F1~F24 可单独使用）。\n选中文字后按这个快捷键即可解析。",
                           "Press the new key combination in the box below (must include Ctrl / Alt / Win; F1-F24 can be used alone).\nSelect text, then press it to parse.")
            };
            box.Text = Hotkey.Display(current);
            box.PreviewKeyDown += (s, e) => e.IsInputKey = true;
            var ok = new Button { Text = L.T("确定", "OK"), DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = L.T("取消", "Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
            var clear = new Button { Text = L.T("不使用快捷键", "No hotkey"), AutoSize = true };
            var reset = new Button { Text = L.T("恢复默认", "Default"), AutoSize = true };
            clear.Click += delegate { Result = ""; box.Text = Hotkey.Display(""); hint.Text = ""; box.Focus(); };
            reset.Click += delegate { Result = Hotkey.Default; box.Text = Hotkey.Default; hint.Text = ""; box.Focus(); };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.AddRange(new Control[] { cancel, ok, reset, clear });
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 4, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(tip, 0, 0); layout.Controls.Add(box, 0, 1); layout.Controls.Add(hint, 0, 2); layout.Controls.Add(buttons, 0, 3);
            Controls.Add(layout);
            var th = Theme.Current;
            BackColor = th.Bar; tip.ForeColor = th.BarFore; hint.ForeColor = th.Warn; box.BackColor = th.InputBack; box.ForeColor = th.Fore;
            foreach (var b in new[] { ok, cancel, clear, reset }) { b.FlatStyle = FlatStyle.Flat; b.BackColor = th.Menu; b.ForeColor = th.BarFore; b.FlatAppearance.BorderColor = th.Border; }
            HandleCreated += delegate { Native.DarkTitleBar(Handle, th.Dark); };
            Shown += delegate { box.Focus(); };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!box.Focused) return base.ProcessCmdKey(ref msg, keyData);
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
            uint mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= Native.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) mods |= Native.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) mods |= 4;
            if (Native.IsKeyDown(0x5B) || Native.IsKeyDown(0x5C)) mods |= 8;
            bool fkey = key >= Keys.F1 && key <= Keys.F24;
            if (!fkey && (mods & ~4u) == 0)
            {
                hint.Text = L.T("需要包含 Ctrl / Alt / Win 之一（只用 Shift 会和打字冲突）", "Must include Ctrl / Alt / Win (Shift alone conflicts with typing)");
                return true;
            }
            Result = Hotkey.Format(mods, key);
            box.Text = Hotkey.Display(Result);
            hint.Text = "";
            return true;
        }
    }

    /// <summary>独立查看模式（从资源管理器打开且托盘未运行）：所有查看窗口都关闭后退出</summary>
    sealed class ViewersContext : ApplicationContext
    {
        public ViewersContext()
        {
            JsonViewer.ViewerCreated += Track;
            foreach (Form f in Application.OpenForms) { var v = f as JsonViewer; if (v != null) Track(v); }
        }

        void Track(JsonViewer v) { v.FormClosed += delegate { Check(); }; }

        void Check()
        {
            foreach (Form f in Application.OpenForms) if (f is JsonViewer && !f.IsDisposed && f.Visible) return;
            ExitThread();
        }
    }
}
