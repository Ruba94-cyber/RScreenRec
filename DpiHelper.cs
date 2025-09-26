using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ScreenshotFlash
{
    public static class DpiHelper
    {
        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        private const int DEFAULT_DPI = 96;

        public static float GetSystemDpiScale()
        {
            try
            {
                IntPtr desktop = GetDesktopWindow();
                int dpi = GetDpiForWindow(desktop);
                return (float)dpi / DEFAULT_DPI;
            }
            catch
            {
                // Fallback usando Graphics
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / DEFAULT_DPI;
                }
            }
        }

        public static float GetScreenDpiScale(Screen screen)
        {
            try
            {
                // Per Windows 10+, ogni monitor può avere DPI diverso
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    return g.DpiX / DEFAULT_DPI;
                }
            }
            catch
            {
                return 1.0f;
            }
        }

        public static int ScaleValue(int value, float scale)
        {
            return (int)Math.Round(value * scale);
        }

        public static Size ScaleSize(Size size, float scale)
        {
            return new Size(
                ScaleValue(size.Width, scale),
                ScaleValue(size.Height, scale)
            );
        }

        public static Point ScalePoint(Point point, float scale)
        {
            return new Point(
                ScaleValue(point.X, scale),
                ScaleValue(point.Y, scale)
            );
        }

        public static Rectangle ScaleRectangle(Rectangle rect, float scale)
        {
            return new Rectangle(
                ScaleValue(rect.X, scale),
                ScaleValue(rect.Y, scale),
                ScaleValue(rect.Width, scale),
                ScaleValue(rect.Height, scale)
            );
        }
    }
}