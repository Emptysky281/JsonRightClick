using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace JsonRightClick
{
    /// <summary>
    /// JSON 树形视图：子节点展开时才创建（大文件也快）；键 / 值分色；
    /// 时间戳、JWT、嵌套 JSON 字符串会加注释，JWT / 嵌套 JSON 可以直接展开看解码内容。
    /// </summary>
    sealed class JsonTree : TreeView
    {
        const int MaxChildren = 2000;
        public event Action<JNode> NodePicked;
        public event Action<string> PathPicked;
        int rootCount;
        Theme th = Theme.Light;

        sealed class Item
        {
            public JNode Node;
            public string Title;                 // 多段 JSON 时的根节点名
            public List<KeyValuePair<int, string>> Segs = new List<KeyValuePair<int, string>>(); // 颜色编号 → 文字
        }

        // 颜色编号：-1 弱化文字，其余为语法颜色下标（3 键名 4 字符串 5 数字 6 字面量 2 标点）
        const int CMuted = -1;

        public JsonTree()
        {
            DrawMode = TreeViewDrawMode.OwnerDrawText;
            HideSelection = false;
            BorderStyle = BorderStyle.None;
            ShowLines = false;
            ShowRootLines = true;
            FullRowSelect = false;
            ContextMenuStrip = BuildMenu();
            BeforeExpand += (s, e) => Populate(e.Node);
            AfterSelect += (s, e) =>
            {
                if (e.Action == TreeViewAction.Unknown) return;
                var it = e.Node.Tag as Item;
                if (it != null && it.Node != null && NodePicked != null) NodePicked(it.Node);
                if (PathPicked != null) PathPicked(PathOfNode(e.Node));
            };
            NodeMouseClick += (s, e) => { if (e.Button == MouseButtons.Right) SelectedNode = e.Node; };
        }

        public void ApplyTheme(Theme t, Font font)
        {
            th = t;
            BackColor = t.Back; ForeColor = t.Fore; LineColor = t.Border;
            if (font != null && (Font == null || !Font.Equals(font))) Font = font;
            ItemHeight = Math.Max(16, TextRenderer.MeasureText("Ag\u56fd", Font).Height + 4);
            if (IsHandleCreated) Native.SetWindowTheme(Handle, t.Dark ? "DarkMode_Explorer" : "Explorer", null);
            if (ContextMenuStrip != null) ContextMenuStrip.Renderer = new ThemeRenderer(t);
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetWindowTheme(Handle, th.Dark ? "DarkMode_Explorer" : "Explorer", null);
        }

        // ---------- 加载 ----------
        public void SetRoots(List<JNode> roots)
        {
            var expanded = new HashSet<string>();
            string selected = SelectedNode == null ? null : IdOf(SelectedNode);
            foreach (TreeNode n in Nodes) CollectExpanded(n, expanded);
            BeginUpdate();
            try
            {
                Nodes.Clear();
                rootCount = roots.Count;
                for (int i = 0; i < roots.Count; i++)
                {
                    var tn = Make(roots[i], roots.Count > 1 ? L.F("JSON 片段 {0}", i + 1) : null);
                    Nodes.Add(tn);
                }
                if (expanded.Count == 0)
                {
                    foreach (TreeNode r in Nodes) { r.Expand(); if (Nodes.Count == 1) foreach (TreeNode c in r.Nodes) if (c.Nodes.Count > 0 && r.Nodes.Count <= 30) c.Expand(); }
                }
                else foreach (TreeNode r in Nodes) Restore(r, expanded);
                if (selected != null)
                {
                    var hit = FindId(Nodes, selected);
                    if (hit != null) SelectedNode = hit;
                }
                if (SelectedNode == null && Nodes.Count > 0) SelectedNode = Nodes[0];
            }
            finally { EndUpdate(); }
        }

        void CollectExpanded(TreeNode n, HashSet<string> set)
        {
            if (!n.IsExpanded) return;
            set.Add(IdOf(n));
            foreach (TreeNode c in n.Nodes) CollectExpanded(c, set);
        }

        void Restore(TreeNode n, HashSet<string> set)
        {
            if (!set.Contains(IdOf(n))) return;
            n.Expand();
            foreach (TreeNode c in n.Nodes) Restore(c, set);
        }

        TreeNode FindId(TreeNodeCollection nodes, string id)
        {
            foreach (TreeNode n in nodes)
            {
                string nid = IdOf(n);
                if (nid == id) return n;
                if (id.StartsWith(nid + "/") && n.IsExpanded) { var r = FindId(n.Nodes, id); if (r != null) return r; }
            }
            return null;
        }

        static string IdOf(TreeNode n)
        {
            var parts = new List<string>();
            for (var x = n; x != null; x = x.Parent)
            {
                var it = x.Tag as Item;
                if (it == null) { parts.Add("?"); continue; }
                parts.Add(it.Title ?? it.Node.Label ?? it.Node.Key ?? (it.Node.Index >= 0 ? "#" + it.Node.Index : "$"));
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        TreeNode Make(JNode n, string title)
        {
            var it = new Item { Node = n, Title = title };
            Describe(it);
            var tn = new TreeNode(TextOf(it)) { Tag = it };
            if (n.Count > 0 || n.MaybeVirtual) tn.Nodes.Add(new TreeNode("…")); // 占位，展开时再创建
            return tn;
        }

        static string TextOf(Item it)
        {
            var t = new System.Text.StringBuilder();
            foreach (var s in it.Segs) t.Append(s.Value);
            return t.Append("   ").ToString(); // 留点余量，让选中背景完整覆盖文字
        }

        void Populate(TreeNode tn)
        {
            if (tn.Nodes.Count != 1 || tn.Nodes[0].Tag != null) return;
            var n = ((Item)tn.Tag).Node;
            BeginUpdate();
            try
            {
                tn.Nodes.Clear();
                var list = n.Children ?? n.VirtualChildren;
                if (list == null) return;
                int shown = Math.Min(list.Count, MaxChildren);
                var arr = new TreeNode[shown];
                for (int i = 0; i < shown; i++) arr[i] = Make(list[i], null);
                tn.Nodes.AddRange(arr);
                if (list.Count > shown)
                {
                    var more = new Item();
                    more.Segs.Add(new KeyValuePair<int, string>(CMuted, L.F("… 还有 {0} 项未显示，请在文本中查看", list.Count - shown)));
                    tn.Nodes.Add(new TreeNode(TextOf(more)) { Tag = more });
                }
            }
            finally { EndUpdate(); }
        }

        // ---------- 显示文字 ----------
        void Describe(Item it)
        {
            var n = it.Node;
            var segs = it.Segs;
            if (it.Title != null) segs.Add(new KeyValuePair<int, string>(CMuted, it.Title + "  "));
            if (n.Label != null) segs.Add(new KeyValuePair<int, string>(3, "‹" + n.Label + "›"));
            else if (n.Key != null) segs.Add(new KeyValuePair<int, string>(3, n.Key));
            else if (n.Index >= 0) segs.Add(new KeyValuePair<int, string>(CMuted, "[" + n.Index + "]"));
            if (n.Label != null || n.Key != null || n.Index >= 0) segs.Add(new KeyValuePair<int, string>(2, n.Kind == JKind.Object || n.Kind == JKind.Array ? "  " : ": "));

            switch (n.Kind)
            {
                case JKind.Object: segs.Add(new KeyValuePair<int, string>(CMuted, "{ } " + L.F("{0} 个字段", n.Count))); break;
                case JKind.Array: segs.Add(new KeyValuePair<int, string>(CMuted, "[ ] " + L.F("{0} 项", n.Count))); break;
                case JKind.String:
                    {
                        string raw = n.Raw;
                        segs.Add(new KeyValuePair<int, string>(4, raw.Length > 80 ? raw.Substring(0, 77) + "…\"" : raw));
                        string v = n.End - n.VStart <= 15 ? n.StringValue : null;
                        string time = v == null ? null : TimeHint.Format(v);
                        if (time != null) segs.Add(new KeyValuePair<int, string>(CMuted, "   ⏱ " + time));
                        else if (n.MaybeVirtual)
                        {
                            var vc = n.VirtualChildren;
                            bool jwt = vc != null && vc.Count > 0 && vc[0].Label != null && vc[0].Label.StartsWith("JWT");
                            segs.Add(new KeyValuePair<int, string>(CMuted, jwt ? L.T("   ← JWT，展开查看解码内容") : L.T("   ← 嵌套 JSON，可展开")));
                        }
                        break;
                    }
                case JKind.Number:
                    {
                        string raw = n.Raw;
                        segs.Add(new KeyValuePair<int, string>(5, raw));
                        string time = TimeHint.Format(raw);
                        if (time != null) segs.Add(new KeyValuePair<int, string>(CMuted, "   ⏱ " + time));
                        break;
                    }
                default: segs.Add(new KeyValuePair<int, string>(6, n.Raw)); break;
            }
        }

        Color ColorOf(int id) { return id == CMuted ? th.Muted : th.Syntax[id]; }

        protected override void OnDrawNode(DrawTreeNodeEventArgs e)
        {
            var it = e.Node.Tag as Item;
            if (it == null || e.Bounds.Width <= 0 || e.Bounds.Height <= 0) { e.DrawDefault = true; return; }
            bool sel = (e.State & TreeNodeStates.Selected) != 0;
            using (var b = new SolidBrush(sel ? th.Checked : th.Back)) e.Graphics.FillRectangle(b, e.Bounds);
            int x = e.Bounds.X + 1;
            const TextFormatFlags f = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            foreach (var s in it.Segs)
            {
                var size = TextRenderer.MeasureText(e.Graphics, s.Value, Font, new Size(int.MaxValue, e.Bounds.Height), f);
                TextRenderer.DrawText(e.Graphics, s.Value, Font, new Rectangle(x, e.Bounds.Y, size.Width + 2, e.Bounds.Height), ColorOf(s.Key), f);
                x += size.Width;
            }
        }

        // ---------- 右键菜单 ----------
        ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.Add(L.T("复制值"), null, delegate { Copy(ValueText(Sel)); });
            m.Items.Add(L.T("复制键名"), null, delegate { var n = Sel; if (n != null) Copy(n.Key ?? n.Label ?? (n.Index >= 0 ? n.Index.ToString() : "")); });
            m.Items.Add(L.T("复制路径"), null, delegate { if (SelectedNode != null) Copy(PathOfNode(SelectedNode)); });
            m.Items.Add(L.T("复制为格式化 JSON"), null, delegate { var n = Sel; if (n != null) Copy(JsonTools.Pretty(n.Raw) ?? n.Raw); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(L.T("展开全部子项"), null, delegate { if (SelectedNode != null) { BeginUpdate(); ExpandDeep(SelectedNode, 0); EndUpdate(); } });
            m.Items.Add(L.T("全部折叠"), null, delegate { CollapseAll(); if (Nodes.Count > 0) Nodes[0].Expand(); });
            return m;
        }

        JNode Sel { get { var it = SelectedNode == null ? null : SelectedNode.Tag as Item; return it == null ? null : it.Node; } }

        static string ValueText(JNode n)
        {
            if (n == null) return null;
            return n.Kind == JKind.String ? n.StringValue : n.Raw;
        }

        static void Copy(string s) { if (!string.IsNullOrEmpty(s)) ClipboardUtil.SetText(s.Replace("\r\n", "\n").Replace("\n", "\r\n")); }

        int expandBudget;
        void ExpandDeep(TreeNode n, int depth)
        {
            if (depth == 0) expandBudget = 5000;
            if (depth > 30 || --expandBudget < 0) return;
            n.Expand();
            foreach (TreeNode c in n.Nodes) if (c.Tag is Item && ((Item)c.Tag).Node != null) ExpandDeep(c, depth + 1);
        }

        public string PathOfNode(TreeNode tn)
        {
            var it = tn == null ? null : tn.Tag as Item;
            if (it == null || it.Node == null) return "";
            var root = tn; while (root.Parent != null) root = root.Parent;
            var rit = root.Tag as Item;
            string path = Dom.PathOf(it.Node);
            return rootCount > 1 && rit != null && rit.Title != null ? rit.Title + " › " + path : path;
        }
    }
}
