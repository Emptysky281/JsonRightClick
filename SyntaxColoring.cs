using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace JsonRightClick
{
    /// <summary>基于编辑框实际字符位置缓存词法颜色；滚动时不重新扫描长字符串或 JSON。</summary>
    sealed class SyntaxIndex
    {
        struct Token { public int Start, End, Color; }
        readonly List<Token> tokens = new List<Token>();
        public SyntaxIndex(string text, List<int[]> ranges)
        {
            foreach (var range in ranges)
                Rtf.Tokenize(text, range[0], Math.Min(text.Length, range[0] + range[1]), (start, length, color) =>
                {
                    if (length > 0) tokens.Add(new Token { Start = start, End = start + length, Color = color });
                });
        }
        public void Visit(int start, int end, Action<int, int, int> emit)
        { VisitUntil(start, end, (i, length, color) => { emit(i, length, color); return true; }); }
        public int VisitUntil(int start, int end, Func<int, int, int, bool> emit)
        {
            int lo = 0, hi = tokens.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (tokens[mid].End <= start) lo = mid + 1; else hi = mid; }
            for (int i = lo; i < tokens.Count && tokens[i].Start < end; i++)
            {
                var t = tokens[i];
                int from = Math.Max(start, t.Start), to = Math.Min(end, t.End);
                if (to > from && !emit(from, to - from, t.Color)) return to;
            }
            return end;
        }

    }

    static partial class RichEditFold
    {
        // TOM Range/Font 不改编辑框选区，因此没有光标跳转、选区闪烁及对应的事件风暴。
        public static ColorBatch Colors(CodeBox box) { return new ColorBatch(box); }
        public sealed class ColorBatch : IDisposable
        {
            UndoSuspension undo;
            ITextRange range;
            bool frozen, restoreReadOnly;
            readonly IntPtr handle;
            internal ColorBatch(CodeBox box)
            {
                handle = box.Handle;
                undo = new UndoSuspension(handle);
                try
                {
                    int count;
                    Marshal.ThrowExceptionForHR(undo.Document.Freeze(out count)); frozen = true;
                    if (box.ReadOnly)
                    {
                        // TOM 不允许给原生只读范围赋颜色。保持托管 ReadOnly 不变，在冻结呈现、
                        // 不泵送用户消息的同步批次中临时解除引擎限制，恢复之后才 Unfreeze。
                        if (Native.SendMessage(handle, 0x00CF /* EM_SETREADONLY */, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero)
                            throw new InvalidOperationException("RichEdit could not prepare read-only syntax coloring.");
                        restoreReadOnly = true;
                    }
                    Marshal.ThrowExceptionForHR(undo.Document.Range(0, 0, out range));
                }
                catch { Dispose(); throw; }
            }
            public void Set(int start, int length, Color color)
            {
                if (length <= 0) return;
                Marshal.ThrowExceptionForHR(range.SetRange(start, start + length));
                ITextFont font;
                Marshal.ThrowExceptionForHR(range.GetFont(out font));
                try { Marshal.ThrowExceptionForHR(font.SetForeColor(color.R | (color.G << 8) | (color.B << 16))); }
                finally { Marshal.ReleaseComObject(font); }
            }
            public void Dispose()
            {
                if (range != null) { Marshal.ReleaseComObject(range); range = null; }
                if (undo == null) return;
                try
                {
                    if (restoreReadOnly) { Native.SendMessage(handle, 0x00CF, (IntPtr)1, IntPtr.Zero); restoreReadOnly = false; }
                    if (frozen) { int count; undo.Document.Unfreeze(out count); frozen = false; }
                }
                finally { undo.Dispose(); undo = null; }
            }
        }

        // 只声明调用所需的前缀槽位；顺序与 Windows SDK TOM.h 一致。
        [ComImport, Guid("8CC497C2-A1DF-11CE-8098-00AA0047BE5D"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface ITextRange
        {
            [PreserveSig] int GetText(out IntPtr text);
            [PreserveSig] int SetText(IntPtr text);
            [PreserveSig] int GetChar(out int value);
            [PreserveSig] int SetChar(int value);
            [PreserveSig] int GetDuplicate(out IntPtr range);
            [PreserveSig] int GetFormattedText(out IntPtr range);
            [PreserveSig] int SetFormattedText(IntPtr range);
            [PreserveSig] int GetStart(out int value);
            [PreserveSig] int SetStart(int value);
            [PreserveSig] int GetEnd(out int value);
            [PreserveSig] int SetEnd(int value);
            [PreserveSig] int GetFont(out ITextFont font);
            [PreserveSig] int SetFont(IntPtr font);
            [PreserveSig] int GetPara(out IntPtr para);
            [PreserveSig] int SetPara(IntPtr para);
            [PreserveSig] int GetStoryLength(out int value);
            [PreserveSig] int GetStoryType(out int value);
            [PreserveSig] int Collapse(int start);
            [PreserveSig] int Expand(int unit, out int delta);
            [PreserveSig] int GetIndex(int unit, out int value);
            [PreserveSig] int SetIndex(int unit, int index, int extend);
            [PreserveSig] int SetRange(int start, int end);
        }
        [ComImport, Guid("8CC497C3-A1DF-11CE-8098-00AA0047BE5D"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        interface ITextFont
        {
            [PreserveSig] int GetDuplicate(out IntPtr font);
            [PreserveSig] int SetDuplicate(IntPtr font);
            [PreserveSig] int CanChange(out int value);
            [PreserveSig] int IsEqual(IntPtr font, out int value);
            [PreserveSig] int Reset(int value);
            [PreserveSig] int GetStyle(out int value);
            [PreserveSig] int SetStyle(int value);
            [PreserveSig] int GetAllCaps(out int value);
            [PreserveSig] int SetAllCaps(int value);
            [PreserveSig] int GetAnimation(out int value);
            [PreserveSig] int SetAnimation(int value);
            [PreserveSig] int GetBackColor(out int value);
            [PreserveSig] int SetBackColor(int value);
            [PreserveSig] int GetBold(out int value);
            [PreserveSig] int SetBold(int value);
            [PreserveSig] int GetEmboss(out int value);
            [PreserveSig] int SetEmboss(int value);
            [PreserveSig] int GetForeColor(out int value);
            [PreserveSig] int SetForeColor(int value);
        }
    }
}
