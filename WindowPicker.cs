using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RScreenRec
{
    internal static class WindowPicker
    {
        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        internal static IntPtr SelectWindow()
        {
            var choices = new List<WindowChoice>();
            EnumWindows((hwnd, unused) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                int length = GetWindowTextLength(hwnd);
                if (length <= 0) return true;
                var title = new StringBuilder(length + 1);
                GetWindowText(hwnd, title, title.Capacity);
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return true;
                choices.Add(new WindowChoice { Handle = hwnd, Title = title.ToString(), ProcessId = pid });
                return true;
            }, IntPtr.Zero);

            using (var dialog = new Form())
            using (var list = new ListBox())
            using (var confirm = new Button())
            using (var cancel = new Button())
            {
                dialog.Text = "Select window - RScreenRec";
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new Size(540, 330);
                list.Location = new Point(12, 12);
                list.Size = new Size(516, 260);
                list.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
                foreach (var choice in choices) list.Items.Add(choice);
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                confirm.Text = "Select";
                confirm.Location = new Point(362, 286);
                confirm.DialogResult = DialogResult.OK;
                cancel.Text = "Cancel";
                cancel.Location = new Point(453, 286);
                cancel.DialogResult = DialogResult.Cancel;
                dialog.Controls.Add(list);
                dialog.Controls.Add(confirm);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = confirm;
                dialog.CancelButton = cancel;
                list.DoubleClick += (sender, args) => { if (list.SelectedItem != null) dialog.DialogResult = DialogResult.OK; };
                if (dialog.ShowDialog() != DialogResult.OK || list.SelectedItem == null) return IntPtr.Zero;
                var selected = (WindowChoice)list.SelectedItem;
                return IsWindow(selected.Handle) ? selected.Handle : IntPtr.Zero;
            }
        }

        private sealed class WindowChoice
        {
            public IntPtr Handle;
            public uint ProcessId;
            public string Title;
            public override string ToString() { return Title + " (PID " + ProcessId + ")"; }
        }
    }
}
