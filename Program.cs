using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RScreenRec
{
    static class Program
    {
        private static string LockFilePath = Path.Combine(Path.GetTempPath(), "screenrec.lock");

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll")]
        private static extern int GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        private const int PROCESS_DPI_UNAWARE = 0;
        private const int PROCESS_SYSTEM_DPI_AWARE = 1;
        private const int PROCESS_PER_MONITOR_DPI_AWARE = 2;

        [STAThread]
        static void Main()
        {
            // ✅ Configure modern DPI awareness for multi-monitor setups
            try
            {
                // Try the modern method first (Windows 8.1+)
                SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
            }
            catch
            {
                // Fall back to the legacy method (Windows Vista+)
                SetProcessDPIAware();
            }

            if (File.Exists(LockFilePath))
            {
                try
                {
                    File.Delete(LockFilePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not delete lock file: {ex.Message}");
                }
                return;
            }

            try
            {
                File.WriteAllText(LockFilePath, "recording");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating lock file: {ex.Message}");
                return;
            }

            // ✅ Get the monitor under the cursor
            Screen activeScreen = Screen.FromPoint(Cursor.Position);
            Rectangle bounds = activeScreen.Bounds;

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Captures");
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating output directory: {ex.Message}");
                return;
            }

            int counter = 1;
            foreach (var f in Directory.GetFiles(folder, "rec_*.avi"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.StartsWith("rec_"))
                {
                    var parts = name.Split('_');
                    if (parts.Length > 1 && int.TryParse(parts[1], out int n) && n >= counter)
                        counter = n + 1;
                }
            }

            string timestamp = DateTime.Now.ToString("HH'h'mm'm'ss's'_dd-MM-yyyy");
            string outputPath = Path.Combine(folder, $"rec_{counter}_{timestamp}.avi");

            var recorder = new ScreenRecorder();
            try
            {
                recorder.StartRecording(bounds, outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start recording: {ex.Message}");
                try { File.Delete(LockFilePath); } catch { }
                return;
            }

            // Recording indicator overlay
            var overlay = new RecordingOverlayForm(bounds);
            Thread overlayThread = new Thread(() => Application.Run(overlay));
            overlayThread.SetApartmentState(ApartmentState.STA);
            overlayThread.Start();

            // Touch indicator overlay
            var touchOverlay = new TouchOverlayForm(bounds);
            Thread touchThread = new Thread(() => Application.Run(touchOverlay));
            touchThread.SetApartmentState(ApartmentState.STA);
            touchThread.Start();

            // Wait for the lock file to be removed
            while (File.Exists(LockFilePath) && recorder.IsRecording)
            {
                Thread.Sleep(300);
            }

            recorder.StopRecording();

            try
            {
                if (File.Exists(LockFilePath))
                {
                    File.Delete(LockFilePath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not clean up lock file: {ex.Message}");
            }

            try
            {
                if (overlay != null && overlay.IsHandleCreated && !overlay.IsDisposed)
                    overlay.BeginInvoke(new Action(() => overlay.Close()));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }

            try
            {
                if (touchOverlay != null && touchOverlay.IsHandleCreated && !touchOverlay.IsDisposed)
                    touchOverlay.BeginInvoke(new Action(() => touchOverlay.Close()));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}
