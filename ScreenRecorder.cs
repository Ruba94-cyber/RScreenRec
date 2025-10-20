using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using RScreenRec.Avi;

namespace RScreenRec
{
    public class ScreenRecorder
    {
        private const int FramesPerSecond = 30;
        private Thread recordingThread;
        private bool isRecording = false;
        private Rectangle bounds;
        private AviWriter writer;
        private byte[] frameBuffer;
        private readonly object recordingLock = new object();

        public void StartRecording(Rectangle screenBounds, string outputPath)
        {
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
                throw new ArgumentException("Screen bounds must have a positive size.", nameof(screenBounds));
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", nameof(outputPath));

            lock (recordingLock)
            {
                if (isRecording)
                    throw new InvalidOperationException("A recording session is already in progress.");

                bounds = screenBounds;
                frameBuffer = new byte[bounds.Width * bounds.Height * 3];
            }

            try
            {
                writer = new AviWriter(outputPath, bounds.Width, bounds.Height, FramesPerSecond);
            }
            catch (Exception ex)
            {
                lock (recordingLock)
                {
                    frameBuffer = null;
                    isRecording = false;
                }
                throw new InvalidOperationException($"Failed to initialize AVI writer: {ex.Message}", ex);
            }

            lock (recordingLock)
            {
                isRecording = true;
            }

            recordingThread = new Thread(RecordLoop);
            recordingThread.IsBackground = true;
            recordingThread.Name = "ScreenRecording";
            recordingThread.Priority = ThreadPriority.AboveNormal;
            recordingThread.Start();
        }

        private void RecordLoop()
        {
            var stopwatch = Stopwatch.StartNew();
            long frameIntervalTicks = (long)Math.Round(Stopwatch.Frequency / (double)FramesPerSecond);
            if (frameIntervalTicks <= 0)
                frameIntervalTicks = 1;
            long nextFrameTicks = stopwatch.ElapsedTicks;
            double ticksToMilliseconds = 1000.0 / Stopwatch.Frequency;

            float dpiScale = DpiHelper.GetSystemDpiScale();

            using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                while (true)
                {
                    lock (recordingLock)
                    {
                        if (!isRecording) break;
                    }

                    long currentTicks = stopwatch.ElapsedTicks;
                    long remainingTicks = nextFrameTicks - currentTicks;
                    if (remainingTicks > 0)
                    {
                        double remainingMs = remainingTicks * ticksToMilliseconds;
                        if (remainingMs >= 2.0)
                        {
                            Thread.Sleep((int)remainingMs - 1);
                        }
                        else
                        {
                            Thread.SpinWait(50);
                        }
                        continue;
                    }

                    nextFrameTicks = currentTicks + frameIntervalTicks;

                    try
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                        DrawMousePointer(g, dpiScale);

                        writer.WriteFrame(BitmapToRgbBytes(bmp));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Recording error: {ex.Message}");

                        // If we can't capture frames, stop recording to prevent infinite loop
                        lock (recordingLock)
                        {
                            isRecording = false;
                        }
                        break;
                    }

                    long postFrameTicks = stopwatch.ElapsedTicks;
                    while (nextFrameTicks < postFrameTicks)
                    {
                        nextFrameTicks += frameIntervalTicks;
                    }
                }
            }

            lock (recordingLock)
            {
                isRecording = false;
            }
        }

        private static readonly SolidBrush mousePointerBrush = new SolidBrush(Color.Red);

        private void DrawMousePointer(Graphics g, float dpiScale)
        {
            Point cursorPos = Cursor.Position;
            int localX = cursorPos.X - bounds.X;
            int localY = cursorPos.Y - bounds.Y;

            if (localX >= 0 && localX < bounds.Width && localY >= 0 && localY < bounds.Height)
            {
                int scaledSize = DpiHelper.ScaleValue(10, dpiScale);
                int scaledOffset = scaledSize / 2;

                g.FillEllipse(mousePointerBrush,
                    localX - scaledOffset, localY - scaledOffset,
                    scaledSize, scaledSize);
            }
        }

        private unsafe byte[] BitmapToRgbBytes(Bitmap bmp)
        {
            int width = bmp.Width;
            int height = bmp.Height;
            int rowSize = width * 3;

            Rectangle rect = new Rectangle(0, 0, width, height);
            BitmapData bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            int stride = bmpData.Stride;
            int bufferSize = height * rowSize;
            if (frameBuffer == null || frameBuffer.Length < bufferSize)
            {
                frameBuffer = new byte[bufferSize];
            }

            byte* srcOrigin = (byte*)bmpData.Scan0.ToPointer();
            bool bottomUp = stride > 0;
            if (stride < 0)
            {
                stride = -stride;
                bottomUp = false;
            }
            byte* src = bottomUp ? srcOrigin + (height - 1) * stride : srcOrigin;

            fixed (byte* destPtr = frameBuffer)
            {
                byte* destRowPtr = destPtr;
                for (int y = 0; y < height; y++)
                {
                    Buffer.MemoryCopy(src, destRowPtr, rowSize, rowSize);
                    destRowPtr += rowSize;
                    src += bottomUp ? -stride : stride;
                }
            }

            bmp.UnlockBits(bmpData);
            return frameBuffer;
        }

        public void StopRecording()
        {
            lock (recordingLock)
            {
                isRecording = false;
            }

            if (recordingThread != null && recordingThread.IsAlive)
            {
                recordingThread.Join();
                recordingThread = null;
            }

            writer?.Close();
            writer = null;
            frameBuffer = null;
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
    }
}
