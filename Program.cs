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
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_SHIFT = 0x10;

        private const string InstanceMutexName = @"Local\RScreenRec_SingleInstance";
        private const string StopEventName = @"Local\RScreenRec_StopEvent";
        private static readonly string StopRequestPath = Path.Combine(Path.GetTempPath(), "RScreenRec_" + Environment.UserName + "_" + Process.GetCurrentProcess().SessionId + ".stop");

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
            Stopwatch startupStopwatch = Stopwatch.StartNew();
            ConfigureDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool windowSelectionRequested = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
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
                    stopEvent.Reset();
                    TryDeleteStopRequest();

                    IntPtr selectedWindow = IntPtr.Zero;
                    if (windowSelectionRequested)
                    {
                        DialogResult choice = MessageBox.Show(
                            "Experimental window capture uses PrintWindow, not Windows Graphics Capture. " +
                            "Some apps may render black or incomplete frames; minimizing the window stops recording. " +
                            "Other windows are never used as a fallback. Continue?",
                            "RScreenRec - Experimental window capture",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        if (choice != DialogResult.Yes) return;
                        selectedWindow = WindowPicker.SelectWindow();
                        if (selectedWindow == IntPtr.Zero) return;
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
                        Logger.Log("Error creating output directory.", ex);
                        MessageBox.Show("Cannot create the recording folder: " + ex.Message,
                            "RScreenRec", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    string outputPath = CreateOutputPath(folder);

                    var recorder = new ScreenRecorder();
                    try
                    {
                        if (selectedWindow == IntPtr.Zero)
                            recorder.StartRecording(bounds, outputPath);
                        else
                            recorder.StartWindowRecording(selectedWindow, outputPath);
                        Logger.Log(string.Format("Startup ready in {0} ms. Output={1}",
                            startupStopwatch.ElapsedMilliseconds,
                            outputPath));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Failed to start recording.", ex);
                        MessageBox.Show("Recording could not start: " + ex.Message,
                            "RScreenRec", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    OverlayHost touchOverlay = selectedWindow == IntPtr.Zero
                        ? OverlayHost.Start("TouchOverlay", () => new TouchOverlayForm(bounds)) : null;
                    OverlayHost recordingOverlay = selectedWindow == IntPtr.Zero
                        ? OverlayHost.Start("RecordingOverlay", () => new RecordingOverlayForm(bounds)) : null;

                    // Wait until stop is requested or recording ends
                    bool stopRequested = false;
                    while (recorder.IsRecording)
                    {
                        if (stopEvent.WaitOne(20) || File.Exists(StopRequestPath))
                        {
                            stopRequested = true;
                            break;
                        }
                    }

                    if (stopRequested)
                    {
                        Logger.Log("Stop request detected; closing overlays immediately.");
                        if (recordingOverlay != null) recordingOverlay.CloseNow();
                        if (touchOverlay != null) touchOverlay.CloseNow();
                    }

                    try
                    {
                        recorder.StopRecording();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Failed to stop recording cleanly.", ex);
                        MessageBox.Show("Recording ended with an error: " + ex.Message,
                            "RScreenRec", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    stopEvent.Reset();
                    TryDeleteStopRequest();
                    if (recordingOverlay != null) recordingOverlay.Dispose();
                    if (touchOverlay != null) touchOverlay.Dispose();
                }
            }
        }

        private static string CreateOutputPath(string folder)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            string basePath = Path.Combine(folder, "rec_" + timestamp);
            string outputPath = basePath + ".mp4";
            if (!File.Exists(outputPath))
                return outputPath;

            for (int i = 1; i < 1000; i++)
            {
                outputPath = string.Format("{0}_{1}.mp4", basePath, i);
                if (!File.Exists(outputPath))
                    return outputPath;
            }

            return Path.Combine(folder, "rec_" + timestamp + "_" + Guid.NewGuid().ToString("N") + ".mp4");
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

            // The file is a fallback only. Writing it after a successful event
            // signal could leave a stale request that stops a later recording.
            if (!signaled)
            {
                try
                {
                    File.WriteAllText(StopRequestPath, DateTime.UtcNow.ToString("O"));
                    signaled = true;
                }
                catch (Exception ex)
                {
                    Logger.Log("Could not write stop request file.", ex);
                }
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
                int result = SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
                if (result >= 0)
                    return;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            try { SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
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
                        form.Dispose();
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
