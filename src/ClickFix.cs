using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class ClickFix
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
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint BmClick = 0x00F5;

    private static void Main(string[] args)
    {
        string outFile = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "clickfix.txt");
        StringBuilder report = new StringBuilder();
        IntPtr dialog = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hWnd)) { return true; }
            if (Text(hWnd).StartsWith("Openness", StringComparison.OrdinalIgnoreCase))
            {
                dialog = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        if (dialog == IntPtr.Zero)
        {
            report.AppendLine("NO_DIALOG");
            File.WriteAllText(outFile, report.ToString(), Encoding.UTF8);
            return;
        }

        List<IntPtr> handles = new List<IntPtr>();
        List<int> lefts = new List<int>();
        EnumChildWindows(dialog, delegate(IntPtr hWnd, IntPtr lParam)
        {
            if (Class(hWnd).IndexOf("BUTTON", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Rect r;
                GetWindowRect(hWnd, out r);
                handles.Add(hWnd);
                lefts.Add(r.Left);
                report.AppendLine("BUTTON 0x" + ((long)hWnd).ToString("X") + " left=" + r.Left + " rect=" + r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom);
            }
            return true;
        }, IntPtr.Zero);

        if (handles.Count < 2)
        {
            report.AppendLine("BUTTON_COUNT=" + handles.Count);
            File.WriteAllText(outFile, report.ToString(), Encoding.UTF8);
            return;
        }

        // sort by x position (left -> right): [0]=allow once, [1]=allow always, [2]=deny
        for (int i = 0; i < lefts.Count; i++)
        {
            for (int j = i + 1; j < lefts.Count; j++)
            {
                if (lefts[j] < lefts[i])
                {
                    int tmp = lefts[i]; lefts[i] = lefts[j]; lefts[j] = tmp;
                    IntPtr th = handles[i]; handles[i] = handles[j]; handles[j] = th;
                }
            }
        }
        IntPtr target = handles.Count >= 2 ? handles[1] : handles[0];
        SendMessage(target, BmClick, IntPtr.Zero, IntPtr.Zero);
        report.AppendLine("CLICKED_MIDDLE 0x" + ((long)target).ToString("X"));
        System.Threading.Thread.Sleep(600);
        report.AppendLine("DIALOG_STILL_VISIBLE=" + IsWindowVisible(dialog));
        File.WriteAllText(outFile, report.ToString(), Encoding.UTF8);
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
