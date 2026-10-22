using System;
using System.Drawing;
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
        private const uint RenderFullContent = 2;

        internal PrintWindowFrameSource(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window))
                throw new ArgumentException("The selected window is not valid.", "window");
            if (IsIconic(window))
                throw new InvalidOperationException("Restore the selected window before recording.");
            NativeRect rect;
            if (!GetWindowRect(window, out rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                throw new InvalidOperationException("Cannot determine the selected window dimensions.");
            this.window = window;
            size = new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        public Size FrameSize { get { return size; } }

        public void CaptureFrame(Graphics destination)
        {
            if (!IsWindow(window))
                throw new InvalidOperationException("The selected window has been closed.");
            if (IsIconic(window))
                throw new InvalidOperationException("The selected window was minimized; capture stopped.");

            NativeRect rect;
            if (!GetWindowRect(window, out rect))
                throw new InvalidOperationException("Cannot read the selected window bounds.");

            IntPtr hdc = destination.GetHdc();
            try
            {
                if (!PrintWindow(window, hdc, RenderFullContent))
                    throw new InvalidOperationException("The selected window does not support PrintWindow capture.");
            }
            finally
            {
                destination.ReleaseHdc(hdc);
            }
        }

        public void Dispose() { }
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
