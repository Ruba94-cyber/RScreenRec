using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RScreenRec
{
    public class RecordingOverlayForm : Form
    {
        private Timer blinkTimer;
        private bool isVisible = true;

        public RecordingOverlayForm(Rectangle screenBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;

            // DPI-aware positioning and sizing
            float dpiScale = DpiHelper.GetSystemDpiScale();
            int scaledOffsetY = DpiHelper.ScaleValue(70, dpiScale);
            int scaledOffsetX = DpiHelper.ScaleValue(30, dpiScale);
            int scaledSize = DpiHelper.ScaleValue(22, dpiScale);

            Location = new Point(
                screenBounds.Right - scaledOffsetX,
                screenBounds.Bottom - scaledOffsetY
            );
            Size = new Size(scaledSize, scaledSize);
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
            };

            blinkTimer = new Timer { Interval = 500 };
            blinkTimer.Tick += (s, e) =>
            {
                isVisible = !isVisible;
                Invalidate();
            };
            blinkTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (isVisible)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                float dpiScale = DpiHelper.GetSystemDpiScale();
                int scaledSize = DpiHelper.ScaleValue(20, dpiScale);
                int scaledBorder1 = DpiHelper.ScaleValue(2, dpiScale);
                int scaledBorder2 = DpiHelper.ScaleValue(4, dpiScale);

                // Outer black ring
                var blackRect = new Rectangle(0, 0, scaledSize, scaledSize);
                using (SolidBrush blackBrush = new SolidBrush(Color.Black))
                {
                    e.Graphics.FillEllipse(blackBrush, blackRect);
                }

                // Middle white ring
                var whiteRect = new Rectangle(
                    scaledBorder1, scaledBorder1,
                    scaledSize - scaledBorder1 * 2, scaledSize - scaledBorder1 * 2
                );
                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                {
                    e.Graphics.FillEllipse(whiteBrush, whiteRect);
                }

                // Inner red circle
                var centerRect = new Rectangle(
                    scaledBorder2, scaledBorder2,
                    scaledSize - scaledBorder2 * 2, scaledSize - scaledBorder2 * 2
                );
                using (SolidBrush fillBrush = new SolidBrush(Color.Red))
                {
                    e.Graphics.FillEllipse(fillBrush, centerRect);
                }
            }
        }


        // Keep the window always on top
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const UInt32 SWP_NOMOVE = 0x0002;
        private const UInt32 SWP_NOSIZE = 0x0001;
        private const UInt32 SWP_NOACTIVATE = 0x0010;
        private const UInt32 SWP_SHOWWINDOW = 0x0040;

        private void ForceOnTop(IntPtr handle)
        {
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
    }
}
