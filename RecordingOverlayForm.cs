using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RScreenRec
{
    public class RecordingOverlayForm : Form
    {
        private readonly Timer blinkTimer;
        private bool blinkOn = true;
        private readonly float dpiScale;

        public RecordingOverlayForm(Rectangle screenBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;

            dpiScale = DpiHelper.GetSystemDpiScale();
            int scaledOffsetY = DpiHelper.ScaleValue(80, dpiScale);
            int scaledOffsetX = DpiHelper.ScaleValue(40, dpiScale);
            int scaledWidth = DpiHelper.ScaleValue(110, dpiScale);
            int scaledHeight = DpiHelper.ScaleValue(34, dpiScale);

            Location = new Point(
                screenBounds.Right - scaledWidth - scaledOffsetX,
                screenBounds.Bottom - scaledHeight - scaledOffsetY
            );
            Size = new Size(scaledWidth, scaledHeight);
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;

            Load += (s, e) =>
            {
                TopMost = true;
                BringToFront();
                ForceOnTop(Handle);
                ExcludeFromCapture(Handle);
            };

            blinkTimer = new Timer { Interval = 520 };
            blinkTimer.Tick += (s, e) =>
            {
                blinkOn = !blinkOn;
                if (blinkOn)
                {
                    Show();
                    ForceOnTop(Handle);
                    Invalidate();
                }
                else
                {
                    Hide();
                }
            };
            Shown += (s, e) => blinkTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            int padding = DpiHelper.ScaleValue(8, dpiScale);
            int cornerRadius = DpiHelper.ScaleValue(10, dpiScale);

            Rectangle pillRect = new Rectangle(0, 0, Width - 1, Height - 1);
            Rectangle shadowRect = pillRect;
            shadowRect.Offset(DpiHelper.ScaleValue(2, dpiScale), DpiHelper.ScaleValue(2, dpiScale));

            using (GraphicsPath shadowPath = CreateRoundedRectangle(shadowRect, cornerRadius))
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using (GraphicsPath pillPath = CreateRoundedRectangle(pillRect, cornerRadius))
            using (LinearGradientBrush pillBrush = new LinearGradientBrush(
                pillRect,
                Color.FromArgb(230, 34, 34, 34),
                Color.FromArgb(230, 24, 24, 24),
                LinearGradientMode.Vertical))
            using (Pen borderPen = new Pen(Color.FromArgb(190, 255, 255, 255), 1f))
            {
                e.Graphics.FillPath(pillBrush, pillPath);
                e.Graphics.DrawPath(borderPen, pillPath);
            }

            int baseDotSize = DpiHelper.ScaleValue(12, dpiScale);
            int dotSize = baseDotSize;
            int dotX = padding;
            int dotY = (Height - dotSize) / 2;

            int glowSize = dotSize + DpiHelper.ScaleValue(8, dpiScale);
            Rectangle glowRect = new Rectangle(
                dotX - (glowSize - dotSize) / 2,
                (Height - glowSize) / 2,
                glowSize,
                glowSize);

            Rectangle dotRect = new Rectangle(dotX, dotY, dotSize, dotSize);
            using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(180, 255, 56, 56)))
            using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(255, 255, 35, 35)))
            {
                e.Graphics.FillEllipse(glowBrush, glowRect);
                e.Graphics.FillEllipse(dotBrush, dotRect);
            }

            int highlightSize = Math.Max(2, dotSize / 3);
            Rectangle highlightRect = new Rectangle(
                dotRect.Left + dotSize / 3,
                dotRect.Top + dotSize / 4,
                highlightSize,
                highlightSize);
            using (SolidBrush highlightBrush = new SolidBrush(Color.FromArgb(210, 255, 210, 210)))
            {
                e.Graphics.FillEllipse(highlightBrush, highlightRect);
            }

            string label = "REC";
            float fontSize = DpiHelper.ScaleValue(11, dpiScale);
            using (Font font = new Font("Segoe UI Semibold", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            {
                SizeF textSize = e.Graphics.MeasureString(label, font);
                float textX = dotRect.Right + DpiHelper.ScaleValue(8, dpiScale);
                float textY = (Height - textSize.Height) / 2;
                e.Graphics.DrawString(label, font, textBrush, textX, textY);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (blinkTimer != null)
                {
                    blinkTimer.Stop();
                    blinkTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        // Keep the window always on top
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const UInt32 SWP_NOMOVE = 0x0002;
        private const UInt32 SWP_NOSIZE = 0x0001;
        private const UInt32 SWP_NOACTIVATE = 0x0010;
        private const UInt32 SWP_SHOWWINDOW = 0x0040;
        private const UInt32 WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        private void ForceOnTop(IntPtr handle)
        {
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private void ExcludeFromCapture(IntPtr handle)
        {
            try
            {
                SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
            }
            catch { }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // Force topmost, transparent, no-activate
                cp.ExStyle |= 0x80 | 0x8 | 0x80000 | 0x20; // WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TRANSPARENT
                return cp;
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
        {
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            Rectangle arc = new Rectangle(rect.Location, size);
            GraphicsPath path = new GraphicsPath();

            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
