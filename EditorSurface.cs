using System.Windows.Forms;

namespace JsonRightClick
{
    /// <summary>只对编辑区及侧栏做系统合成，避免原生文字与叠加标记分两次呈现。</summary>
    sealed class EditorSurface : Panel
    {
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED; RichEdit 自绘不支持普通 WinForms 双缓冲。
                return parameters;
            }
        }
    }
}
