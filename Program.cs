using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RScreenRec
{
    static class Program
    {
        private const string InstanceMutexName = "RScreenRec_SingleInstance";
        private const string StopEventName = "RScreenRec_StopEvent";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        private const int PROCESS_DPI_UNAWARE = 0;
        private const int PROCESS_SYSTEM_DPI_AWARE = 1;
        private const int PROCESS_PER_MONITOR_DPI_AWARE = 2;

        [STAThread]
        static void Main()
        {
            // Configure DPI awareness
            try
            {
                SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
            }
            catch
            {
                SetProcessDPIAware();
            }

            bool isFirstInstance;
            using (var mutex = new Mutex(true, InstanceMutexName, out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    SignalStop();
                    return; // second launch only stops the running recorder
                }

                bool createdNewStopEvent;
                using (var stopEvent = new EventWaitHandle(
                    false,
                    EventResetMode.ManualReset,
                    StopEventName,
                    out createdNewStopEvent))
                {
                    if (!createdNewStopEvent)
                    {
                        // Ensure previous stop signal does not terminate immediately
                        stopEvent.Reset();
                    }

                    // Get the monitor under the cursor
                    Screen activeScreen = Screen.FromPoint(Cursor.Position);
                    Rectangle bounds = activeScreen.Bounds;

                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Captures");
                    try
                    {
                        Directory.CreateDirectory(folder);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(string.Format("Error creating output directory: {0}", ex.Message));
                        return;
                    }

                    int counter = 1;
                    foreach (var f in Directory.GetFiles(folder, "rec_*.avi"))
                    {
                        string name = Path.GetFileNameWithoutExtension(f);
                        if (name.StartsWith("rec_"))
                        {
                            var parts = name.Split('_');
                            if (parts.Length > 1)
                            {
                                int n;
                                if (int.TryParse(parts[1], out n) && n >= counter)
                                    counter = n + 1;
                            }
                        }
                    }

                    string timestamp = DateTime.Now.ToString("HH'h'mm'm'ss's'_dd-MM-yyyy");
                    string outputPath = Path.Combine(folder, string.Format("rec_{0}_{1}.avi", counter, timestamp));

                    var recorder = new ScreenRecorder();
                    try
                    {
                        recorder.StartRecording(bounds, outputPath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(string.Format("Failed to start recording: {0}", ex.Message));
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

                    // Wait until stop is requested or recording ends
                    while (recorder.IsRecording && !stopEvent.WaitOne(0))
                    {
                        Thread.Sleep(200);
                    }

                    recorder.StopRecording();
                    stopEvent.Reset();

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

        private static void SignalStop()
        {
            try
            {
                using (var stopEvent = EventWaitHandle.OpenExisting(StopEventName))
                {
                    stopEvent.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // No running instance to stop
            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("Warning: could not signal stop: {0}", ex.Message));
            }
        }
    }
}
