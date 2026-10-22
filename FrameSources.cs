using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RScreenRec
{
    // An explicit frame-source boundary prevents window capture from silently
    // falling back to desktop pixels. Implementations must provide frames that
    // belong exclusively to their declared source.
    internal interface IFrameSource : IDisposable
    {
        Size FrameSize { get; }
        void CaptureFrame(Graphics destination);
    }

    // Compatibility-only window source. PrintWindow requests the target to render
    // itself; it never samples the desktop, but some GPU/protected windows may
    // refuse to render. Windows Graphics Capture is still the preferred backend.
    internal sealed class PrintWindowFrameSource : IFrameSource
    {
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        private readonly IntPtr window;
        private readonly Size size;
        private Bitmap windowBitmap;
        private const uint RenderFullContent = 2;
        private const long MaxCapturePixels = 3840L * 2160L * 4L;

        internal PrintWindowFrameSource(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window))
                throw new ArgumentException("The selected window is not valid.", "window");
            if (IsIconic(window))
                throw new InvalidOperationException("Restore the selected window before recording.");
            NativeRect rect;
            if (!GetWindowRect(window, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                throw new InvalidOperationException("Cannot determine the selected window dimensions.");
            if ((rect.Right - (long)rect.Left) * (rect.Bottom - (long)rect.Top) > MaxCapturePixels)
                throw new InvalidOperationException("The selected window is too large for compatibility capture.");
            this.window = window;
            size = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
            windowBitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        }

        public Size FrameSize { get { return size; } }

        public void CaptureFrame(Graphics destination)
        {
            if (destination == null)
                throw new ArgumentNullException("destination");
            if (!IsWindow(window))
                throw new InvalidOperationException("The selected window has been closed.");
            if (IsIconic(window))
            {
                // Windows may stop rendering minimized windows. Preserve the
                // last frame without ever substituting unrelated desktop pixels.
                // The MP4 timeline continues; these are held frames, not live
                // updates from the minimized application.
                return;
            }

            NativeRect rect;
            if (!GetWindowRect(window, out rect))
                throw new InvalidOperationException("Cannot read the selected window bounds.");

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("The selected window has invalid dimensions.");
            if (width * (long)height > MaxCapturePixels)
                throw new InvalidOperationException("The selected window has grown beyond the capture size limit.");

            // PrintWindow draws at the target's current dimensions. Capture to an
            // appropriately sized off-screen bitmap, then fit it inside the
            // original, fixed-size video frame. Never read desktop pixels.
            if (windowBitmap.Width != width || windowBitmap.Height != height)
            {
                Bitmap resized = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                Bitmap previous = windowBitmap;
                windowBitmap = resized;
                previous.Dispose();
            }

            using (Graphics offscreen = Graphics.FromImage(windowBitmap))
            {
                offscreen.Clear(Color.Black);
                IntPtr hdc = offscreen.GetHdc();
                try
                {
                    if (!PrintWindow(window, hdc, RenderFullContent))
                        throw new InvalidOperationException("The selected window does not support PrintWindow capture.");
                }
                finally
                {
                    offscreen.ReleaseHdc(hdc);
                }
            }

            destination.Clear(Color.Black);
            float scale = Math.Min(size.Width / (float)width, size.Height / (float)height);
            int fittedWidth = Math.Max(1, (int)Math.Round(width * scale));
            int fittedHeight = Math.Max(1, (int)Math.Round(height * scale));
            int x = (size.Width - fittedWidth) / 2;
            int y = (size.Height - fittedHeight) / 2;
            destination.DrawImage(windowBitmap,
                new Rectangle(x, y, fittedWidth, fittedHeight),
                new Rectangle(0, 0, width, height), GraphicsUnit.Pixel);
        }

        public void Dispose()
        {
            if (windowBitmap != null)
            {
                windowBitmap.Dispose();
                windowBitmap = null;
            }
        }
    }

    internal sealed class DesktopFrameSource : IFrameSource
    {
        private readonly Rectangle captureBounds;

        internal DesktopFrameSource(Rectangle captureBounds)
        {
            if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
                throw new ArgumentException("Capture bounds must be positive.", "captureBounds");
            this.captureBounds = captureBounds;
        }

        public Size FrameSize { get { return captureBounds.Size; } }

        public void CaptureFrame(Graphics destination)
        {
            if (destination == null)
                throw new ArgumentNullException("destination");
            destination.CopyFromScreen(
                captureBounds.Location,
                Point.Empty,
                captureBounds.Size,
                CopyPixelOperation.SourceCopy);
        }

        public void Dispose() { }
    }
}
