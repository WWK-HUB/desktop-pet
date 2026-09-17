using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopPet
{
    internal static class UiTheme
    {
        internal static readonly Color Background = Color.FromArgb(239, 247, 252), Ink = Color.FromArgb(36, 57, 76), Muted = Color.FromArgb(99, 130, 153), Accent = Color.FromArgb(20, 151, 179), Soft = Color.FromArgb(225, 243, 252), Border = Color.FromArgb(205, 226, 238);
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath(); float d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static void Paw(Graphics g, RectangleF r, Color color)
        {
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, r.X + r.Width * .24f, r.Y + r.Height * .47f, r.Width * .52f, r.Height * .43f);
                g.FillEllipse(brush, r.X + r.Width * .04f, r.Y + r.Height * .29f, r.Width * .22f, r.Height * .27f);
                g.FillEllipse(brush, r.X + r.Width * .27f, r.Y + r.Height * .07f, r.Width * .21f, r.Height * .29f);
                g.FillEllipse(brush, r.X + r.Width * .53f, r.Y + r.Height * .07f, r.Width * .21f, r.Height * .29f);
                g.FillEllipse(brush, r.X + r.Width * .76f, r.Y + r.Height * .29f, r.Width * .22f, r.Height * .27f);
            }
        }
        internal static Label Label(string text, float size, bool bold)
        { return new Label { Text = text, AutoSize = true, ForeColor = Ink, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular), BackColor = Color.Transparent }; }
        internal static void Apply(Control root)
        {
            root.ForeColor = Ink;
            foreach (Control c in root.Controls)
            {
                if (c is Label && (c.ForeColor == Color.DimGray || c.ForeColor == Color.Gray)) c.ForeColor = Muted;
                else if (!(c is Label)) c.ForeColor = Ink;
                var text = c as TextBoxBase;
                if (text != null) { text.BackColor = Color.White; text.BorderStyle = BorderStyle.FixedSingle; }
                var combo = c as ComboBox; if (combo != null) { combo.FlatStyle = FlatStyle.Flat; combo.BackColor = Color.White; }
                if (c is CheckBox) ((CheckBox)c).FlatStyle = FlatStyle.Flat;
                ApplyChildren(c);
            }
        }
        private static void ApplyChildren(Control c)
        {
            // Preserve heading and validation colors while styling descendants.
            foreach (Control child in c.Controls)
            {
                if (child is Label && (child.ForeColor == Color.DimGray || child.ForeColor == Color.Gray)) child.ForeColor = Muted;
                if (child is TextBoxBase) { var t = (TextBoxBase)child; t.BackColor = Color.White; t.ForeColor = Ink; t.BorderStyle = BorderStyle.FixedSingle; }
                if (child is ComboBox) { ((ComboBox)child).FlatStyle = FlatStyle.Flat; child.BackColor = Color.White; child.ForeColor = Ink; }
                if (child is CheckBox) { ((CheckBox)child).FlatStyle = FlatStyle.Flat; child.ForeColor = Ink; }
                ApplyChildren(child);
            }
        }
        internal static void Primary(Button button) { var b = button as PetButton; if (b != null) b.Primary = true; button.Invalidate(); }
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        internal static void Window(Form form)
        {
            form.BackColor = Background; form.ForeColor = Ink;
            form.HandleCreated += delegate
            {
                try { int bg = ColorTranslator.ToWin32(Background), ink = ColorTranslator.ToWin32(Ink), corner = 2; DwmSetWindowAttribute(form.Handle, 35, ref bg, 4); DwmSetWindowAttribute(form.Handle, 36, ref ink, 4); DwmSetWindowAttribute(form.Handle, 33, ref corner, 4); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            };
        }
    }
    internal sealed class PetButton : Button
    {
        internal bool Primary;
        private bool hover, down;
        internal PetButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; Cursor = Cursors.Hand; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(Parent == null ? UiTheme.Background : Parent.BackColor);
            if (Width < 3 || Height < 3) return;
            Color fill = !Enabled ? Color.FromArgb(236, 241, 245) : Primary ? (down ? Color.FromArgb(12, 118, 146) : hover ? Color.FromArgb(14, 134, 164) : UiTheme.Accent) : hover ? UiTheme.Soft : Color.White;
            using (var path = UiTheme.Round(new RectangleF(1, 1, Width - 3, Height - 3), 10))
            using (var brush = new SolidBrush(fill)) using (var pen = new Pen(Primary && Enabled ? fill : UiTheme.Border)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(8, 0, Width - 16, Height), !Enabled ? Color.FromArgb(150, 167, 180) : Primary ? Color.White : UiTheme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(5, 5, Width - 10, Height - 10), Primary ? Color.White : UiTheme.Accent, fill);
        }
    }
    internal sealed class PetCard : TableLayoutPanel
    {
        internal PetCard() { DoubleBuffered = true; BackColor = Color.White; Padding = new Padding(18); Margin = new Padding(0); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Background : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width < 3 || Height < 3) return;
            using (var path = UiTheme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 18)) using (var b = new SolidBrush(BackColor)) using (var p = new Pen(UiTheme.Border)) { e.Graphics.FillPath(b, path); e.Graphics.DrawPath(p, path); }
        }
    }
    internal sealed class PetTabs : TabControl
    {
        private PageTransition transition;
        private Bitmap previousPage;
        private int previousIndex;
        internal bool IsTransitionRunning { get { return transition != null && transition.IsRunning; } }
        internal PetTabs() { DrawMode = TabDrawMode.OwnerDrawFixed; ItemSize = new Size(125, 42); SizeMode = TabSizeMode.Fixed; Padding = new Point(12, 7); }
        protected override void OnDeselecting(TabControlCancelEventArgs e)
        {
            base.OnDeselecting(e); if (e.Cancel) return;
            if (transition != null) transition.Dispose();
            if (previousPage != null) previousPage.Dispose();
            previousIndex = e.TabPageIndex; previousPage = PageTransition.Capture(e.TabPage);
        }
        protected override void OnSelecting(TabControlCancelEventArgs e)
        {
            base.OnSelecting(e);
            if (e.Cancel && previousPage != null) { previousPage.Dispose(); previousPage = null; }
        }
        protected override void OnSelectedIndexChanged(EventArgs e)
        {
            base.OnSelectedIndexChanged(e);
            Bitmap before = previousPage; previousPage = null;
            if (SelectedTab == null) { if (before != null) before.Dispose(); return; }
            transition = PageTransition.Begin(SelectedTab, SelectedTab, before, SelectedIndex >= previousIndex ? 1 : -1);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (transition != null) { transition.Dispose(); transition = null; } if (previousPage != null) { previousPage.Dispose(); previousPage = null; } }
            base.Dispose(disposing);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool active = e.Index == SelectedIndex; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(UiTheme.Background)) e.Graphics.FillRectangle(brush, e.Bounds);
            var bounds = Rectangle.Inflate(e.Bounds, -3, -3);
            using (var path = UiTheme.Round(bounds, 9)) using (var brush = new SolidBrush(active ? UiTheme.Soft : UiTheme.Background)) e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, TabPages[e.Index].Text, Font, bounds, active ? UiTheme.Accent : UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (active) using (var pen = new Pen(UiTheme.Accent, 2)) e.Graphics.DrawLine(pen, bounds.Left + 20, bounds.Bottom - 1, bounds.Right - 20, bounds.Bottom - 1);
        }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0xF && IsHandleCreated && Width > 4 && Height > 4)
                using (var g = Graphics.FromHwnd(Handle)) using (var b = new SolidBrush(UiTheme.Background)) using (var p = new Pen(UiTheme.Border, 2))
                {
                    int end = TabCount == 0 ? 0 : GetTabRect(TabCount - 1).Right + 2;
                    if (end < Width) g.FillRectangle(b, end, 0, Width - end, ItemSize.Height + 2);
                    g.DrawRectangle(p, 1, ItemSize.Height + 3, Width - 3, Math.Max(1, Height - ItemSize.Height - 5));
                }
        }
    }
    internal sealed class WelcomeBanner : Control
    {
        internal WelcomeBanner() { DoubleBuffered = true; ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(UiTheme.Background);
            using (var path = UiTheme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 20))
            using (var gradient = new LinearGradientBrush(ClientRectangle, Color.FromArgb(213, 239, 251), Color.FromArgb(247, 252, 255), 0f)) e.Graphics.FillPath(gradient, path);
            bool compact = Height < 100;
            UiTheme.Paw(e.Graphics, compact ? new RectangleF(Width - 80, 9, 48, 50) : new RectangleF(Width - 110, 23, 74, 76), Color.FromArgb(185, 224, 242));
            using (var title = new Font(Font.FontFamily, 21, FontStyle.Bold)) using (var detail = new Font(Font.FontFamily, 10))
            { TextRenderer.DrawText(e.Graphics, "让桌面，多一份陪伴", title, new Rectangle(24, compact ? 12 : 24, Width - 145, 43), UiTheme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); if (!compact) TextRenderer.DrawText(e.Graphics, "选一个喜欢的角色，开启今天的小小陪伴。", detail, new Rectangle(26, 77, Width - 145, 30), UiTheme.Muted, TextFormatFlags.EndEllipsis); }
        }
    }
    internal sealed class PetMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(249, 253, 255); } }
        public override Color ImageMarginGradientBegin { get { return UiTheme.Background; } }
        public override Color ImageMarginGradientMiddle { get { return UiTheme.Background; } }
        public override Color ImageMarginGradientEnd { get { return UiTheme.Background; } }
        public override Color MenuItemSelected { get { return UiTheme.Soft; } }
        public override Color MenuItemBorder { get { return UiTheme.Border; } }
        public override Color MenuBorder { get { return UiTheme.Border; } }
        public override Color SeparatorDark { get { return UiTheme.Border; } }
    }
}
