using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RScreenRec
{
    public class ScreenRecorder
    {
        private const int FramesPerSecond = 30;
        private readonly object recordingLock = new object();
        private readonly StringBuilder ffmpegLog = new StringBuilder();
        private Process ffmpegProcess;
        private Thread recordingThread;
        private Stopwatch recordingStopwatch;
        private Rectangle bounds;
        private byte[] frameBuffer;
        private bool isRecording;
        private string outputPath;

        public void StartRecording(Rectangle screenBounds, string outputPath)
        {
            if (screenBounds.Width <= 0 || screenBounds.Height <= 0)
                throw new ArgumentException("Screen bounds must have a positive size.", "screenBounds");
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Output path is required.", "outputPath");

            lock (recordingLock)
            {
                if (isRecording)
                    throw new InvalidOperationException("A recording session is already in progress.");

                bounds = screenBounds;
                this.outputPath = outputPath;
                frameBuffer = new byte[bounds.Width * bounds.Height * 4];
            }

            string ffmpegPath = ResolveFfmpegPath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            string preset = SelectPreset(screenBounds.Width, screenBounds.Height);
            string arguments = BuildArguments(screenBounds, outputPath, preset);
            Logger.Log(string.Format("Starting FFmpeg pipe encoder. Path={0}, Args={1}", ffmpegPath, arguments));

            var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(outputPath)
            };
            process.EnableRaisingEvents = true;
            process.ErrorDataReceived += OnFfmpegOutput;
            process.OutputDataReceived += OnFfmpegOutput;
            process.Exited += (s, e) =>
            {
                lock (recordingLock)
                {
                    isRecording = false;
                }
                Logger.Log(string.Format("FFmpeg exited. ExitCode={0}, Output={1}", SafeExitCode(process), this.outputPath));
            };

            ffmpegLog.Length = 0;
            if (!process.Start())
                throw new InvalidOperationException("Could not start FFmpeg.");

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            lock (recordingLock)
            {
                ffmpegProcess = process;
                isRecording = true;
                recordingStopwatch = Stopwatch.StartNew();
            }

            recordingThread = new Thread(RecordLoop);
            recordingThread.IsBackground = true;
            recordingThread.Name = "ScreenCapturePipe";
            recordingThread.Priority = ThreadPriority.AboveNormal;
            recordingThread.Start();

            Thread.Sleep(250);
            if (process.HasExited)
            {
                StopRecording();
                throw new InvalidOperationException("FFmpeg stopped during startup. " + ffmpegLog);
            }

            Logger.Log(string.Format("Recording started. Output={0}, Bounds={1}x{2}@({3},{4}), FPS={5}, Preset={6}",
                outputPath, screenBounds.Width, screenBounds.Height, screenBounds.X, screenBounds.Y, FramesPerSecond, preset));
        }

        public void StopRecording()
        {
            Process process;
            Thread captureThread;
            Stopwatch stopwatch;
            string path;

            lock (recordingLock)
            {
                process = ffmpegProcess;
                captureThread = recordingThread;
                stopwatch = recordingStopwatch;
                path = outputPath;
                isRecording = false;
                recordingThread = null;
                recordingStopwatch = null;
            }

            TimeSpan requestedDuration = stopwatch != null ? stopwatch.Elapsed : TimeSpan.Zero;
            if (stopwatch != null)
                stopwatch.Stop();

            CloseFfmpegInput(process);

            if (captureThread != null && captureThread.IsAlive && !captureThread.Join(1000))
            {
                Logger.Log("Capture thread did not stop immediately after input pipe close.");
            }

            if (process == null)
                return;

            try
            {
                if (!process.WaitForExit(60000))
                {
                    Logger.Log("FFmpeg did not finalize after input EOF; killing process.");
                    process.Kill();
                    process.WaitForExit(5000);
                }

                long size = File.Exists(path) ? new FileInfo(path).Length : 0;
                Logger.Log(string.Format("Recording stopped. RequestedDuration={0}, ExitCode={1}, Size={2}, Output={3}",
                    requestedDuration, SafeExitCode(process), size, path));
            }
            finally
            {
                try { process.CancelErrorRead(); } catch { }
                try { process.CancelOutputRead(); } catch { }
                process.Dispose();
                lock (recordingLock)
                {
                    if (ffmpegProcess == process)
                        ffmpegProcess = null;
                    frameBuffer = null;
                }
            }
        }

        public bool IsRecording
        {
            get
            {
                lock (recordingLock)
                {
                    if (ffmpegProcess != null && ffmpegProcess.HasExited)
                        isRecording = false;
                    return isRecording;
                }
            }
        }

        private void RecordLoop()
        {
            Stopwatch stopwatch;
            Process process;
            Rectangle captureBounds;

            lock (recordingLock)
            {
                stopwatch = recordingStopwatch;
                process = ffmpegProcess;
                captureBounds = bounds;
            }

            if (stopwatch == null || process == null)
                return;

            long frameIntervalTicks = Math.Max(1, (long)Math.Round(Stopwatch.Frequency / (double)FramesPerSecond));
            long nextFrameTicks = stopwatch.ElapsedTicks;
            float dpiScale = DpiHelper.GetSystemDpiScale();

            try
            {
                using (Bitmap bitmap = new Bitmap(captureBounds.Width, captureBounds.Height, PixelFormat.Format32bppArgb))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    while (IsRecording)
                    {
                        graphics.CopyFromScreen(captureBounds.X, captureBounds.Y, 0, 0, captureBounds.Size, CopyPixelOperation.SourceCopy);
                        DrawMousePointer(graphics, captureBounds, dpiScale);
                        DrawTouchPointers(graphics, dpiScale);
                        byte[] frame = BitmapToBgraTopDown(bitmap);
                        process.StandardInput.BaseStream.Write(frame, 0, frame.Length);

                        nextFrameTicks += frameIntervalTicks;
                        long nowTicks = stopwatch.ElapsedTicks;
                        if (nowTicks > nextFrameTicks)
                            nextFrameTicks = nowTicks;
                        else
                            SleepUntil(stopwatch, nextFrameTicks);
                    }
                }
            }
            catch (Exception ex)
            {
                if (IsRecording)
                    Logger.Log("Capture loop stopped by error.", ex);
            }
            finally
            {
                CloseFfmpegInput(process);
                Logger.Log("Capture loop stopped.");
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

        private unsafe byte[] BitmapToBgraTopDown(Bitmap bitmap)
        {
            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = bitmap.Width * 4;
                int bufferSize = rowBytes * bitmap.Height;
                if (frameBuffer == null || frameBuffer.Length < bufferSize)
                    frameBuffer = new byte[bufferSize];

                int stride = data.Stride;
                int absStride = Math.Abs(stride);
                byte* srcBase = (byte*)data.Scan0.ToPointer();
                byte* src = stride < 0 ? srcBase + (bitmap.Height - 1) * absStride : srcBase;

                fixed (byte* destBase = frameBuffer)
                {
                    byte* dest = destBase;
                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        Buffer.MemoryCopy(src, dest, rowBytes, rowBytes);
                        dest += rowBytes;
                        src += stride < 0 ? -absStride : absStride;
                    }
                }

                return frameBuffer;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private void OnFfmpegOutput(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            lock (ffmpegLog)
            {
                if (ffmpegLog.Length > 12000)
                    ffmpegLog.Remove(0, ffmpegLog.Length - 8000);
                ffmpegLog.AppendLine(e.Data);
            }

            if (e.Data.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                e.Data.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Logger.Log("FFmpeg: " + e.Data);
            }
        }

        private static string ResolveFfmpegPath()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string resourceName = null;

            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (name.EndsWith(".tools.ffmpeg.exe.gz", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".ffmpeg.exe.gz", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".tools.ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                {
                    resourceName = name;
                    break;
                }
            }

            if (resourceName == null)
                throw new FileNotFoundException("Embedded ffmpeg.exe resource was not found. The executable was not packaged correctly.");

            string cacheDirectory = Path.Combine(Path.GetTempPath(), "RScreenRec");
            Directory.CreateDirectory(cacheDirectory);
            string ffmpegPath = Path.Combine(cacheDirectory, "RScreenRec_ffmpeg.exe");

            using (Stream resource = assembly.GetManifestResourceStream(resourceName))
            {
                if (resource == null)
                    throw new FileNotFoundException("Embedded ffmpeg.exe resource could not be opened.");

                bool compressed = resourceName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
                FileInfo existing = new FileInfo(ffmpegPath);
                if (!existing.Exists || existing.Length == 0)
                {
                    string tempPath = ffmpegPath + ".tmp";
                    using (FileStream output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        if (compressed)
                        {
                            using (var gzip = new GZipStream(resource, CompressionMode.Decompress))
                            {
                                gzip.CopyTo(output);
                            }
                        }
                        else
                        {
                            resource.CopyTo(output);
                        }
                    }

                    if (File.Exists(ffmpegPath))
                        File.Delete(ffmpegPath);
                    File.Move(tempPath, ffmpegPath);
                }
            }

            return ffmpegPath;
        }

        private static string BuildArguments(Rectangle bounds, string outputPath, string preset)
        {
            return string.Join(" ", new[]
            {
                "-hide_banner",
                "-loglevel warning",
                "-y",
                "-f rawvideo",
                "-pixel_format bgra",
                "-video_size " + bounds.Width + "x" + bounds.Height,
                "-framerate " + FramesPerSecond,
                "-i pipe:0",
                "-an",
                "-c:v libx264",
                "-preset " + preset,
                "-crf " + SelectCrf(bounds.Width, bounds.Height),
                "-pix_fmt yuv420p",
                "-g " + (FramesPerSecond * 2),
                Quote(outputPath)
            });
        }

        private static string SelectPreset(int width, int height)
        {
            long pixels = width * (long)height;
            if (pixels >= 3840L * 2160L)
                return "ultrafast";
            if (pixels >= 2560L * 1440L)
                return "superfast";
            return "veryfast";
        }

        private static int SelectCrf(int width, int height)
        {
            long pixels = width * (long)height;
            return pixels >= 2560L * 1440L ? 24 : 23;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void CloseFfmpegInput(Process process)
        {
            if (process == null)
                return;

            try
            {
                process.StandardInput.BaseStream.Close();
            }
            catch { }
        }

        private static int SafeExitCode(Process process)
        {
            try
            {
                return process.HasExited ? process.ExitCode : int.MinValue;
            }
            catch
            {
                return int.MinValue;
            }
        }
    }
}
