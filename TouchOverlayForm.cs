using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenshotFlash
{
    public class TouchOverlayForm : Form
    {
        [DllImport("user32.dll")]
        static extern bool RegisterTouchWindow(IntPtr hWnd, uint ulFlags);

        [DllImport("user32.dll")]
        static extern bool GetTouchInputInfo(IntPtr hTouchInput, int cInputs, [In, Out] TOUCHINPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        static extern void CloseTouchInputHandle(IntPtr lParam);

        private const int WM_TOUCH = 0x0240;
        private const int TOUCHEVENTF_DOWN = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct TOUCHINPUT
        {
            public int x;
            public int y;
            public IntPtr hSource;
            public uint dwID;
            public uint dwFlags;
            public uint dwMask;
            public uint time;
            public IntPtr dwExtraInfo;
            public uint cxContact;
            public uint cyContact;
        }

        private readonly List<Point> touchPoints = new List<Point>();
        private readonly Timer cleanupTimer;

        public TouchOverlayForm(Rectangle screenBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screenBounds;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
            Opacity = 0.5;

            Load += (s, e) =>
            {
                RegisterTouchWindow(Handle, 0);
                BringToFront();
            };

            // Timer: ogni 500ms pulisce e ridisegna
            cleanupTimer = new Timer { Interval = 500 };
            cleanupTimer.Tick += (s, e) =>
            {
                if (touchPoints.Count > 0)
                {
                    touchPoints.Clear();
                    Invalidate();
                }
            };
            cleanupTimer.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_TOUCH)
            {
                int inputCount = (m.WParam.ToInt32() & 0xFFFF);
                TOUCHINPUT[] inputs = new TOUCHINPUT[inputCount];
                if (GetTouchInputInfo(m.LParam, inputCount, inputs, Marshal.SizeOf(typeof(TOUCHINPUT))))
                {
                    foreach (var ti in inputs)
                    {
                        if ((ti.dwFlags & TOUCHEVENTF_DOWN) != 0)
                        {
                            Point pt = new Point(ti.x / 100, ti.y / 100);
                            touchPoints.Add(pt);
                        }
                    }
                }
                CloseTouchInputHandle(m.LParam);
                Invalidate();
            }

            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            foreach (var pt in touchPoints)
            {
                Rectangle r = new Rectangle(pt.X - 20, pt.Y - 20, 40, 40);
                using (Pen border = new Pen(Color.White, 3))
                using (Brush fill = new SolidBrush(Color.Red))  // Rosso
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.FillEllipse(fill, r);
                    e.Graphics.DrawEllipse(border, r);
                }
            }
        }
    }
}
