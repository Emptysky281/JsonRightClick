using System;
using System.Drawing;
using System.Windows.Forms;

namespace JsonRightClick
{
    /// <summary>使用原生流式布局按需增减行数；不把按钮移入溢出菜单，不重建搜索输入框。</summary>
    sealed class WrappingToolStrip : ToolStrip
    {
        ToolStripTextBox searchBox;
        ToolStripItem breakAfter;
        Padding searchMargin;
        int preferredSearchWidth;
        bool layingOut;

        public WrappingToolStrip()
        {
            GripStyle = ToolStripGripStyle.Hidden;
            Dock = DockStyle.Top;
            AutoSize = true;
            CanOverflow = false;
            Stretch = true;
            Padding = new Padding(6, 3, 6, 3);
            LayoutStyle = ToolStripLayoutStyle.Flow;
            var flow = (FlowLayoutSettings)LayoutSettings;
            flow.FlowDirection = FlowDirection.LeftToRight;
            flow.WrapContents = true;
        }
        internal void ConfigureSearchGroup(ToolStripTextBox first)
        {
            searchBox = first;
            searchMargin = first.Margin;
            preferredSearchWidth = first.Width;
            foreach (ToolStripItem item in Items) item.Alignment = ToolStripItemAlignment.Left;
            PerformLayout();
        }
        static int ItemWidth(ToolStripItem item)
        {
            int width = item.AutoSize ? item.GetPreferredSize(Size.Empty).Width : item.Width;
            return Math.Max(0, width) + item.Margin.Horizontal;
        }
        void PrepareRows()
        {
            if (searchBox == null || searchBox.Owner != this || LayoutStyle != ToolStripLayoutStyle.Flow) return;
            int available = Math.Max(1, ClientSize.Width - Padding.Horizontal);
            int desired = Math.Max(1, Math.Min(preferredSearchWidth, available - searchMargin.Horizontal));
            if (searchBox.Width != desired) searchBox.Width = desired;
            var flow = (FlowLayoutSettings)LayoutSettings;
            int start = Items.IndexOf(searchBox), used = 0, trailing = 0;
            ToolStripItem last = null;
            for (int i = 0; i < start; i++)
            {
                var item = Items[i];
                if (!item.Available) continue;
                int width = ItemWidth(item);
                if (used > 0 && used + width > available) used = 0;
                used += width; last = item;
            }
            for (int i = start; i < Items.Count; i++)
            {
                var item = Items[i];
                if (!item.Available) continue;
                // 不把上次为了右对齐而添加的弹性间距算回本次所需宽度。
                trailing += item == searchBox ? desired + searchMargin.Horizontal : ItemWidth(item);
            }
            bool grouped = trailing <= available;
            bool newRow = grouped && used > 0 && used + trailing > available;
            if (breakAfter != last && breakAfter != null && flow.GetFlowBreak(breakAfter)) flow.SetFlowBreak(breakAfter, false);
            breakAfter = last;
            if (breakAfter != null && flow.GetFlowBreak(breakAfter) != newRow) flow.SetFlowBreak(breakAfter, newRow);
            // 够宽时保留搜索/设置区靠右；很窄时整个区域也可自然分行，不会裁掉搜索框。
            int gap = grouped ? Math.Max(0, available - trailing - (newRow ? 0 : used)) : 0;
            var margin = new Padding(searchMargin.Left + gap, searchMargin.Top, searchMargin.Right, searchMargin.Bottom);
            if (searchBox.Margin != margin) searchBox.Margin = margin;
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (layingOut) return;
            layingOut = true;
            try { PrepareRows(); base.OnLayout(e); }
            finally { layingOut = false; }
        }
    }
}
