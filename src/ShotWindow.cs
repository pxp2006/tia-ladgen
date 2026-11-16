using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class ShotWindow
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
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    private static void Main(string[] args)
    {
        string outFile = args[0];
        string match = args.Length > 1 ? args[1] : "Siemens";
        IntPtr target = IntPtr.Zero;
        StringBuilder log = new StringBuilder();
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hWnd)) { return true; }
            string title = Text(hWnd);
            string cls = Class(hWnd);
            if (title.StartsWith(match, StringComparison.OrdinalIgnoreCase))
            {
                log.AppendLine("FOUND 0x" + ((long)hWnd).ToString("X") + " class=" + cls + " title=" + title);
                if (target == IntPtr.Zero) { target = hWnd; }
            }
            return true;
        }, IntPtr.Zero);
        if (target == IntPtr.Zero)
        {
            File.WriteAllText(outFile + ".log", log + "\nNO_WINDOW", Encoding.UTF8);
            return;
        }
        Rect r;
        GetWindowRect(target, out r);
        int w = Math.Max(100, r.Right - r.Left);
        int h = Math.Max(100, r.Bottom - r.Top);
        log.AppendLine("RECT " + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom);
        using (Bitmap bmp = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(target, hdc, 0);
                g.ReleaseHdc(hdc);
            }
            bmp.Save(outFile, ImageFormat.Png);
        }
        log.AppendLine("SAVED " + outFile);
        File.WriteAllText(outFile + ".log", log.ToString(), Encoding.UTF8);
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
