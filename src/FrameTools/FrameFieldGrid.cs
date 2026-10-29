using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace FrameTools
{
    internal sealed class FrameFieldGrid : DataGridView
    {
        private sealed class FieldNode
        {
            public readonly FrameField Field;
            public readonly List<FieldNode> Children = new List<FieldNode>();
            public bool Expanded = true;
            public int Depth;

            public FieldNode(FrameField field)
            {
                Field = field;
            }
        }

        private List<FieldNode> roots = new List<FieldNode>();
        private readonly List<FieldNode> visible = new List<FieldNode>();

        public FrameFieldGrid()
        {
            DoubleBuffered = true;
            AccessibleName = "可折叠报文字段";
            AccessibleDescription = "点击字段左侧箭头展开或折叠；选中分组后也可使用左右方向键。";
        }

        public void ShowFields(FrameResult result, ProtocolKind? protocol)
        {
            roots = result.Fields.Select(field => new FieldNode(field)).ToList();
            FieldNode control = roots.FirstOrDefault(node => node.Field.Name == "控制域 C" || node.Field.Name == "控制码 C");
            if (control != null)
            {
                // Present the existing control explanation in separate rows without decoding it again.
                foreach (string part in control.Field.Meaning.Split('；'))
                {
                    control.Children.Add(new FieldNode(new FrameField
                    {
                        Offset = control.Field.Offset,
                        Length = control.Field.Length,
                        Name = part.Split('=')[0],
                        Raw = control.Field.Raw,
                        Meaning = part
                    }));
                }
            }
            NestField("数据域 DATA");
            NestField("数据标识内容");
            NestField("首个数据内容");
            NestField("应用业务区");
            FieldNode checksum = roots.FirstOrDefault(node => node.Field.Name == "校验和 CS");
            if (control != null && checksum != null)
            {
                if (protocol == ProtocolKind.CsgLocal)
                {
                    FieldNode di = roots.FirstOrDefault(node => node.Field.Name == "数据标识 DI");
                    if (di != null)
                    {
                        AddGroup(result, di.Field.Offset, checksum.Field.Offset, "应用数据域", "数据标识及其内容");
                    }
                    AddGroup(result, control.Field.Offset + 1, checksum.Field.Offset, "用户数据域", "地址（如有）、AFN、SEQ及应用数据");
                }
                else if (protocol == ProtocolKind.CsgUplink)
                {
                    AddGroup(result, control.Field.Offset, checksum.Field.Offset, "用户数据域", "控制域、地址域及链路用户数据；不含CS和结束符");
                }
            }
            ShowRows(null);
        }

        private void NestField(string name)
        {
            FieldNode parent = roots.FirstOrDefault(node => node.Field.Name == name);
            if (parent != null)
            {
                MoveChildren(parent);
            }
        }

        private void AddGroup(FrameResult result, int start, int end, string name, string meaning)
        {
            FieldNode group = new FieldNode(new FrameField
            {
                Offset = start,
                Length = end - start,
                Name = name,
                Raw = Hex.Format(result.Bytes.Skip(start).Take(end - start).ToArray()),
                Meaning = meaning
            });
            int index = roots.FindIndex(node => node.Field.Offset >= start);
            roots.Insert(index, group);
            MoveChildren(group);
        }

        private void MoveChildren(FieldNode parent)
        {
            int end = parent.Field.Offset + parent.Field.Length;
            foreach (FieldNode node in roots.ToArray())
            {
                if (node != parent && node.Field.Offset >= parent.Field.Offset && node.Field.Offset + node.Field.Length <= end)
                {
                    parent.Children.Add(node);
                    roots.Remove(node);
                }
            }
        }

        private void AppendVisible(IEnumerable<FieldNode> nodes, int depth)
        {
            foreach (FieldNode node in nodes.OrderBy(item => item.Field.Offset))
            {
                node.Depth = depth;
                visible.Add(node);
                if (node.Expanded)
                {
                    AppendVisible(node.Children, depth + 1);
                }
            }
        }

        private void ShowRows(FieldNode selected)
        {
            int firstRow = FirstDisplayedScrollingRowIndex;
            visible.Clear();
            AppendVisible(roots, 0);
            DataSource = visible.Select(node => node.Field).ToList();
            if (selected != null)
            {
                CurrentCell = Rows[visible.IndexOf(selected)].Cells[2];
                if (firstRow >= 0)
                {
                    FirstDisplayedScrollingRowIndex = Math.Min(firstRow, Rows.Count - 1);
                }
            }
        }

        private void SetExpanded(FieldNode node, bool expanded)
        {
            if (node.Expanded != expanded)
            {
                node.Expanded = expanded;
                ShowRows(node);
            }
        }

        private int Indent
        {
            get
            {
                return Math.Max(18, Font.Height + 2);
            }
        }

        protected override void OnDataSourceChanged(EventArgs e)
        {
            if (DataSource == null)
            {
                roots.Clear();
                visible.Clear();
            }
            base.OnDataSourceChanged(e);
        }

        protected override void OnColumnAdded(DataGridViewColumnEventArgs e)
        {
            e.Column.SortMode = DataGridViewColumnSortMode.NotSortable;
            base.OnColumnAdded(e);
        }

        protected override void OnCellFormatting(DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex == 2 && e.RowIndex >= 0 && e.RowIndex < visible.Count)
            {
                e.CellStyle.Padding = new Padding((visible[e.RowIndex].Depth + 1) * Indent, 1, 3, 1);
            }
            base.OnCellFormatting(e);
        }

        protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex == 2 && e.RowIndex >= 0 && e.RowIndex < visible.Count && visible[e.RowIndex].Children.Count > 0)
            {
                e.Paint(e.ClipBounds, e.PaintParts);
                FieldNode node = visible[e.RowIndex];
                int x = e.CellBounds.Left + node.Depth * Indent + 6;
                int y = e.CellBounds.Top + e.CellBounds.Height / 2;
                Point[] arrow = node.Expanded
                    ? new[] { new Point(x, y - 2), new Point(x + 8, y - 2), new Point(x + 4, y + 3) }
                    : new[] { new Point(x + 1, y - 4), new Point(x + 1, y + 4), new Point(x + 6, y) };
                using (Brush brush = new SolidBrush((e.State & DataGridViewElementStates.Selected) != 0
                    ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor))
                {
                    e.Graphics.FillPolygon(brush, arrow);
                }
                e.Handled = true;
            }
            base.OnCellPainting(e);
        }

        protected override void OnCellMouseClick(DataGridViewCellMouseEventArgs e)
        {
            base.OnCellMouseClick(e);
            if (e.Button == MouseButtons.Left && e.ColumnIndex == 2 && e.RowIndex >= 0 && e.RowIndex < visible.Count)
            {
                FieldNode node = visible[e.RowIndex];
                if (node.Children.Count > 0 && e.X >= node.Depth * Indent && e.X < (node.Depth + 1) * Indent)
                {
                    SetExpanded(node, !node.Expanded);
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (CurrentRow != null && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
            {
                FieldNode node = visible[CurrentRow.Index];
                if (node.Children.Count > 0)
                {
                    SetExpanded(node, e.KeyCode == Keys.Right);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    return;
                }
            }
            base.OnKeyDown(e);
        }
    }
}
