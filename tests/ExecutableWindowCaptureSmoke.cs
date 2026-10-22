using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class ExecutableWindowCaptureSmoke
{
    private const int VkShift = 0x10;
    private const uint KeyUp = 0x0002;
    private const int GwChild = 5;
    private const int GwNext = 2;
    private const int LbGetText = 0x0189;
    private const int LbGetTextLength = 0x018A;
    private const int LbGetCount = 0x018B;
    private const int LbSetCurSel = 0x0186;
    private const int BmClick = 0x00F5;
    private const int WmClose = 0x0010;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindow(string className, string windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageInt(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageText(IntPtr window, int message, IntPtr wParam, StringBuilder text);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("Usage: ExecutableWindowCaptureSmoke <RScreenRec.exe> <output-directory> <width> <height>");
            return 2;
        }

        string appPath = Path.GetFullPath(args[0]);
        string outputDirectory = Path.GetFullPath(args[1]);
        int width = int.Parse(args[2]);
        int height = int.Parse(args[3]);
        Process targetProcess = null;
        Process appProcess = null;
        Scenario scenario = null;
        bool shiftDown = false;
        int result = 1;

        try
        {
            string tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "RScreenRecSmoke-"));
            if (!outputDirectory.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The executable smoke output must stay inside its dedicated temporary folder.");
            Directory.CreateDirectory(outputDirectory);
            if (Directory.GetFiles(outputDirectory, "*.mp4").Length != 0)
                throw new InvalidOperationException("The executable smoke output folder must be empty.");

            try
            {
                using (Mutex.OpenExisting(@"Local\RScreenRec_SingleInstance"))
                    throw new InvalidOperationException("RScreenRec is already running; the smoke test will not signal or stop the user's recording.");
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SyntheticCaptureTarget.exe");
            if (!File.Exists(targetPath))
                throw new FileNotFoundException("The synthetic capture target was not compiled.", targetPath);
            targetProcess = Process.Start(new ProcessStartInfo(targetPath, width + " " + height) { UseShellExecute = false });
            if (targetProcess == null || !targetProcess.WaitForInputIdle(10000))
                throw new TimeoutException("The synthetic target did not become idle.");

            IntPtr targetWindow = IntPtr.Zero;
            Stopwatch targetDeadline = Stopwatch.StartNew();
            while (targetDeadline.Elapsed < TimeSpan.FromSeconds(10) && !targetProcess.HasExited)
            {
                targetProcess.Refresh();
                targetWindow = targetProcess.MainWindowHandle;
                if (targetWindow != IntPtr.Zero)
                    break;
                System.Threading.Thread.Sleep(50);
            }
            if (targetWindow == IntPtr.Zero)
                throw new TimeoutException("The synthetic target window handle was unavailable.");

            keybd_event(VkShift, 0x2A, 0, UIntPtr.Zero);
            shiftDown = true;
            Stopwatch shiftDeadline = Stopwatch.StartNew();
            while ((GetAsyncKeyState(VkShift) & 0x8000) == 0 && shiftDeadline.Elapsed < TimeSpan.FromSeconds(1))
                System.Threading.Thread.Sleep(10);
            if ((GetAsyncKeyState(VkShift) & 0x8000) == 0)
                throw new InvalidOperationException("Windows did not report the injected Shift key as held.");

            var startInfo = new ProcessStartInfo(appPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(appPath)
            };
            startInfo.EnvironmentVariables["RSCREENREC_BENCHMARK_OUTPUT_DIRECTORY"] = outputDirectory;
            appProcess = Process.Start(startInfo);
            if (appProcess == null)
                throw new InvalidOperationException("The actual RScreenRec executable did not start.");

            Console.WriteLine("actual_exe_started=true");
            scenario = new Scenario(appPath, outputDirectory, targetWindow, appProcess);
            result = scenario.Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            result = 1;
        }
        finally
        {
            if (shiftDown)
                keybd_event(VkShift, 0x2A, KeyUp, UIntPtr.Zero);

            if (scenario != null)
                scenario.Dispose();

            StopAppSafely(appPath, appProcess);
            CloseTarget(targetProcess);

            if (result != 0 && Directory.Exists(outputDirectory))
            {
                foreach (string video in Directory.GetFiles(outputDirectory, "*.mp4"))
                {
                    try { File.Delete(video); }
                    catch (Exception ex) { Console.Error.WriteLine("Could not remove failed smoke output: " + ex.Message); }
                }
            }
        }

        return result;
    }

    private static void StopAppSafely(string appPath, Process appProcess)
    {
        if (appProcess == null)
            return;

        try
        {
            if (!appProcess.HasExited)
            {
                IntPtr picker = FindWindow(null, "Select window - RScreenRec");
                if (picker != IntPtr.Zero)
                    SendMessageInt(picker, WmClose, IntPtr.Zero, IntPtr.Zero);

                using (Process stopSignal = Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = false }))
                    if (stopSignal != null)
                        stopSignal.WaitForExit(3000);

                if (!appProcess.WaitForExit(5000))
                {
                    appProcess.Kill();
                    appProcess.WaitForExit(3000);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Recorder cleanup: " + ex.Message);
            try
            {
                if (!appProcess.HasExited)
                {
                    appProcess.Kill();
                    appProcess.WaitForExit(3000);
                }
            }
            catch { }
        }
        finally
        {
            appProcess.Dispose();
        }
    }

    private static void CloseTarget(Process targetProcess)
    {
        if (targetProcess == null)
            return;
        try
        {
            if (!targetProcess.HasExited)
            {
                targetProcess.CloseMainWindow();
                if (!targetProcess.WaitForExit(3000))
                {
                    targetProcess.Kill();
                    targetProcess.WaitForExit(3000);
                }
            }
        }
        catch { }
        finally
        {
            targetProcess.Dispose();
        }
    }

    private sealed class Scenario : IDisposable
    {
        private readonly string appPath;
        private readonly string outputDirectory;
        private readonly IntPtr targetWindow;
        private readonly Process appProcess;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch total = Stopwatch.StartNew();
        private Stopwatch recording;
        private Form occluder;
        private string videoPath;
        private string failure;
        private bool pickerSeen;
        private bool targetSelected;
        private bool occlusionShown;
        private bool occlusionHidden;
        private bool resized;
        private bool minimized;
        private bool restored;
        private bool stopRequested;
        private DateTime pickerSeenAt;
        private DateTime selectionAt;
        private int result = 1;

        internal Scenario(string appPath, string outputDirectory, IntPtr targetWindow, Process appProcess)
        {
            this.appPath = appPath;
            this.outputDirectory = outputDirectory;
            this.targetWindow = targetWindow;
            this.appProcess = appProcess;
            timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += OnTick;
        }

        internal int Run()
        {
            timer.Start();
            Application.Run(new ApplicationContext());
            timer.Stop();
            if (failure != null)
                Console.Error.WriteLine(failure);
            return result;
        }

        private void OnTick(object sender, EventArgs args)
        {
            try
            {
                if (appProcess.HasExited && !stopRequested)
                    throw new InvalidOperationException("The actual RScreenRec process exited before the smoke test requested stop.");

                IntPtr compatibilityDialog = FindWindow(null, "RScreenRec - Compatibility capture");
                if (compatibilityDialog != IntPtr.Zero)
                {
                    SendMessageInt(compatibilityDialog, WmClose, IntPtr.Zero, IntPtr.Zero);
                    throw new InvalidOperationException("The actual executable selected compatibility capture instead of Windows Graphics Capture.");
                }

                IntPtr picker = FindWindow(null, "Select window - RScreenRec");
                if (picker != IntPtr.Zero)
                {
                    uint pickerProcessId;
                    GetWindowThreadProcessId(picker, out pickerProcessId);
                    if (pickerProcessId == (uint)appProcess.Id)
                    {
                        if (!pickerSeen)
                        {
                            pickerSeen = true;
                            pickerSeenAt = DateTime.UtcNow;
                            keybd_event(VkShift, 0x2A, KeyUp, UIntPtr.Zero);
                            Console.WriteLine("shift_picker=seen_with_shift_held");
                        }
                        SelectSyntheticTarget(picker);
                    }
                }

                if (!pickerSeen && total.Elapsed > TimeSpan.FromSeconds(5))
                    throw new TimeoutException("Holding Shift did not open the actual executable's window picker within five seconds.");
                if (pickerSeen && !targetSelected && DateTime.UtcNow - pickerSeenAt > TimeSpan.FromSeconds(10))
                    throw new TimeoutException("The synthetic target did not appear in the actual executable's window picker.");
                if (targetSelected && recording == null)
                {
                    string[] videos = Directory.GetFiles(outputDirectory, "rec_*.mp4");
                    if (videos.Length > 0)
                    {
                        Array.Sort(videos, StringComparer.OrdinalIgnoreCase);
                        for (int i = videos.Length - 1; i >= 0; i--)
                        {
                            var info = new FileInfo(videos[i]);
                            if (info.Length > 0)
                            {
                                videoPath = videos[i];
                                recording = Stopwatch.StartNew();
                                Console.WriteLine("recording_file_created=true");
                                break;
                            }
                        }
                    }
                    if (recording == null && DateTime.UtcNow - selectionAt > TimeSpan.FromSeconds(8))
                        throw new TimeoutException("The actual executable did not create an MP4 after selecting the synthetic window.");
                }

                if (recording != null)
                    AdvanceCaptureScenario();

                if (stopRequested)
                {
                    if (appProcess.HasExited)
                    {
                        var info = new FileInfo(videoPath);
                        if (!info.Exists || info.Length == 0)
                            throw new InvalidOperationException("The actual executable exited without finalizing a non-empty MP4.");
                        Console.WriteLine("actual_exe_stop=ok");
                        Console.WriteLine("occlusion_and_resume=ok");
                        Console.WriteLine("output=" + videoPath);
                        Console.WriteLine("recorded_seconds=" + recording.Elapsed.TotalSeconds.ToString("F2"));
                        result = 0;
                        timer.Stop();
                        Application.ExitThread();
                    }
                    else if (total.Elapsed > TimeSpan.FromSeconds(22))
                    {
                        throw new TimeoutException("The actual executable did not exit after receiving its second-launch stop request.");
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex.ToString();
                timer.Stop();
                Application.ExitThread();
            }
        }

        private void SelectSyntheticTarget(IntPtr picker)
        {
            IntPtr listBox = IntPtr.Zero;
            IntPtr selectButton = IntPtr.Zero;
            IntPtr cancelButton = IntPtr.Zero;
            IntPtr child = GetWindow(picker, GwChild);
            while (child != IntPtr.Zero)
            {
                var className = new StringBuilder(256);
                GetClassName(child, className, className.Capacity);
                var text = new StringBuilder(256);
                GetWindowText(child, text, text.Capacity);
                if (className.ToString().IndexOf("LISTBOX", StringComparison.OrdinalIgnoreCase) >= 0)
                    listBox = child;
                if (className.ToString().IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (text.ToString() == "Select") selectButton = child;
                    if (text.ToString() == "Cancel") cancelButton = child;
                }
                child = GetWindow(child, GwNext);
            }

            if (listBox == IntPtr.Zero || selectButton == IntPtr.Zero)
                return;

            int count = SendMessageInt(listBox, LbGetCount, IntPtr.Zero, IntPtr.Zero).ToInt32();
            for (int index = 0; index < count; index++)
            {
                int length = SendMessageInt(listBox, LbGetTextLength, new IntPtr(index), IntPtr.Zero).ToInt32();
                var itemText = new StringBuilder(Math.Max(1, length + 1));
                SendMessageText(listBox, LbGetText, new IntPtr(index), itemText);
                if (itemText.ToString().IndexOf("RScreenRec synthetic capture target", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                SendMessageInt(listBox, LbSetCurSel, new IntPtr(index), IntPtr.Zero);
                SendMessageInt(selectButton, BmClick, IntPtr.Zero, IntPtr.Zero);
                if (!targetSelected)
                {
                    targetSelected = true;
                    selectionAt = DateTime.UtcNow;
                    Console.WriteLine("picker_selection=synthetic_target");
                }
                return;
            }

            if (DateTime.UtcNow - pickerSeenAt > TimeSpan.FromSeconds(9) && cancelButton != IntPtr.Zero)
                SendMessageInt(cancelButton, BmClick, IntPtr.Zero, IntPtr.Zero);
        }

        private void AdvanceCaptureScenario()
        {
            double seconds = recording.Elapsed.TotalSeconds;
            if (!occlusionShown && seconds >= 0.8)
            {
                NativeRect bounds;
                if (!GetWindowRect(targetWindow, out bounds))
                    throw new InvalidOperationException("Could not read the synthetic target's bounds for occlusion.");
                occluder = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Bounds = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
                    TopMost = true,
                    ShowInTaskbar = false,
                    BackColor = Color.Magenta
                };
                occluder.Show();
                occluder.BringToFront();
                BringWindowToTop(occluder.Handle);
                occlusionShown = true;
                Console.WriteLine("occluder=shown_at=" + seconds.ToString("F2"));
            }
            if (occlusionShown && !occlusionHidden && seconds >= 2.2)
            {
                occluder.Hide();
                occlusionHidden = true;
                Console.WriteLine("occluder=hidden_at=" + seconds.ToString("F2"));
            }
            if (occlusionHidden && !resized && seconds >= 2.5)
            {
                if (!MoveWindow(targetWindow, 260, 160, 771, 437, true))
                    throw new InvalidOperationException("Could not resize and move the synthetic target after occlusion.");
                resized = true;
            }
            if (resized && !minimized && seconds >= 3.2)
            {
                ShowWindow(targetWindow, SwMinimize);
                minimized = true;
            }
            if (minimized && !restored && seconds >= 4.0)
            {
                ShowWindow(targetWindow, SwRestore);
                restored = true;
                Console.WriteLine("target=restored_after_minimize");
            }
            if (restored && !stopRequested && seconds >= 7.0)
            {
                if (!occlusionHidden || !resized || !minimized)
                    throw new InvalidOperationException("The occlusion, resize, minimize, and restore events did not all run.");
                keybd_event(VkShift, 0x2A, KeyUp, UIntPtr.Zero);
                using (Process stopSignal = Process.Start(new ProcessStartInfo(appPath) { UseShellExecute = false }))
                    if (stopSignal == null || !stopSignal.WaitForExit(5000) || stopSignal.ExitCode != 0)
                        throw new InvalidOperationException("The actual executable's second-launch stop request failed.");
                stopRequested = true;
                Console.WriteLine("stop_request=second_launch");
            }
        }

        public void Dispose()
        {
            timer.Dispose();
            if (occluder != null)
                occluder.Dispose();
        }
    }
}
