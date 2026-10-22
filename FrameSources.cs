using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using SizeInt32 = Windows.Graphics.SizeInt32;

namespace RScreenRec
{
    // An explicit frame-source boundary prevents window capture from silently
    // falling back to desktop pixels. Implementations must provide frames that
    // belong exclusively to their declared source.
    internal interface IFrameSource : IDisposable
    {
        Size FrameSize { get; }
        FrameCaptureStatus CaptureFrame(Graphics destination);
    }

    internal enum FrameCaptureStatus
    {
        Updated,
        Repeated
    }

    internal static class WindowFrameSourceFactory
    {
        internal static IFrameSource Create(IntPtr window, out string backend)
        {
            try
            {
                IFrameSource source = new WindowsGraphicsCaptureFrameSource(window);
                backend = "Windows Graphics Capture";
                return source;
            }
            catch (Exception ex) when (
                ex is COMException ||
                ex is DllNotFoundException ||
                ex is EntryPointNotFoundException ||
                ex is TypeLoadException ||
                ex is PlatformNotSupportedException ||
                ex is NotSupportedException ||
                ex is TimeoutException ||
                ex is Win32Exception)
            {
                Logger.Log("Windows Graphics Capture could not initialize; using PrintWindow compatibility capture.", ex);
                IFrameSource source = new PrintWindowFrameSource(window);
                backend = "PrintWindow compatibility";
                return source;
            }
        }
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

        public FrameCaptureStatus CaptureFrame(Graphics destination)
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
                return FrameCaptureStatus.Repeated;
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
            return FrameCaptureStatus.Updated;
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

    // Window Graphics Capture supplies frames for one HWND and is independent
    // of desktop occlusion. A single latest frame is retained so a slow encoder
    // cannot build an unbounded queue. Frames are copied to CPU memory only when
    // the recorder requests them.
    internal sealed class WindowsGraphicsCaptureFrameSource : IFrameSource
    {
        private const int D3D11CreateDeviceBgraSupport = 0x20;
        private const int D3D11SdkVersion = 7;
        private const int D3DDriverTypeHardware = 1;
        private const int D3DDriverTypeWarp = 5;
        private const int FramePoolBufferCount = 2;
        private const long MaxCapturePixels = 3840L * 2160L * 4L;
        private static readonly Guid DxgiDeviceIid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
        private static readonly Guid GraphicsCaptureItemIid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int D3D11CreateDevice(
            IntPtr adapter,
            int driverType,
            IntPtr software,
            uint flags,
            IntPtr featureLevels,
            uint featureLevelCount,
            uint sdkVersion,
            out IntPtr device,
            out int featureLevel,
            out IntPtr immediateContext);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        }

        [ComImport]
        [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMemoryBufferByteAccess
        {
            void GetBuffer(out IntPtr buffer, out uint capacity);
        }

        private readonly object gate = new object();
        private readonly IntPtr window;
        private readonly Size frameSize;
        private readonly AutoResetEvent frameAvailable = new AutoResetEvent(false);
        private object nativeD3DDevice;
        private IDirect3DDevice captureDevice;
        private GraphicsCaptureItem captureItem;
        private Direct3D11CaptureFramePool framePool;
        private GraphicsCaptureSession captureSession;
        private Direct3D11CaptureFrame latestFrame;
        private Bitmap lastFrameBitmap;
        private SizeInt32 framePoolSize;
        private SizeInt32 pendingSize;
        private bool hasPendingSize;
        private bool sourceClosed;
        private bool disposed;
        private Exception captureFailure;
        private Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object> frameArrivedHandler;
        private Windows.Foundation.TypedEventHandler<GraphicsCaptureItem, object> itemClosedHandler;

        internal WindowsGraphicsCaptureFrameSource(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window))
                throw new ArgumentException("The selected window is not valid.", "window");
            if (IsIconic(window))
                throw new InvalidOperationException("Restore the selected window before recording.");

            this.window = window;
            try
            {
                captureDevice = CreateCaptureDevice(out nativeD3DDevice);
                captureItem = CreateCaptureItem(window);
                SizeInt32 initialSize = captureItem.Size;
                ValidateSize(initialSize.Width, initialSize.Height);
                frameSize = new Size(initialSize.Width, initialSize.Height);
                framePoolSize = initialSize;

                framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                    captureDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    FramePoolBufferCount,
                    framePoolSize);
                frameArrivedHandler = OnFrameArrived;
                itemClosedHandler = OnItemClosed;
                framePool.FrameArrived += frameArrivedHandler;
                captureItem.Closed += itemClosedHandler;
                captureSession = framePool.CreateCaptureSession(captureItem);
                captureSession.StartCapture();

                if (!frameAvailable.WaitOne(TimeSpan.FromSeconds(3)))
                    throw new TimeoutException("Windows Graphics Capture did not deliver its first frame within 3 seconds.");
                ThrowIfCaptureFailed();
                lock (gate)
                {
                    if (latestFrame == null)
                        throw new InvalidOperationException("Windows Graphics Capture started without a usable frame.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Size FrameSize { get { return frameSize; } }

        public FrameCaptureStatus CaptureFrame(Graphics destination)
        {
            if (destination == null)
                throw new ArgumentNullException("destination");
            bool isDisposed;
            bool isClosed;
            lock (gate)
            {
                isDisposed = disposed;
                isClosed = sourceClosed;
            }
            if (isDisposed)
                throw new ObjectDisposedException("WindowsGraphicsCaptureFrameSource");
            if (isClosed || !IsWindow(window))
                throw new InvalidOperationException("The selected window has been closed; window capture stopped.");
            ThrowIfCaptureFailed();
            RecreateFramePoolIfNeeded();

            Direct3D11CaptureFrame frame = null;
            bool minimized = IsIconic(window);
            lock (gate)
            {
                if (latestFrame != null)
                {
                    frame = latestFrame;
                    latestFrame = null;
                }
            }

            if (minimized)
            {
                if (frame != null)
                {
                    if (lastFrameBitmap == null)
                    {
                        SizeInt32 contentSize = frame.ContentSize;
                        ValidateSize(contentSize.Width, contentSize.Height);
                        using (frame)
                        using (SoftwareBitmap softwareBitmap = CopySurfaceToSoftwareBitmap(frame))
                        {
                            CopySoftwareBitmapToLastFrame(softwareBitmap);
                        }
                        QueueFramePoolResize(contentSize);
                        RecreateFramePoolIfNeeded();
                        RenderLastFrame(destination);
                        return FrameCaptureStatus.Updated;
                    }
                    frame.Dispose();
                }
                if (lastFrameBitmap == null)
                    throw new InvalidOperationException("The source was minimized before a frame was captured.");
                RenderLastFrame(destination);
                return FrameCaptureStatus.Repeated;
            }

            if (frame != null)
            {
                SizeInt32 contentSize = frame.ContentSize;
                ValidateSize(contentSize.Width, contentSize.Height);
                if (lastFrameBitmap != null && IsFramePoolSizeDifferent(contentSize))
                {
                    // A frame can report the new content size while its surface
                    // still belongs to the old pool. Keep the last complete image
                    // for this tick, recreate the pool, and resume on a fresh frame.
                    frame.Dispose();
                    QueueFramePoolResize(contentSize);
                    RecreateFramePoolIfNeeded();
                    RenderLastFrame(destination);
                    return FrameCaptureStatus.Repeated;
                }
                using (frame)
                using (SoftwareBitmap softwareBitmap = CopySurfaceToSoftwareBitmap(frame))
                {
                    CopySoftwareBitmapToLastFrame(softwareBitmap);
                }
                QueueFramePoolResize(contentSize);
                RecreateFramePoolIfNeeded();
                RenderLastFrame(destination);
                return FrameCaptureStatus.Updated;
            }

            if (lastFrameBitmap == null)
            {
                if (!frameAvailable.WaitOne(TimeSpan.FromSeconds(2)))
                    throw new TimeoutException("Windows Graphics Capture stopped delivering frames during startup.");
                ThrowIfCaptureFailed();
                lock (gate)
                {
                    frame = latestFrame;
                    latestFrame = null;
                }
                if (frame == null)
                    throw new InvalidOperationException("Windows Graphics Capture did not return a frame.");
                SizeInt32 contentSize = frame.ContentSize;
                ValidateSize(contentSize.Width, contentSize.Height);
                using (frame)
                using (SoftwareBitmap softwareBitmap = CopySurfaceToSoftwareBitmap(frame))
                {
                    CopySoftwareBitmapToLastFrame(softwareBitmap);
                }
                QueueFramePoolResize(contentSize);
                RecreateFramePoolIfNeeded();
                RenderLastFrame(destination);
                return FrameCaptureStatus.Updated;
            }

            RenderLastFrame(destination);
            return FrameCaptureStatus.Repeated;
        }

        private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                lock (gate)
                {
                    if (disposed)
                        return;

                    Direct3D11CaptureFrame frame;
                    while ((frame = sender.TryGetNextFrame()) != null)
                    {
                        if (latestFrame != null)
                            latestFrame.Dispose();
                        latestFrame = frame;
                    }
                    frameAvailable.Set();
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    if (!disposed)
                    {
                        captureFailure = ex;
                        frameAvailable.Set();
                    }
                }
            }
        }

        private void QueueFramePoolResize(SizeInt32 size)
        {
            if (size.Width <= 0 || size.Height <= 0)
                return;
            ValidateSize(size.Width, size.Height);
            lock (gate)
            {
                if (disposed)
                    return;
                if (size.Width != framePoolSize.Width || size.Height != framePoolSize.Height)
                {
                    pendingSize = size;
                    hasPendingSize = true;
                }
            }
        }

        private void OnItemClosed(GraphicsCaptureItem sender, object args)
        {
            lock (gate)
            {
                if (disposed)
                    return;
                sourceClosed = true;
                frameAvailable.Set();
            }
        }

        private bool IsFramePoolSizeDifferent(SizeInt32 size)
        {
            lock (gate)
                return framePoolSize.Width != size.Width || framePoolSize.Height != size.Height;
        }

        private void RecreateFramePoolIfNeeded()
        {
            lock (gate)
            {
                if (!hasPendingSize || disposed)
                    return;

                if (pendingSize.Width <= 0 || pendingSize.Height <= 0)
                    return;
                ValidateSize(pendingSize.Width, pendingSize.Height);
                if (pendingSize.Width != framePoolSize.Width || pendingSize.Height != framePoolSize.Height)
                {
                    framePool.Recreate(
                        captureDevice,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        FramePoolBufferCount,
                        pendingSize);
                    framePoolSize = pendingSize;
                    if (latestFrame != null)
                    {
                        latestFrame.Dispose();
                        latestFrame = null;
                    }
                }
                hasPendingSize = false;
            }
        }

        private static SoftwareBitmap CopySurfaceToSoftwareBitmap(Direct3D11CaptureFrame frame)
        {
            var operation = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
            Task<SoftwareBitmap> task = operation.AsTask();
            if (!task.Wait(TimeSpan.FromSeconds(3)))
            {
                operation.Cancel();
                task.Wait(TimeSpan.FromSeconds(1));
                throw new TimeoutException("Timed out copying a Windows Graphics Capture frame to system memory.");
            }
            return task.GetAwaiter().GetResult();
        }

        private void CopySoftwareBitmapToLastFrame(SoftwareBitmap softwareBitmap)
        {
            int width = softwareBitmap.PixelWidth;
            int height = softwareBitmap.PixelHeight;
            ValidateSize(width, height);

            Bitmap replacement = null;
            if (lastFrameBitmap == null || lastFrameBitmap.Width != width || lastFrameBitmap.Height != height)
                replacement = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Bitmap target = replacement ?? lastFrameBitmap;
            BitmapData bitmapData = null;
            bool copySucceeded = false;
            try
            {
                bitmapData = target.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);
                using (BitmapBuffer bitmapBuffer = softwareBitmap.LockBuffer(BitmapBufferAccessMode.Read))
                using (var reference = bitmapBuffer.CreateReference())
                {
                    BitmapPlaneDescription plane = bitmapBuffer.GetPlaneDescription(0);
                    int rowBytes = checked(width * 4);
                    if (plane.Width < width || plane.Height < height || plane.Stride < rowBytes || plane.StartIndex < 0)
                        throw new InvalidOperationException("Windows Graphics Capture returned an invalid pixel plane.");

                    IntPtr source;
                    uint capacity;
                    ((IMemoryBufferByteAccess)reference).GetBuffer(out source, out capacity);
                    long lastByte = (long)plane.StartIndex + (long)(height - 1) * plane.Stride + rowBytes;
                    if (source == IntPtr.Zero || lastByte > capacity)
                        throw new InvalidOperationException("Windows Graphics Capture returned a truncated pixel buffer.");

                    unsafe
                    {
                        byte* sourceBase = (byte*)source.ToPointer() + plane.StartIndex;
                        byte* destinationBase = (byte*)bitmapData.Scan0.ToPointer();
                        for (int y = 0; y < height; y++)
                        {
                            byte* sourceRow = sourceBase + (long)y * plane.Stride;
                            byte* destinationRow = destinationBase + (long)y * bitmapData.Stride;
                            Buffer.MemoryCopy(sourceRow, destinationRow, Math.Abs(bitmapData.Stride), rowBytes);
                        }
                    }
                }
                copySucceeded = true;
            }
            finally
            {
                try
                {
                    if (bitmapData != null)
                        target.UnlockBits(bitmapData);
                }
                finally
                {
                    if (!copySucceeded && replacement != null)
                        replacement.Dispose();
                }
            }

            if (replacement != null)
            {
                Bitmap previous = lastFrameBitmap;
                lastFrameBitmap = replacement;
                if (previous != null)
                    previous.Dispose();
            }
        }

        private void RenderLastFrame(Graphics destination)
        {
            destination.Clear(Color.Black);
            float scale = Math.Min(frameSize.Width / (float)lastFrameBitmap.Width,
                frameSize.Height / (float)lastFrameBitmap.Height);
            int width = Math.Max(1, (int)Math.Round(lastFrameBitmap.Width * scale));
            int height = Math.Max(1, (int)Math.Round(lastFrameBitmap.Height * scale));
            int x = (frameSize.Width - width) / 2;
            int y = (frameSize.Height - height) / 2;
            destination.DrawImage(lastFrameBitmap,
                new Rectangle(x, y, width, height),
                new Rectangle(0, 0, lastFrameBitmap.Width, lastFrameBitmap.Height),
                GraphicsUnit.Pixel);
        }

        private void ThrowIfCaptureFailed()
        {
            Exception failure;
            lock (gate)
                failure = captureFailure;
            if (failure != null)
                throw new InvalidOperationException("Windows Graphics Capture failed while receiving frames.", failure);
        }

        private static void ValidateSize(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("The selected window has invalid capture dimensions.");
            if ((long)width * height > MaxCapturePixels)
                throw new InvalidOperationException("The selected window exceeds the supported capture size limit.");
        }

        private static GraphicsCaptureItem CreateCaptureItem(IntPtr window)
        {
            object factory = WindowsRuntimeMarshal.GetActivationFactory(typeof(GraphicsCaptureItem));
            IGraphicsCaptureItemInterop interop = (IGraphicsCaptureItemInterop)factory;
            Guid itemIid = GraphicsCaptureItemIid;
            IntPtr itemPointer = interop.CreateForWindow(window, ref itemIid);
            if (itemPointer == IntPtr.Zero)
                throw new COMException("Windows Graphics Capture returned no capture item.");
            try
            {
                return (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPointer);
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }

        private static IDirect3DDevice CreateCaptureDevice(out object nativeDevice)
        {
            nativeDevice = null;
            IntPtr devicePointer;
            IntPtr contextPointer;
            int featureLevel;
            int hr = D3D11CreateDevice(IntPtr.Zero, D3DDriverTypeHardware, IntPtr.Zero,
                D3D11CreateDeviceBgraSupport, IntPtr.Zero, 0, D3D11SdkVersion,
                out devicePointer, out featureLevel, out contextPointer);
            if (hr < 0)
            {
                if (devicePointer != IntPtr.Zero)
                    Marshal.Release(devicePointer);
                if (contextPointer != IntPtr.Zero)
                    Marshal.Release(contextPointer);
                hr = D3D11CreateDevice(IntPtr.Zero, D3DDriverTypeWarp, IntPtr.Zero,
                    D3D11CreateDeviceBgraSupport, IntPtr.Zero, 0, D3D11SdkVersion,
                    out devicePointer, out featureLevel, out contextPointer);
            }
            if (hr < 0)
            {
                if (devicePointer != IntPtr.Zero)
                    Marshal.Release(devicePointer);
                if (contextPointer != IntPtr.Zero)
                    Marshal.Release(contextPointer);
                Marshal.ThrowExceptionForHR(hr);
            }

            try
            {
                nativeDevice = Marshal.GetObjectForIUnknown(devicePointer);
            }
            finally
            {
                Marshal.Release(devicePointer);
                if (contextPointer != IntPtr.Zero)
                    Marshal.Release(contextPointer);
            }

            IntPtr unknown = Marshal.GetIUnknownForObject(nativeDevice);
            IntPtr dxgiDevice = IntPtr.Zero;
            try
            {
                Guid dxgiIid = DxgiDeviceIid;
                hr = Marshal.QueryInterface(unknown, ref dxgiIid, out dxgiDevice);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
            finally
            {
                Marshal.Release(unknown);
            }

            IntPtr captureDevicePointer = IntPtr.Zero;
            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out captureDevicePointer);
                if (hr < 0)
                    Marshal.ThrowExceptionForHR(hr);
                return (IDirect3DDevice)Marshal.GetObjectForIUnknown(captureDevicePointer);
            }
            finally
            {
                Marshal.Release(dxgiDevice);
                if (captureDevicePointer != IntPtr.Zero)
                    Marshal.Release(captureDevicePointer);
            }
        }

        public void Dispose()
        {
            if (framePool != null && frameArrivedHandler != null)
            {
                try { framePool.FrameArrived -= frameArrivedHandler; }
                catch { }
            }
            if (captureItem != null)
            {
                try { if (itemClosedHandler != null) captureItem.Closed -= itemClosedHandler; }
                catch { }
            }

            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                if (latestFrame != null)
                {
                    latestFrame.Dispose();
                    latestFrame = null;
                }
                if (captureSession != null)
                {
                    try { ((IDisposable)captureSession).Dispose(); }
                    catch { }
                    ReleaseComObject(captureSession);
                    captureSession = null;
                }
                if (framePool != null)
                {
                    try { ((IDisposable)framePool).Dispose(); }
                    catch { }
                    ReleaseComObject(framePool);
                    framePool = null;
                }
                if (captureItem != null)
                {
                    ReleaseComObject(captureItem);
                    captureItem = null;
                }
                if (captureDevice != null)
                {
                    ReleaseComObject(captureDevice);
                    captureDevice = null;
                }
                if (nativeD3DDevice != null)
                {
                    ReleaseComObject(nativeD3DDevice);
                    nativeD3DDevice = null;
                }
                if (lastFrameBitmap != null)
                {
                    lastFrameBitmap.Dispose();
                    lastFrameBitmap = null;
                }
            }

            frameAvailable.Dispose();
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try { Marshal.FinalReleaseComObject(value); }
                catch { }
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

        public FrameCaptureStatus CaptureFrame(Graphics destination)
        {
            if (destination == null)
                throw new ArgumentNullException("destination");
            destination.CopyFromScreen(
                captureBounds.Location,
                Point.Empty,
                captureBounds.Size,
                CopyPixelOperation.SourceCopy);
            return FrameCaptureStatus.Updated;
        }

        public void Dispose() { }
    }
}
