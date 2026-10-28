using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FrameTools
{
    public static class WorkspaceStyle
    {
        public static readonly Color Background = Color.FromArgb(242, 242, 247);
        public static readonly Color Surface = Color.White;
        public static readonly Color Text = Color.FromArgb(28, 28, 30);
        public static readonly Color Secondary = Color.FromArgb(110, 110, 115);
        public static readonly Color Border = Color.FromArgb(209, 209, 214);
        public static readonly Color Blue = Color.FromArgb(0, 122, 255);

        internal static float Scale(Font font)
        {
            return Math.Max(1F, font.Height / 16F);
        }

        internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public sealed class WorkspaceButton : Button
    {
        private bool primary;
        private bool hovered;
        private bool pressed;

        public WorkspaceButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Padding = new Padding(12, 0, 12, 0);
        }

        public bool Primary
        {
            get { return primary; }
            set
            {
                primary = value;
                Invalidate();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            float scale = WorkspaceStyle.Scale(Font);
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
            return new Size(text.Width + Padding.Horizontal + (int)(12 * scale),
                Math.Max((int)(36 * scale), Font.Height + Padding.Vertical + (int)(14 * scale)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? WorkspaceStyle.Background : Parent.BackColor);
            if (ClientSize.Width < 2 || ClientSize.Height < 2)
            {
                return;
            }
            Color fill = primary ? WorkspaceStyle.Blue : Color.FromArgb(232, 232, 237);
            Color foreground = primary ? Color.White : WorkspaceStyle.Text;
            if (!Enabled)
            {
                fill = Color.FromArgb(229, 229, 234);
                foreground = Color.FromArgb(142, 142, 147);
            }
            else if (pressed)
            {
                fill = primary ? Color.FromArgb(0, 96, 215) : Color.FromArgb(210, 210, 216);
            }
            else if (hovered)
            {
                fill = primary ? Color.FromArgb(0, 112, 239) : Color.FromArgb(222, 222, 228);
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = WorkspaceStyle.RoundedRectangle(bounds, 9 * WorkspaceStyle.Scale(Font)))
            using (Brush brush = new SolidBrush(fill))
            {
                e.Graphics.FillPath(brush, path);
            }
            TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (!UseMnemonic)
            {
                flags |= TextFormatFlags.NoPrefix;
            }
            else if (!ShowKeyboardCues)
            {
                flags |= TextFormatFlags.HidePrefix;
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground, flags);
            if (Focused && ShowFocusCues && Enabled)
            {
                Rectangle focus = ClientRectangle;
                focus.Inflate(-5, -5);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, foreground, fill);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = e.Button == MouseButtons.Left;
            base.OnMouseDown(e);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            base.OnMouseUp(e);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                pressed = true;
                Invalidate();
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            pressed = false;
            base.OnKeyUp(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            pressed = false;
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            hovered = false;
            pressed = false;
            base.OnEnabledChanged(e);
            Invalidate();
        }
    }

    public sealed class ProtocolSwitch : CheckBox
    {
        public ProtocolSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            float scale = WorkspaceStyle.Scale(Font);
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.SingleLine);
            return new Size(text.Width + (int)(48 * scale), Math.Max((int)(32 * scale), Font.Height + 8));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? WorkspaceStyle.Background : Parent.BackColor);
            float scale = WorkspaceStyle.Scale(Font);
            float trackWidth = 38 * scale;
            float trackHeight = 22 * scale;
            if (ClientSize.Width < 2 || ClientSize.Height < 2)
            {
                return;
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = new RectangleF(1, (Height - trackHeight) / 2, trackWidth, trackHeight);
            Color trackColor = !Enabled ? Color.FromArgb(229, 229, 234)
                : Checked ? WorkspaceStyle.Blue : WorkspaceStyle.Border;
            using (GraphicsPath path = WorkspaceStyle.RoundedRectangle(track, trackHeight / 2))
            using (Brush brush = new SolidBrush(trackColor))
            {
                e.Graphics.FillPath(brush, path);
            }
            float inset = 2 * scale;
            float thumbSize = trackHeight - 2 * inset;
            float thumbX = Checked ? track.Right - inset - thumbSize : track.Left + inset;
            using (Brush thumb = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(thumb, thumbX, track.Top + inset, thumbSize, thumbSize);
            }
            int textStart = (int)(track.Right + 8 * scale);
            Rectangle textBounds = new Rectangle(textStart, 0, Math.Max(0, Width - textStart), Height);
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            if (!UseMnemonic)
            {
                flags |= TextFormatFlags.NoPrefix;
            }
            else if (!ShowKeyboardCues)
            {
                flags |= TextFormatFlags.HidePrefix;
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
                Enabled ? WorkspaceStyle.Text : WorkspaceStyle.Secondary, flags);
            if (Focused && ShowFocusCues)
            {
                textBounds.Inflate(-1, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, textBounds);
            }
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }

    public sealed class WorkspaceTabs : TabControl
    {
        public WorkspaceTabs()
        {
            Appearance = TabAppearance.FlatButtons;
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(160, 40);
            Padding = new Point(16, 8);
            HotTrack = true;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = e.Index == SelectedIndex;
            using (Brush background = new SolidBrush(WorkspaceStyle.Background))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }
            Rectangle bounds = e.Bounds;
            bounds.Inflate(-3, -3);
            if (bounds.Width < 2 || bounds.Height < 2)
            {
                return;
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = WorkspaceStyle.RoundedRectangle(bounds, 8))
            using (Brush fill = new SolidBrush(selected ? WorkspaceStyle.Surface : Color.FromArgb(229, 229, 234)))
            {
                e.Graphics.FillPath(fill, path);
                if (selected)
                {
                    using (Pen border = new Pen(WorkspaceStyle.Border))
                    {
                        e.Graphics.DrawPath(border, path);
                    }
                }
            }
            TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, bounds,
                selected ? WorkspaceStyle.Blue : WorkspaceStyle.Secondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (selected && Focused && ShowFocusCues)
            {
                bounds.Inflate(-5, -5);
                ControlPaint.DrawFocusRectangle(e.Graphics, bounds);
            }
        }

        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Invalidate();
        }
    }
}
