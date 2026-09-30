using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace InventoryKamera
{
    public class ToggleSwitchControl : Control
    {
        private bool _checked;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public string OffText { get; set; } = "Fast";
        public string OnText  { get; set; } = "Slow";

        public ToggleSwitchControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size    = new Size(100, 28);
            Cursor  = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g  = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = Width, h = Height;
            int radius   = h / 2;
            int thumbPad = 3;
            int thumbDia = h - thumbPad * 2;

            // Track colours
            Color trackOn  = Color.FromArgb(0, 120, 212);
            Color trackOff = Color.FromArgb(160, 160, 160);
            Color trackCol = _checked ? trackOff : trackOn;

            // Draw track (pill)
            using (var brush = new SolidBrush(trackCol))
            using (var path  = RoundedRect(new Rectangle(0, 0, w - 1, h - 1), radius))
                g.FillPath(brush, path);

            // Thumb X position
            int thumbX = _checked ? w - thumbPad - thumbDia : thumbPad;
            var thumbRect = new Rectangle(thumbX, thumbPad, thumbDia, thumbDia);

            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, thumbRect);

            // Label text
            string label = _checked ? OnText : OffText;
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var textBrush = new SolidBrush(Color.White))
            {
                // text on the opposite side of the thumb
                int textX  = _checked ? 0 : thumbDia + thumbPad * 2;
                int textW  = w - thumbDia - thumbPad * 2;
                var textRect = new Rectangle(textX, 0, textW, h);
                g.DrawString(label, Font, textBrush, textRect, sf);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
