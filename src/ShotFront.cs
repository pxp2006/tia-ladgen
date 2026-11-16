using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class ShotFront
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private static void Main(string[] args)
    {
        string outFile = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "tia.png");
        IntPtr target = IntPtr.Zero;
        StringBuilder log = new StringBuilder();
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            string cls = Class(hWnd);
            string title = Text(hWnd);
            if (cls.IndexOf("WindowsForms10.Window", StringComparison.OrdinalIgnoreCase) >= 0 && title.StartsWith("Siemens", StringComparison.OrdinalIgnoreCase))
            {
                target = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        if (target == IntPtr.Zero)
        {
            File.WriteAllText(outFile + ".txt", "NO_TIA_WINDOW", Encoding.UTF8);
            return;
        }
        ShowWindow(target, 3); // SW_MAXIMIZE
        BringWindowToTop(target);
        SetForegroundWindow(target);
        System.Threading.Thread.Sleep(1200);
        Rect r;
        GetWindowRect(target, out r);
        int w = Math.Max(100, r.Right - r.Left);
        int h = Math.Max(100, r.Bottom - r.Top);
        log.AppendLine("WINDOW " + Text(target) + " rect=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom);
        using (Bitmap bmp = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h));
            }
            bmp.Save(outFile, ImageFormat.Png);
        }
        log.AppendLine("SAVED " + outFile);
        File.WriteAllText(outFile + ".txt", log.ToString(), Encoding.UTF8);
    }

    private static string Text(IntPtr hWnd)
    {
        StringBuilder sb = new StringBuilder(512);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string Class(IntPtr hWnd)
    {
        StringBuilder sb = new StringBuilder(512);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
