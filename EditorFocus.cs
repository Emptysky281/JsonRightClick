using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JsonRightClick
{
    sealed partial class CodeBox
    {
        int viewportReadDepth;
        bool imeComposing;
        [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
        static extern int RegisterEditorMessage(string name);
        static readonly int ImeQueryPosition = RegisterEditorMessage("MSIMEQueryPosition");

        // RichEdit 会在恢复焦点/回答输入法位置查询时把旧光标滚进屏幕。
        // 这些消息不是导航意图：必须在第一次鼠标命中测试之前保持当前横纵视口，
        // 不能通过重选旧光标、延迟回滚或把编辑焦点抢回来解决。
        protected override void WndProc(ref Message message)
        {
            bool positionQuery = (message.Msg == ImeQueryPosition && message.WParam == (IntPtr)1)
                || (message.Msg == 0x0288 /* WM_IME_REQUEST */ && message.WParam == (IntPtr)6 /* IMR_QUERYCHARPOSITION */);
            if (message.Msg == 0x010D /* WM_IME_STARTCOMPOSITION */) imeComposing = true;
            if (message.Msg == 0x010E /* WM_IME_ENDCOMPOSITION */ || message.Msg == 0x0008 /* WM_KILLFOCUS */) imeComposing = false;
            bool preserve = viewportReadDepth == 0 && IsHandleCreated && DocumentLength > 0
                && (message.Msg == 0x0007 /* WM_SETFOCUS */ || message.Msg == 0x0021 /* WM_MOUSEACTIVATE */
                    || (positionQuery && !imeComposing));
            if (!preserve) { ProcessEditorMessage(ref message); return; }

            Point before = ScrollPos;
            // 只在旧光标在屏幕外时暂停绘制，防止先跳到旧位置再恢复造成闪烁。
            Point caret = CharPosition(SelectionStart);
            bool redraw = Visible && (caret.Y < 0 || caret.Y + LineHeight > ClientSize.Height || caret.X < 0 || caret.X > ClientSize.Width);
            viewportReadDepth++;
            if (redraw) Native.SendMessage(Handle, 0x000B /* WM_SETREDRAW */, IntPtr.Zero, IntPtr.Zero);
            try
            {
                ProcessEditorMessage(ref message);
                Point after = ScrollPos;
                if (positionQuery && message.Result != IntPtr.Zero && message.LParam != IntPtr.Zero && after != before)
                {
                    // IMECHARPOSITION v1：保留引擎正常返回值，只把屏幕坐标换回实际视口。
                    // dwSize/dwCharPos 后是 POINT，不能让候选框使用临时滚动后的错误位置。
                    if (Marshal.ReadInt32(message.LParam) >= 36)
                    {
                        int x = Marshal.ReadInt32(message.LParam, 8), y = Marshal.ReadInt32(message.LParam, 12);
                        Marshal.WriteInt32(message.LParam, 8, x + after.X - before.X);
                        Marshal.WriteInt32(message.LParam, 12, y + after.Y - before.Y);
                    }
                }
            }
            finally
            {
                try { if (IsHandleCreated && !IsDisposed) ScrollPos = before; }
                finally
                {
                    viewportReadDepth--;
                    if (redraw && IsHandleCreated && !IsDisposed)
                    { Native.SendMessage(Handle, 0x000B, (IntPtr)1, IntPtr.Zero); Invalidate(); }
                    OnViewportChanged(); // 临时滚动不通知侧栏、提示和补色，最终视口最多通知一次。
                }
            }
        }
    }
}
