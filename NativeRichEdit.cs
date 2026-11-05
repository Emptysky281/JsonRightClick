using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace JsonRightClick
{
    sealed partial class CodeBox
    {
        [StructLayout(LayoutKind.Sequential)]
        struct FormatRectangle { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr GetFormatRectangle(IntPtr handle, int message, IntPtr unused, ref FormatRectangle rectangle);

        Point ReadPixelScrollPosition()
        {
            // 旧 RichEdit20W 在长文档中把 EM_GETSCROLLPOS 缩放成 16 位滚动条坐标，
            // 但 EM_SETSCROLLPOS 接收的仍是真实像素。由字符 0 的位置求真实偏移，
            // 不更换控件、不改文本流或折叠语义，就可以全程按像素缓动。
            var rectangle = new FormatRectangle();
            GetFormatRectangle(Handle, 0x00B2 /* EM_GETRECT */, IntPtr.Zero, ref rectangle);
            Point origin = CharPosition(0);
            return new Point(Math.Max(0, rectangle.Left - origin.X), Math.Max(0, rectangle.Top - origin.Y));
        }

        int VerticalPixelLimit(out bool ok)
        {
            var info = new Native.SCROLLINFO { cbSize = (uint)Marshal.SizeOf(typeof(Native.SCROLLINFO)), fMask = 0x17 };
            ok = Native.GetScrollInfo(Handle, 1, ref info);
            if (!ok || DocumentLength == 0) return 0;
            if (info.nMax < 65535) return Math.Max(0, info.nMax - Math.Max(0, (int)info.nPage - 1));
            var rectangle = new FormatRectangle();
            GetFormatRectangle(Handle, 0x00B2, IntPtr.Zero, ref rectangle);
            Point first = CharPosition(0), last = CharPosition(DocumentLength);
            return Math.Max(0, last.Y - first.Y + LineHeight - Math.Max(0, rectangle.Bottom - rectangle.Top - 1));
        }
    }
}
