using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RScreenRec
{
    public class ScreenRecorder
    {
        private const int FramesPerSecond = 30;
        private const int MaxCatchUpFramesPerCycle = FramesPerSecond * 2;
        private const int LowResolutionBitrate = 250000;
        private const int FullHdBitrate = 400000;
        private const int QuadHdBitrate = 900000;
        private const int UltraHdBitrate = 2200000;
        private readonly object recordingLock = new object();
        private Thread recordingThread;
        private Stopwatch recordingStopwatch;
        private ManualResetEventSlim firstFrameWritten;
        private Rectangle bounds;
        private byte[] frameBuffer;
        private bool isRecording;
        private string outputPath;
        private int encodedWidth;
        private int encodedHeight;
        private int encodedPaddingRight;
        private int encodedPaddingBottom;
        private int framesWritten;
        private Exception recordingError;

        public void StartRecording(Rectangle screenBounds, string outputPath)
        {
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
                throw new ArgumentException("Screen bounds must have a positive size.", "screenBounds");
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", "outputPath");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            Stopwatch startupStopwatch = Stopwatch.StartNew();
            lock (recordingLock)
            {
                if (isRecording)
                    throw new InvalidOperationException("A recording session is already in progress.");

                bounds = screenBounds;
                this.outputPath = outputPath;
                encodedWidth = MakeH264CompatibleDimension(screenBounds.Width);
                encodedHeight = MakeH264CompatibleDimension(screenBounds.Height);
                encodedPaddingRight = encodedWidth - screenBounds.Width;
                encodedPaddingBottom = encodedHeight - screenBounds.Height;
                frameBuffer = new byte[GetFrameBufferSize(encodedWidth, encodedHeight)];
                firstFrameWritten = new ManualResetEventSlim(false);
                framesWritten = 0;
                recordingError = null;
                recordingStopwatch = Stopwatch.StartNew();
                isRecording = true;
            }

            recordingThread = new Thread(RecordLoop);
            recordingThread.IsBackground = true;
            recordingThread.Name = "ScreenCaptureMediaFoundation";
            recordingThread.Priority = ThreadPriority.AboveNormal;
            recordingThread.Start();

            int selectedBitrate = SelectBitrate(encodedWidth, encodedHeight);
            Logger.Log(string.Format("Recording dispatched in {0} ms. Output={1}, CaptureBounds={2}x{3}@({4},{5}), EncodedSize={6}x{7}, PaddingRight={8}, PaddingBottom={9}, FPS={10}, Bitrate={11}, TargetMBPerMinute={12:F1}",
                startupStopwatch.ElapsedMilliseconds,
                outputPath,
                screenBounds.Width,
                screenBounds.Height,
                screenBounds.X,
                screenBounds.Y,
                encodedWidth,
                encodedHeight,
                encodedPaddingRight,
                encodedPaddingBottom,
                FramesPerSecond,
                selectedBitrate,
                EstimateMegabytesPerMinute(selectedBitrate)));
        }

        public void StopRecording()
        {
            Thread captureThread;
            Stopwatch stopwatch;
            ManualResetEventSlim firstFrameEvent;
            string path;

            lock (recordingLock)
            {
                captureThread = recordingThread;
                stopwatch = recordingStopwatch;
                firstFrameEvent = firstFrameWritten;
                path = outputPath;
            }

            if (firstFrameEvent != null && !firstFrameEvent.IsSet)
                firstFrameEvent.Wait(750);

            lock (recordingLock)
            {
                isRecording = false;
                recordingThread = null;
                recordingStopwatch = null;
                firstFrameWritten = null;
            }

            TimeSpan requestedDuration = stopwatch != null ? stopwatch.Elapsed : TimeSpan.Zero;
            if (stopwatch != null)
                stopwatch.Stop();

            if (captureThread != null && captureThread.IsAlive && !captureThread.Join(90000))
            {
                Logger.Log("Capture thread did not finalize within timeout.");
            }

            Exception error;
            int written;
            lock (recordingLock)
            {
                error = recordingError;
                written = framesWritten;
                frameBuffer = null;
            }

            if (firstFrameEvent != null)
                firstFrameEvent.Dispose();

            long size = File.Exists(path) ? new FileInfo(path).Length : 0;
            Logger.Log(string.Format("Recording stopped. RequestedDuration={0}, Frames={1}, Size={2}, Output={3}",
                requestedDuration, written, size, path));

            if (error != null)
                throw new InvalidOperationException("Recording failed while finalizing.", error);
        }

        public bool IsRecording
        {
            get
            {
                lock (recordingLock)
                {
                    return isRecording;
                }
            }
        }

        private void RecordLoop()
        {
            Stopwatch stopwatch;
            Rectangle captureBounds;
            string path;
            int outputWidth;
            int outputHeight;

            lock (recordingLock)
            {
                stopwatch = recordingStopwatch;
                captureBounds = bounds;
                path = outputPath;
                outputWidth = encodedWidth;
                outputHeight = encodedHeight;
            }

            if (stopwatch == null)
                return;

            long frameIntervalTicks = Math.Max(1, (long)Math.Round(Stopwatch.Frequency / (double)FramesPerSecond));
            long frameIndex = 0;
            float dpiScale = DpiHelper.GetSystemDpiScale();
            bool firstFrameSignaled = false;

            try
            {
                using (var writer = new MediaFoundationMp4Writer(path, outputWidth, outputHeight, FramesPerSecond, SelectBitrate(outputWidth, outputHeight)))
                using (Bitmap bitmap = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    while (IsRecording)
                    {
                        SleepUntil(stopwatch, frameIndex * frameIntervalTicks);
                        if (!IsRecording)
                            break;

                        graphics.CopyFromScreen(captureBounds.X, captureBounds.Y, 0, 0, captureBounds.Size, CopyPixelOperation.SourceCopy);
                        DrawMousePointer(graphics, captureBounds, dpiScale);
                        DrawTouchPointers(graphics, dpiScale);

                        byte[] frame = BitmapToRgb32TopDownPadded(bitmap, outputWidth, outputHeight);
                        WriteFrame(writer, frame, frame.Length, frameIndex, ref firstFrameSignaled);
                        frameIndex++;

                        long expectedFrameCount = Math.Max(frameIndex, stopwatch.ElapsedTicks / frameIntervalTicks);
                        int catchUpFrames = 0;
                        while (frameIndex < expectedFrameCount &&
                               catchUpFrames < MaxCatchUpFramesPerCycle &&
                               IsRecording)
                        {
                            WriteFrame(writer, frame, frame.Length, frameIndex, ref firstFrameSignaled);
                            frameIndex++;
                            catchUpFrames++;
                        }

                        if (frameIndex < expectedFrameCount)
                            frameIndex = expectedFrameCount;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (recordingLock)
                {
                    recordingError = ex;
                    isRecording = false;
                }
                Logger.Log("Capture loop stopped by error.", ex);
            }
            finally
            {
                lock (recordingLock)
                {
                    isRecording = false;
                }
                Logger.Log("Capture loop stopped.");
            }
        }

        private void WriteFrame(MediaFoundationMp4Writer writer, byte[] frame, int length, long frameIndex, ref bool firstFrameSignaled)
        {
            writer.WriteFrame(frame, length, frameIndex);
            lock (recordingLock)
            {
                framesWritten++;
            }

            if (!firstFrameSignaled)
            {
                firstFrameSignaled = true;
                ManualResetEventSlim firstFrameEvent;
                lock (recordingLock)
                {
                    firstFrameEvent = firstFrameWritten;
                }

                if (firstFrameEvent != null)
                    firstFrameEvent.Set();
            }
        }

        private static void SleepUntil(Stopwatch stopwatch, long targetTicks)
        {
            while (true)
            {
                long remainingTicks = targetTicks - stopwatch.ElapsedTicks;
                if (remainingTicks <= 0)
                    return;

                int sleepMs = (int)(remainingTicks * 1000 / Stopwatch.Frequency);
                if (sleepMs > 1)
                    Thread.Sleep(sleepMs - 1);
                else
                    Thread.SpinWait(80);
            }
        }

        private static readonly SolidBrush MousePointerBrush = new SolidBrush(Color.Red);

        private static void DrawMousePointer(Graphics graphics, Rectangle captureBounds, float dpiScale)
        {
            Point cursorPos = Cursor.Position;
            int localX = cursorPos.X - captureBounds.X;
            int localY = cursorPos.Y - captureBounds.Y;

            if (localX >= 0 && localX < captureBounds.Width && localY >= 0 && localY < captureBounds.Height)
            {
                int scaledSize = DpiHelper.ScaleValue(10, dpiScale);
                int scaledOffset = scaledSize / 2;
                graphics.FillEllipse(
                    MousePointerBrush,
                    localX - scaledOffset,
                    localY - scaledOffset,
                    scaledSize,
                    scaledSize);
            }
        }

        private static void DrawTouchPointers(Graphics graphics, float dpiScale)
        {
            Point[] touchPoints = TouchOverlayForm.GetActiveTouchPointsSnapshot();
            if (touchPoints.Length == 0)
                return;

            int radius = DpiHelper.ScaleValue(20, dpiScale);
            int diameter = radius * 2;
            int borderWidth = Math.Max(1, DpiHelper.ScaleValue(3, dpiScale));
            foreach (Point point in touchPoints)
            {
                Rectangle rect = new Rectangle(point.X - radius, point.Y - radius, diameter, diameter);
                using (Pen border = new Pen(Color.White, borderWidth))
                {
                    graphics.FillEllipse(MousePointerBrush, rect);
                    graphics.DrawEllipse(border, rect);
                }
            }
        }

        private unsafe byte[] BitmapToRgb32TopDownPadded(Bitmap bitmap, int outputWidth, int outputHeight)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int outputStride = outputWidth * 4;
                int bufferSize = GetFrameBufferSize(outputWidth, outputHeight);
                if (frameBuffer == null || frameBuffer.Length < bufferSize)
                    frameBuffer = new byte[bufferSize];

                if (outputWidth != bitmap.Width || outputHeight != bitmap.Height)
                    Array.Clear(frameBuffer, 0, bufferSize);

                int inputRowBytes = bitmap.Width * 4;
                int inputStride = data.Stride;
                int absStride = Math.Abs(inputStride);
                byte* srcBase = (byte*)data.Scan0.ToPointer();
                byte* src = inputStride < 0 ? srcBase + (bitmap.Height - 1) * absStride : srcBase;

                fixed (byte* destBase = frameBuffer)
                {
                    byte* dest = destBase;
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Buffer.MemoryCopy(src, dest, outputStride, inputRowBytes);
                        dest += outputStride;
                        src += inputStride < 0 ? -absStride : absStride;
                    }
                }

                return frameBuffer;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static int MakeH264CompatibleDimension(int value)
        {
            return (value & 1) == 0 ? value : value + 1;
        }

        private static int GetFrameBufferSize(int width, int height)
        {
            long bytes = width * (long)height * 4L;
            if (bytes > int.MaxValue)
                throw new InvalidOperationException("Screen resolution is too large for a single RGB32 frame buffer.");

            return (int)bytes;
        }

        private static int SelectBitrate(int width, int height)
        {
            long pixels = width * (long)height;
            if (pixels >= 3840L * 2160L)
                return UltraHdBitrate;
            if (pixels >= 2560L * 1440L)
                return QuadHdBitrate;
            if (pixels >= 1280L * 720L)
                return FullHdBitrate;

            return LowResolutionBitrate;
        }

        private static double EstimateMegabytesPerMinute(int bitrate)
        {
            return bitrate * 60.0 / 8.0 / 1024.0 / 1024.0;
        }
    }
}
