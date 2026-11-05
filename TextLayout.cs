using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace JsonRightClick
{
    sealed partial class CodeBox
    {
        // Framework RichTextBox.GetPositionFromCharIndex 会先求 TextLength，长文档中每次都可能扫描全文。
        // 编辑器已经保存了完整源文本及 UTF-16 位置，绘制时直接调用 RichEdit 的布局接口即可。
        internal int DocumentLength { get { return folding == null ? 0 : folding.DocumentLength; } }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr PositionFromChar(IntPtr handle, int message, ref Native.POINT point, IntPtr index);

        internal Point CharPosition(int index)
        {
            if (index < 0 || !IsHandleCreated) return Point.Empty;
            var point = new Native.POINT { X = int.MinValue, Y = int.MinValue };
            PositionFromChar(Handle, 0x00D6 /* EM_POSFROMCHAR, RichEdit 2.0+ */, ref point, (IntPtr)index);
            return point.X == int.MinValue ? Point.Empty : new Point(point.X, point.Y);
        }

        internal int CharIndexAt(Point position)
        {
            if (!IsHandleCreated || DocumentLength == 0) return 0;
            var point = new Native.POINT { X = position.X, Y = position.Y };
            int index = (int)Native.SendMessage(Handle, 0x00D7 /* EM_CHARFROMPOS */, IntPtr.Zero, ref point);
            return Math.Max(0, Math.Min(DocumentLength - 1, index));
        }
    }
}
