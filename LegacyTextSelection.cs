using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Accessibility;

namespace JsonRightClick
{
    // IAccessible2 的公开文本 ABI。仅读取选区及最多 32 字符的样本，不执行任何更改选区的方法。
    [ComImport, Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAccessibleTextSelection
    {
        [PreserveSig] int AddSelection(int start, int end);
        [PreserveSig] int Attributes(int offset, out int start, out int end, [MarshalAs(UnmanagedType.BStr)] out string attributes);
        [PreserveSig] int CaretOffset(out int offset);
        [PreserveSig] int CharacterExtents(int offset, int coordinateType, out int x, out int y, out int width, out int height);
        [PreserveSig] int SelectionCount(out int count);
        [PreserveSig] int OffsetAtPoint(int x, int y, int coordinateType, out int offset);
        [PreserveSig] int Selection(int index, out int start, out int end);
        [PreserveSig] int Text(int start, int end, [MarshalAs(UnmanagedType.BStr)] out string text);
    }
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAccessibilityServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
    }

    /// <summary>兼容 Chromium/Firefox/Electron 的 MSAA + IAccessible2，不把列表/图片的 accSelection 当作文本。</summary>
    static class LegacyTextSelection
    {
        static readonly Guid AccessibleId = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        static readonly Guid Accessible2Id = new Guid("E89F726E-C4F4-4C19-BB19-B647D7FA8478");
        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromPoint(Native.POINT point, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible,
            [MarshalAs(UnmanagedType.Struct)] out object child);
        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
        [DllImport("oleacc.dll")]
        static extern int WindowFromAccessibleObject(IAccessible accessible, out IntPtr window);

        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        static IAccessibleTextSelection TextInterface(IAccessible accessible)
        {
            var service = accessible as IAccessibilityServiceProvider;
            if (service == null) return null;
            Guid iid = typeof(IAccessibleTextSelection).GUID;
            foreach (var id in new[] { AccessibleId, Accessible2Id })
            {
                Guid sid = id; IntPtr pointer = IntPtr.Zero;
                try
                {
                    if (service.QueryService(ref sid, ref iid, out pointer) >= 0 && pointer != IntPtr.Zero)
                        return (IAccessibleTextSelection)Marshal.GetTypedObjectForIUnknown(pointer, typeof(IAccessibleTextSelection));
                }
                finally { if (pointer != IntPtr.Zero) Marshal.Release(pointer); }
            }
            return null;
        }
        internal static bool ValidOffsets(int start, int end)
        { return start >= 0 && end >= 0 && start != end; }
        internal static bool OffsetWithinSelection(int start, int end, int offset)
        { return ValidOffsets(start, end) && offset >= Math.Min(start, end) && offset <= Math.Max(start, end); }

        static Sel ReadChain(IAccessible accessible, IntPtr root, Point point, out bool blocked, Action<string> capture)
        {
            blocked = false; bool supported = false;
            try
            {
                for (int depth = 0; accessible != null && depth < 12; depth++)
                {
                    IntPtr owner;
                    if (WindowFromAccessibleObject(accessible, out owner) < 0 || owner == IntPtr.Zero || Native.Root(owner) != root) break;
                    int state = Convert.ToInt32(accessible.get_accState(0));
                    if ((state & 0x20000000 /* STATE_SYSTEM_PROTECTED */) != 0) { blocked = true; return Sel.No; }
                    int left, top, width, height;
                    accessible.accLocation(out left, out top, out width, out height, 0);
                    bool contains = width > 0 && height > 0 && new Rectangle(left, top, width, height).Contains(point);
                    if (contains && (state & (0x8000 | 0x10000) /* INVISIBLE/OFFSCREEN */) == 0)
                    {
                        IAccessibleTextSelection text = null;
                        try
                        {
                            text = TextInterface(accessible);
                            int count;
                            if (text != null && text.SelectionCount(out count) >= 0)
                            {
                                supported = true;
                                int role = Convert.ToInt32(accessible.get_accRole(0));
                                for (int i = 0; i < Math.Min(count, 16); i++)
                                {
                                    int start, end;
                                    if (text.Selection(i, out start, out end) < 0 || !ValidOffsets(start, end)) continue;
                                    int offset;
                                    bool located = text.OffsetAtPoint(point.X, point.Y, 0 /* IA2_COORDTYPE_SCREEN_RELATIVE */, out offset) >= 0 && offset >= 0;
                                    if (located && !OffsetWithinSelection(start, end, offset)) continue;
                                    // 容器的其他地方有选区，不应让点击画布/图片也触发按钮。
                                    if (!located && role != 41 /* STATICTEXT */ && role != 42 /* TEXT */) continue;
                                    int from = Math.Min(start, end), to = Math.Max(start, end);
                                    string sample;
                                    if (text.Text(from, from + (int)Math.Min(32L, (long)to - from), out sample) >= 0
                                        && SelectionProbe.NonEmptyTextRange(-1, sample))
                                    {
                                        if (capture != null && (long)to - from <= SelectedTextReader.MaxCharacters)
                                        {
                                            string full;
                                            if (text.Text(from, to, out full) >= 0 && full != null && full.Length == to - from && SelectedTextReader.Accept(full)) capture(full);
                                        }
                                        return Sel.Yes;
                                    }
                                }
                            }
                        }
                        catch { }
                        finally { Release(text); }
                    }
                    var parent = accessible.accParent as IAccessible;
                    Release(accessible); accessible = parent;
                }
            }
            catch { }
            finally { Release(accessible); }
            return supported ? Sel.No : Sel.Unknown;
        }
        public static Sel AtPoint(IntPtr root, Point point, out bool blocked, Action<string> capture = null)
        {
            blocked = false; IAccessible accessible = null; object child;
            try
            {
                if (AccessibleObjectFromPoint(new Native.POINT { X = point.X, Y = point.Y }, out accessible, out child) < 0 || accessible == null)
                    return Sel.Unknown;
                if (child is int && (int)child != 0)
                {
                    var item = accessible.get_accChild(child) as IAccessible;
                    if (item != null) { Release(accessible); accessible = item; }
                }
                var owned = accessible; accessible = null; // 所有权交给 ReadChain，异常路径也释放 RCW。
                return ReadChain(owned, root, point, out blocked, capture);
            }
            catch { return Sel.Unknown; }
            finally { Release(accessible); }
        }
        public static Sel AtWindow(IntPtr window, IntPtr root, Point point, out bool blocked, Action<string> capture = null)
        {
            blocked = false; IAccessible accessible = null; Guid iid = AccessibleId;
            try
            {
                if (AccessibleObjectFromWindow(window, 0xFFFFFFFC /* OBJID_CLIENT */, ref iid, out accessible) < 0 || accessible == null)
                    return Sel.Unknown;
                // 原生菜单遮住鼠标时，在保存的原控件里做 MSAA 命中，而非读取菜单。
                for (int depth = 0; depth < 8; depth++)
                {
                    var hit = accessible.accHitTest(point.X, point.Y) as IAccessible;
                    if (hit == null || ReferenceEquals(hit, accessible)) { if (hit != null) Release(hit); break; }
                    Release(accessible); accessible = hit;
                }
                var owned = accessible; accessible = null; // 所有权交给 ReadChain，异常路径也释放 RCW。
                return ReadChain(owned, root, point, out blocked, capture);
            }
            catch { return Sel.Unknown; }
            finally { Release(accessible); }
        }
    }
}
