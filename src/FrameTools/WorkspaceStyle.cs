using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FrameTools
{
    public static class WorkspaceStyle
    {
        public static bool IsDark { get; set; }
        public static Color Background { get { return IsDark ? Color.FromArgb(31, 31, 31) : Color.FromArgb(242, 242, 242); } }
        public static Color Surface { get { return IsDark ? Color.FromArgb(44, 44, 44) : Color.White; } }
        public static Color Text { get { return IsDark ? Color.FromArgb(232, 232, 232) : Color.FromArgb(32, 32, 32); } }
        public static Color Secondary { get { return IsDark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(96, 96, 96); } }
        public static Color Border { get { return IsDark ? Color.FromArgb(82, 82, 82) : Color.FromArgb(190, 190, 190); } }
        public static Color Blue { get { return Color.FromArgb(0, 120, 212); } }
        public static Color Header { get { return IsDark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(232, 232, 232); } }
        public static Color Hover { get { return IsDark ? Color.FromArgb(73, 73, 73) : Color.FromArgb(220, 231, 242); } }
        public static Color Alternate { get { return IsDark ? Color.FromArgb(54, 54, 54) : Color.FromArgb(246, 246, 246); } }
        public static Color Selection { get { return IsDark ? Blue : Color.FromArgb(205, 229, 250); } }
        public static Color Success { get { return IsDark ? Color.FromArgb(106, 209, 143) : Color.FromArgb(20, 110, 65); } }
        public static Color Error { get { return IsDark ? Color.FromArgb(255, 132, 132) : Color.Firebrick; } }
        public static Color Warning { get { return IsDark ? Color.FromArgb(239, 193, 92) : Color.DarkGoldenrod; } }

        public static void Apply(Control root)
        {
            root.BackColor = root.Parent == null ? Background : root.Parent.BackColor;
            root.ForeColor = Text;
            if (root is TextBoxBase || root is ComboBox || root is NumericUpDown || root is TabPage
                || root is WorkspaceGroupBox || root is WorkspaceEditor)
            {
                root.BackColor = Surface;
            }
            DataGridView grid = root as DataGridView;
            if (grid != null)
            {
                grid.BackgroundColor = Surface;
                grid.GridColor = IsDark ? Color.FromArgb(42, 42, 42) : Color.FromArgb(231, 231, 231);
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Text;
                grid.DefaultCellStyle.SelectionBackColor = Selection;
                grid.DefaultCellStyle.SelectionForeColor = IsDark ? Color.White : Text;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Alternate;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Header;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Header;
                grid.RowHeadersDefaultCellStyle.BackColor = Header;
            }
            string role = root.Tag as string;
            if (role == "secondary")
            {
                root.ForeColor = Secondary;
            }
            else if (role == "success")
            {
                root.ForeColor = Success;
            }
            else if (role == "error")
            {
                root.ForeColor = Error;
            }
            else if (role == "warning")
            {
                root.ForeColor = Warning;
            }
            foreach (Control child in root.Controls)
            {
                Apply(child);
            }
            root.Invalidate();
        }

        public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ConfigureGrid(DataGridView grid)
        {
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoGenerateColumns = false;
            grid.EnableHeadersVisualStyles = false;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeight = 24;
            grid.RowTemplate.Height = 23;
            grid.DefaultCellStyle.Padding = new Padding(3, 1, 3, 1);
        }

        public static void AddFieldColumns(DataGridView grid)
        {
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Offset", HeaderText = "偏移", Width = 54 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Length", HeaderText = "长度", Width = 54 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "字段", Width = 150 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Raw", HeaderText = "原始字节", Width = 180 });
            grid.Columns[3].DefaultCellStyle.Font = new Font("Consolas", 10F);
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                DataPropertyName = "Meaning", HeaderText = "解读", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180,
                DefaultCellStyle = new DataGridViewCellStyle { WrapMode = DataGridViewTriState.True }
            });
        }
    }

    public sealed class WorkspaceTextBox : TextBox
    {
        private bool updatingScrollbars;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern bool ShowScrollBar(IntPtr window, int bar, bool show);

        public WorkspaceTextBox()
        {
            Multiline = true;
            ScrollBars = ScrollBars.Both;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateScrollbars();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            UpdateScrollbars();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollbars();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateScrollbars();
        }

        private void UpdateScrollbars()
        {
            if (!IsHandleCreated || updatingScrollbars)
            {
                return;
            }
            updatingScrollbars = true;
            try
            {
                int textWidth = WordWrap || TextLength == 0 ? 0 : TextRenderer.MeasureText(Text, Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                // One scrollbar can make the other axis overflow.
                for (int pass = 0; pass < 2; pass++)
                {
                    SetScrollbar(0, 0x00100000, textWidth > ClientSize.Width);
                    int lines = (int)SendMessage(Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero); // EM_GETLINECOUNT includes wrapped lines.
                    SetScrollbar(1, 0x00200000, TextLength > 0 && lines * Font.Height > ClientSize.Height);
                }
            }
            finally
            {
                updatingScrollbars = false;
            }
        }

        private void SetScrollbar(int bar, int style, bool visible)
        {
            if (((GetWindowLong(Handle, -16) & style) != 0) != visible)
            {
                ShowScrollBar(Handle, bar, visible);
            }
        }
    }

    public sealed class WorkspaceButton : Button
    {
        private bool primary;
        private bool hover;
        private bool pressed;
        public WorkspaceButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            UseVisualStyleBackColor = false;
            AutoSize = true;
            Padding = new Padding(6, 1, 6, 1);
            Margin = new Padding(2, 2, 5, 2);
        }
        public bool Primary
        {
            get { return primary; }
            set { primary = value; Invalidate(); }
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            Size size = TextRenderer.MeasureText(Text, Font);
            return new Size(size.Width + Padding.Horizontal + 8, Math.Max(25, Font.Height + 7));
        }
        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = e.Button == MouseButtons.Left;
            Invalidate();
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? WorkspaceStyle.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = pressed ? WorkspaceStyle.Selection : hover && Enabled ? WorkspaceStyle.Hover : WorkspaceStyle.Header;
            using (GraphicsPath path = WorkspaceStyle.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 3))
            using (Brush brush = new SolidBrush(fill))
            using (Pen border = new Pen(primary && Enabled ? WorkspaceStyle.Blue : WorkspaceStyle.Border))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(border, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? WorkspaceStyle.Text : WorkspaceStyle.Secondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = ClientRectangle;
                focus.Inflate(-4, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, WorkspaceStyle.Text, fill);
            }
        }
    }

    public sealed class ThemeButton : Button
    {
        public ThemeButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            UseVisualStyleBackColor = false;
            Size = new Size(30, 26);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? WorkspaceStyle.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = WorkspaceStyle.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 3))
            using (Brush background = new SolidBrush(WorkspaceStyle.Header))
            using (Pen edge = new Pen(WorkspaceStyle.Border))
            using (Pen icon = new Pen(WorkspaceStyle.Text, 1.6F))
            using (Brush fill = new SolidBrush(WorkspaceStyle.Text))
            using (Brush cutout = new SolidBrush(WorkspaceStyle.Header))
            {
                e.Graphics.FillPath(background, path);
                e.Graphics.DrawPath(edge, path);
                float x = Width / 2F;
                float y = Height / 2F;
                if (WorkspaceStyle.IsDark)
                {
                    e.Graphics.DrawEllipse(icon, x - 5, y - 5, 10, 10);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4;
                        e.Graphics.DrawLine(icon, x + (float)Math.Cos(angle) * 8, y + (float)Math.Sin(angle) * 8,
                            x + (float)Math.Cos(angle) * 11, y + (float)Math.Sin(angle) * 11);
                    }
                }
                else
                {
                    e.Graphics.FillEllipse(fill, x - 8, y - 8, 16, 16);
                    e.Graphics.FillEllipse(cutout, x - 2, y - 11, 15, 15);
                }
            }
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = ClientRectangle;
                focus.Inflate(-3, -3);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, WorkspaceStyle.Text, WorkspaceStyle.Header);
            }
        }
    }

    public sealed class WorkspaceTabs : TabControl
    {
        public WorkspaceTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Appearance = TabAppearance.FlatButtons;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(110, 26);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(WorkspaceStyle.Background);
            for (int i = 0; i < TabCount; i++)
            {
                DrawTab(e.Graphics, GetTabRect(i), i);
            }
        }
        private void DrawTab(Graphics graphics, Rectangle bounds, int index)
        {
            bool selected = index == SelectedIndex;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF shape = new RectangleF(bounds.Left + 0.5F, bounds.Top + 0.5F, bounds.Width - 1F, bounds.Height - 1F);
            using (GraphicsPath path = WorkspaceStyle.RoundedRectangle(shape, 3))
            using (Brush fill = new SolidBrush(selected ? WorkspaceStyle.Surface : WorkspaceStyle.Header))
            using (Pen border = new Pen(WorkspaceStyle.Border))
            {
                graphics.FillPath(fill, path);
                graphics.DrawPath(border, path);
            }
            Rectangle icon = new Rectangle(bounds.Left + 9, bounds.Top + 7, 12, 12);
            using (Pen pen = new Pen(WorkspaceStyle.Text, 1.2F))
            {
                if (index == 0)
                {
                    graphics.DrawLines(pen, new[] { new Point(icon.Left, icon.Top), new Point(icon.Left + 4, icon.Top + 4), new Point(icon.Left, icon.Top + 8) });
                    graphics.DrawLine(pen, icon.Left + 6, icon.Top + 8, icon.Right, icon.Top + 8);
                }
                else if (index == 1)
                {
                    graphics.DrawRectangle(pen, icon.Left, icon.Top, 8, 10);
                    graphics.DrawRectangle(pen, icon.Left + 3, icon.Top + 3, 8, 10);
                }
                else
                {
                    for (int i = 0; i < 3; i++)
                    {
                        graphics.DrawLine(pen, icon.Left, icon.Top + i * 4, icon.Right, icon.Top + i * 4);
                        graphics.DrawEllipse(pen, icon.Left + (i % 2 == 0 ? 3 : 7), icon.Top + i * 4 - 1, 2, 2);
                    }
                }
                if (selected)
                {
                    using (Pen accent = new Pen(WorkspaceStyle.Blue, 2))
                    {
                        graphics.DrawLine(accent, bounds.Left + 4, bounds.Bottom - 2, bounds.Right - 4, bounds.Bottom - 2);
                    }
                }
            }
            Rectangle text = new Rectangle(bounds.Left + 25, bounds.Top, bounds.Width - 29, bounds.Height);
            TextRenderer.DrawText(graphics, TabPages[index].Text, Font, text, WorkspaceStyle.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected && Focused && ShowFocusCues)
            {
                bounds.Inflate(-4, -4);
                ControlPaint.DrawFocusRectangle(graphics, bounds, WorkspaceStyle.Text, WorkspaceStyle.Surface);
            }
        }
    }

    public sealed class WorkspaceGroupBox : GroupBox
    {
        public WorkspaceGroupBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? WorkspaceStyle.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float top = Font.Height / 2F;
            using (GraphicsPath path = WorkspaceStyle.RoundedRectangle(new RectangleF(0.5F, top, Width - 1F, Height - top - 1F), 3))
            using (Brush fill = new SolidBrush(WorkspaceStyle.Surface))
            using (Pen border = new Pen(WorkspaceStyle.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
            Size size = TextRenderer.MeasureText(Text, Font);
            Rectangle caption = new Rectangle(8, 0, size.Width + 4, size.Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, caption, WorkspaceStyle.Text, WorkspaceStyle.Surface, TextFormatFlags.Left);
        }
    }

    public sealed class WorkspaceEditor : Panel
    {
        public WorkspaceEditor()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(6);
            Margin = new Padding(0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? WorkspaceStyle.Background : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = WorkspaceStyle.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 3))
            using (Brush fill = new SolidBrush(WorkspaceStyle.Surface))
            using (Pen border = new Pen(WorkspaceStyle.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }
}
