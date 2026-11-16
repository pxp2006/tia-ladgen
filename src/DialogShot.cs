using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class DialogShot
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

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
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static readonly StringBuilder Report = new StringBuilder();

    private static void Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : Path.GetTempPath();
        IntPtr target = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hWnd))
            {
                return true;
            }
            string title = Text(hWnd);
            if (title.StartsWith("Openness", StringComparison.OrdinalIgnoreCase))
            {
                target = hWnd;
                Report.AppendLine("FOUND hwnd=0x" + ((long)hWnd).ToString("X") + " title=" + title);
            }
            return true;
        }, IntPtr.Zero);

        if (target == IntPtr.Zero)
        {
            Report.AppendLine("NO_OPENNESS_WINDOW");
            File.WriteAllText(Path.Combine(outDir, "dialog.txt"), Report.ToString(), Encoding.UTF8);
            return;
        }

        Rect r;
        GetWindowRect(target, out r);
        Report.AppendLine("RECT " + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom);
        EnumChildWindows(target, delegate(IntPtr hWnd, IntPtr lParam)
        {
            Rect cr;
            GetWindowRect(hWnd, out cr);
            Report.AppendLine("CHILD hwnd=0x" + ((long)hWnd).ToString("X") + " class=" + Class(hWnd) + " text=" + Text(hWnd) + " rect=" + cr.Left + "," + cr.Top + "," + cr.Right + "," + cr.Bottom);
            return true;
        }, IntPtr.Zero);

        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        if (w > 0 && h > 0)
        {
            using (Bitmap bmp = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(target, hdc, 0);
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(Path.Combine(outDir, "dialog.png"), ImageFormat.Png);
                Report.AppendLine("SAVED dialog.png " + w + "x" + h);
            }
        }
        File.WriteAllText(Path.Combine(outDir, "dialog.txt"), Report.ToString(), Encoding.UTF8);
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
