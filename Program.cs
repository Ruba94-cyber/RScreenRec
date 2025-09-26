using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScreenshotFlash
{
    static class Program
    {
        private static string LockFilePath = Path.Combine(Path.GetTempPath(), "screenrec.lock");

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            // ✅ Disabilita scaling DPI per schermi speciali tipo Panasonic FG-Z2
            SetProcessDPIAware();

            if (File.Exists(LockFilePath))
            {
                try { File.Delete(LockFilePath); } catch { }
                return;
            }

            File.WriteAllText(LockFilePath, "recording");

            // ✅ Ottiene il monitor corretto sotto al cursore
            Screen activeScreen = Screen.FromPoint(Cursor.Position);
            Rectangle bounds = activeScreen.Bounds;

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Captures");
            Directory.CreateDirectory(folder);

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
            recorder.StartRecording(bounds, outputPath);

            // Overlay pallino
            var overlay = new RecordingOverlayForm(bounds);
            Thread overlayThread = new Thread(() => Application.Run(overlay));
            overlayThread.SetApartmentState(ApartmentState.STA);
            overlayThread.Start();

            // Overlay touch
            var touchOverlay = new TouchOverlayForm(bounds);
            Thread touchThread = new Thread(() => Application.Run(touchOverlay));
            touchThread.SetApartmentState(ApartmentState.STA);
            touchThread.Start();

            // Attendi la rimozione del file di lock
            while (File.Exists(LockFilePath))
            {
                Thread.Sleep(300);
            }

            recorder.StopRecording();

            if (overlay != null && !overlay.IsDisposed)
                overlay.Invoke(new Action(() => overlay.Close()));

            if (touchOverlay != null && !touchOverlay.IsDisposed)
                touchOverlay.Invoke(new Action(() => touchOverlay.Close()));
        }
    }
}
