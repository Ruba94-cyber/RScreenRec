using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ScreenshotFlash.Avi;

namespace ScreenshotFlash
{
    public class ScreenRecorder
    {
        private Thread recordingThread;
        private bool isRecording = false;
        private Rectangle bounds;
        private AviWriter writer;

        public void StartRecording(Rectangle screenBounds, string outputPath)
        {
            bounds = screenBounds;
            writer = new AviWriter(outputPath, bounds.Width, bounds.Height, 30); // 30 FPS

            isRecording = true;
            recordingThread = new Thread(RecordLoop);
            recordingThread.IsBackground = true;
            recordingThread.Start();
        }

        private void RecordLoop()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long frameDuration = 1000L / 30; // 30 FPS → ~33ms/frame
            long nextFrameTime = 0;

            while (isRecording)
            {
                long elapsed = stopwatch.ElapsedMilliseconds;
                if (elapsed < nextFrameTime)
                {
                    Thread.Sleep((int)(nextFrameTime - elapsed));
                    continue;
                }

                nextFrameTime += frameDuration;

                try
                {
                    using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                            DrawMousePointer(g);
                        }

                        byte[] rawData = BitmapToRgbBytes(bmp);
                        writer.WriteFrame(rawData);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Errore durante la registrazione: " + ex.Message);
                }
            }
        }

        private void DrawMousePointer(Graphics g)
        {
            Point cursorPos = Cursor.Position;
            int localX = cursorPos.X - bounds.X;
            int localY = cursorPos.Y - bounds.Y;

            if (localX >= 0 && localX < bounds.Width && localY >= 0 && localY < bounds.Height)
            {
                using (Brush brush = new SolidBrush(Color.Red))
                {
                    g.FillEllipse(brush, localX - 5, localY - 5, 10, 10);
                }
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
            byte[] flipped = new byte[height * rowSize];
            byte* src = (byte*)bmpData.Scan0.ToPointer();

            fixed (byte* destPtr = flipped)
            {
                for (int y = 0; y < height; y++)
                {
                    int srcRow = y * stride;
                    int destRow = (height - 1 - y) * rowSize;

                    Buffer.MemoryCopy(src + srcRow, destPtr + destRow, rowSize, rowSize);
                }
            }

            bmp.UnlockBits(bmpData);
            return flipped;
        }

        public void StopRecording()
        {
            isRecording = false;
            recordingThread.Join();
            writer.Close();
        }
    }
}
