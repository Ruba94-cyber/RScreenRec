using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class SyntheticCaptureTargetForm : Form
{
    private int frame;

    internal SyntheticCaptureTargetForm(int width, int height)
    {
        Text = "RScreenRec synthetic capture target";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(140, 140, width, height);
        TopMost = true;
        ShowInTaskbar = true;
        BackColor = Color.Black;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Rectangle area = ClientRectangle;
        if (area.Width <= 0 || area.Height <= 0)
        {
            base.OnPaint(e);
            return;
        }

        using (var brush = new LinearGradientBrush(area,
            Color.FromArgb((frame * 29) % 256, 40, 160),
            Color.FromArgb(20, (frame * 17) % 256, 230), 30f))
            e.Graphics.FillRectangle(brush, area);
        using (var pen = new Pen(Color.White, 4f))
            e.Graphics.DrawRectangle(pen, 6, 6, Math.Max(1, area.Width - 13), Math.Max(1, area.Height - 13));
        using (var font = new Font("Segoe UI", 18f, FontStyle.Bold))
        using (var text = new SolidBrush(Color.White))
        {
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            e.Graphics.DrawString("SYNTHETIC " + frame, font, text, 18, 18);
        }
        base.OnPaint(e);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 2)
            return;
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
        catch (EntryPointNotFoundException) { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var timer = new Timer { Interval = 100 })
        using (var form = new SyntheticCaptureTargetForm(int.Parse(args[0]), int.Parse(args[1])))
        {
            timer.Tick += (sender, eventArgs) => { form.frame++; form.Invalidate(); };
            form.Shown += (sender, eventArgs) => timer.Start();
            Application.Run(form);
        }
    }

    [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
}
