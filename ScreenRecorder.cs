using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using RScreenRec.Avi;

namespace RScreenRec
{
    public class ScreenRecorder
    {
        private const int FramesPerSecond = 24;
        private static readonly bool UseMjpegCompression = false;
        private const int JpegQuality = 80;
        private const long AviSizeLimitBytes = AviWriter.MaxUsableFileSizeBytes;
        private Thread recordingThread;
        private bool isRecording = false;
        private Rectangle bounds;
        private AviWriter writer;
        private byte[] frameBuffer;
        private readonly object recordingLock = new object();
        private Stopwatch recordingStopwatch;
        private Stopwatch segmentStopwatch;
        private int capturedFrames = 0;
        private string baseOutputPath;
        private int segmentIndex = 1;
        private bool mjpegEnabled = UseMjpegCompression;
        private MemoryStream jpegStream;
        private byte[] jpegBuffer = Array.Empty<byte>();
        private ImageCodecInfo jpegCodec;
        private EncoderParameters jpegEncoderParams;

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
                capturedFrames = 0;
                baseOutputPath = outputPath;
                segmentIndex = 1;
                mjpegEnabled = UseMjpegCompression;
            }

            try
            {
                writer = CreateWriter(outputPath);
                segmentStopwatch = Stopwatch.StartNew();
                Logger.Log($"Recording started. Output: {outputPath}, Bounds: {bounds.Width}x{bounds.Height}, MJPEG: {mjpegEnabled}");
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
            recordingStopwatch = Stopwatch.StartNew();
            recordingThread.Start();
        }

        private void RecordLoop()
        {
            var stopwatch = recordingStopwatch ?? Stopwatch.StartNew();
            recordingStopwatch = stopwatch;
            segmentStopwatch = segmentStopwatch ?? Stopwatch.StartNew();

            long frameIntervalTicks = (long)Math.Round(Stopwatch.Frequency / (double)FramesPerSecond);
            if (frameIntervalTicks <= 0)
                frameIntervalTicks = 1;

            float dpiScale = DpiHelper.GetSystemDpiScale();

            using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                while (true)
                {
                    long frameStart = stopwatch.ElapsedTicks;

                    lock (recordingLock)
                    {
                        if (!isRecording) break;
                    }

                    try
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                        DrawMousePointer(g, dpiScale);

                        byte[] frameData = GetFrameBytes(bmp, out int frameLength);
                        if (writer.WouldExceedLimit(frameLength, AviSizeLimitBytes))
                        {
                            RotateWriter();
                        }
                        writer.WriteFrame(frameData, frameLength);
                        capturedFrames++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Recording error", ex);

                        // If we can't capture frames, stop recording to prevent infinite loop
                        lock (recordingLock)
                        {
                            isRecording = false;
                        }
                        break;
                    }

                    long elapsedTicks = stopwatch.ElapsedTicks - frameStart;
                    long remainingTicks = frameIntervalTicks - elapsedTicks;
                    if (remainingTicks > 0)
                    {
                        int sleepMs = (int)(remainingTicks * 1000 / Stopwatch.Frequency);
                        if (sleepMs > 0)
                        {
                            Thread.Sleep(sleepMs);
                        }
                        else
                        {
                            Thread.SpinWait(100);
                        }
                    }
                }
            }

            recordingStopwatch?.Stop();
            segmentStopwatch?.Stop();

            lock (recordingLock)
            {
                isRecording = false;
            }
            Logger.Log($"Recording loop stopped. CapturedFrames={capturedFrames}");
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

        private byte[] GetFrameBytes(Bitmap bmp, out int length)
        {
            byte[] buffer = BitmapToRgbBytes(bmp);
            length = buffer.Length;
            return buffer;
        }

        private byte[] BitmapToJpegBytes(Bitmap bmp, out int length)
        {
            if (jpegCodec == null)
            {
                foreach (var codec in ImageCodecInfo.GetImageEncoders())
                {
                    if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    {
                        jpegCodec = codec;
                        break;
                    }
                }
                if (jpegCodec == null)
                {
                    throw new InvalidOperationException("JPEG encoder not found.");
                }
            }

            if (jpegEncoderParams == null)
            {
                jpegEncoderParams = new EncoderParameters(1);
                jpegEncoderParams.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
            }

            if (jpegStream == null)
            {
                jpegStream = new MemoryStream(bounds.Width * bounds.Height);
            }
            else
            {
                jpegStream.Position = 0;
                jpegStream.SetLength(0);
            }

            bmp.Save(jpegStream, jpegCodec, jpegEncoderParams);
            length = (int)jpegStream.Position;

            if (jpegBuffer == null || jpegBuffer.Length < length)
            {
                jpegBuffer = new byte[length];
            }
            Array.Copy(jpegStream.GetBuffer(), jpegBuffer, length);
            return jpegBuffer;
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

            TimeSpan duration = recordingStopwatch?.Elapsed ?? TimeSpan.Zero;
            recordingStopwatch = null;

            TimeSpan segmentDuration = segmentStopwatch?.Elapsed ?? duration;
            segmentStopwatch = null;

            writer?.Close(segmentDuration);
            writer = null;
            frameBuffer = null;
            DisposeEncodingResources();
            Logger.Log($"Recording stopped. Duration={segmentDuration}, Frames={capturedFrames}, OutputBase={baseOutputPath}");
        }

        private AviWriter CreateWriter(string path)
        {
            var codec = UseMjpegCompression ? AviWriter.VideoCodec.Mjpeg : AviWriter.VideoCodec.Rgb24;
            return new AviWriter(path, bounds.Width, bounds.Height, FramesPerSecond, codec);
        }

        private void RotateWriter()
        {
            TimeSpan elapsed = segmentStopwatch?.Elapsed ?? TimeSpan.Zero;
            writer?.Close(elapsed);
            segmentStopwatch?.Restart();

            segmentIndex++;
            string nextPath = GetSegmentPath(segmentIndex);
            writer = CreateWriter(nextPath);
        }

        private string GetSegmentPath(int index)
        {
            if (index <= 1 || string.IsNullOrEmpty(baseOutputPath))
                return baseOutputPath;

            string directory = Path.GetDirectoryName(baseOutputPath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(baseOutputPath);
            string extension = Path.GetExtension(baseOutputPath);

            return Path.Combine(directory, $"{name}_part{index:D2}{extension}");
        }

        private void DisposeEncodingResources()
        {
            jpegStream?.Dispose();
            jpegStream = null;

            if (jpegEncoderParams != null)
            {
                jpegEncoderParams.Dispose();
                jpegEncoderParams = null;
            }

            jpegCodec = null;
            jpegBuffer = Array.Empty<byte>();
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
