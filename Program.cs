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
        private const string InstanceMutexName = @"Global\RScreenRec_SingleInstance";
        private const string StopEventName = @"Global\RScreenRec_StopEvent";
        private static readonly string StopRequestPath = Path.Combine(Path.GetTempPath(), "RScreenRec.stop");

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        private const int PROCESS_DPI_UNAWARE = 0;
        private const int PROCESS_SYSTEM_DPI_AWARE = 1;
        private const int PROCESS_PER_MONITOR_DPI_AWARE = 2;
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [STAThread]
        static void Main()
        {
            ConfigureDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool isFirstInstance;
            using (var mutex = new Mutex(true, InstanceMutexName, out isFirstInstance))
            {
                if (!isFirstInstance || HasOtherRunningInstance())
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
                    stopEvent.Reset();
                    TryDeleteStopRequest();

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
                    foreach (var f in Directory.GetFiles(folder, "rec_*.*"))
                    {
                        string extension = Path.GetExtension(f);
                        if (!string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(extension, ".avi", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

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
                    string outputPath = Path.Combine(folder, string.Format("rec_{0}_{1}.mp4", counter, timestamp));

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

                    OverlayHost touchOverlay = OverlayHost.Start("TouchOverlay", () => new TouchOverlayForm(bounds));
                    OverlayHost recordingOverlay = OverlayHost.Start("RecordingOverlay", () => new RecordingOverlayForm(bounds));

                    // Wait until stop is requested or recording ends
                    bool stopRequested = false;
                    while (recorder.IsRecording)
                    {
                        if (stopEvent.WaitOne(50) || File.Exists(StopRequestPath))
                        {
                            stopRequested = true;
                            break;
                        }
                    }

                    if (stopRequested)
                    {
                        Logger.Log("Stop request detected; closing overlays immediately.");
                        recordingOverlay.CloseNow();
                        touchOverlay.CloseNow();
                    }

                    recorder.StopRecording();
                    stopEvent.Reset();
                    TryDeleteStopRequest();
                    recordingOverlay.Dispose();
                    touchOverlay.Dispose();
                }
            }
        }

        private static void SignalStop()
        {
            bool signaled = false;
            try
            {
                using (var stopEvent = EventWaitHandle.OpenExisting(StopEventName))
                {
                    stopEvent.Set();
                    signaled = true;
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

            try
            {
                File.WriteAllText(StopRequestPath, DateTime.UtcNow.ToString("O"));
                signaled = true;
            }
            catch (Exception ex)
            {
                Logger.Log("Could not write stop request file.", ex);
            }

            Logger.Log(string.Format("Stop signal requested. Success={0}", signaled));
        }

        private static void ConfigureDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
                    return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try
            {
                SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
            }
            catch
            {
                try
                {
                    SetProcessDPIAware();
                }
                catch { }
            }
        }

        private static bool HasOtherRunningInstance()
        {
            try
            {
                int currentId = Process.GetCurrentProcess().Id;
                foreach (Process process in Process.GetProcessesByName("RScreenRec"))
                {
                    using (process)
                    {
                        if (process.Id != currentId)
                            return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("Could not inspect running RScreenRec processes.", ex);
            }

            return false;
        }

        private static void TryDeleteStopRequest()
        {
            try
            {
                if (File.Exists(StopRequestPath))
                    File.Delete(StopRequestPath);
            }
            catch (Exception ex)
            {
                Logger.Log("Could not delete stop request file.", ex);
            }
        }

        private sealed class OverlayHost : IDisposable
        {
            private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
            private readonly Func<Form> formFactory;
            private readonly string name;
            private Thread thread;
            private Form form;
            private bool closeRequested;

            private OverlayHost(string name, Func<Form> formFactory)
            {
                this.name = name;
                this.formFactory = formFactory;
            }

            public static OverlayHost Start(string name, Func<Form> formFactory)
            {
                var host = new OverlayHost(name, formFactory);
                host.Start();
                return host;
            }

            public void CloseNow()
            {
                Form current = form;
                closeRequested = true;
                if (current == null)
                    return;

                try
                {
                    if (!current.IsDisposed && current.IsHandleCreated)
                    {
                        current.BeginInvoke(new Action(() =>
                        {
                            current.Hide();
                            current.Close();
                        }));
                    }
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }

            public void Dispose()
            {
                CloseNow();
                if (thread != null && thread.IsAlive)
                    thread.Join(2000);
                ready.Dispose();
            }

            private void Start()
            {
                thread = new Thread(Run);
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Name = name;
                thread.Start();
                ready.Wait(250);
            }

            private void Run()
            {
                try
                {
                    form = formFactory();
                    if (closeRequested)
                    {
                        ready.Set();
                        return;
                    }

                    form.Shown += (s, e) => ready.Set();
                    form.FormClosed += (s, e) => ready.Set();
                    Application.Run(form);
                }
                catch (Exception ex)
                {
                    Logger.Log(string.Format("{0} failed.", name), ex);
                    ready.Set();
                }
            }
        }
    }
}
