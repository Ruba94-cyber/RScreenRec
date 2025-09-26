using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenshotFlash
{
    public class RecordingOverlayForm : Form
    {
        private Timer blinkTimer;
        private bool isVisible = true;

        public RecordingOverlayForm(Rectangle screenBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;

            // Scegli la posizione (in basso a destra, offset 70)
            int offsetY = 70;
            Location = new Point(screenBounds.Right - 30, screenBounds.Bottom - offsetY);
            Size = new Size(22, 22);
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

                // Esterno nero
                var blackRect = new Rectangle(0, 0, 20, 20);
                using (SolidBrush blackBrush = new SolidBrush(Color.Black))
                {
                    e.Graphics.FillEllipse(blackBrush, blackRect);
                }

                // Intermedio bianco
                var whiteRect = new Rectangle(2, 2, 16, 16);
                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                {
                    e.Graphics.FillEllipse(whiteBrush, whiteRect);
                }

                // Interno magenta (alternativo per evitare trasparenza)
                var centerRect = new Rectangle(4, 4, 12, 12);
                using (SolidBrush fillBrush = new SolidBrush(Color.Red)) // ← diverso da TransparencyKey
                {
                    e.Graphics.FillEllipse(fillBrush, centerRect);
                }
            }
        }


        // Forziamo la finestra sempre on top
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
