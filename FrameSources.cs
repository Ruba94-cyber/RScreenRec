using System;
using System.Drawing;
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
