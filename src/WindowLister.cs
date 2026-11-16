using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class WindowLister
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static void Main(string[] args)
    {
        uint targetPid = uint.Parse(args[0]);
        StringBuilder sb = new StringBuilder();
        EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
        {
            uint pid;
            GetWindowThreadProcessId(hWnd, out pid);
            if (pid != targetPid)
            {
                return true;
            }
            StringBuilder title = new StringBuilder(512);
            GetWindowText(hWnd, title, title.Capacity);
            StringBuilder cls = new StringBuilder(512);
            GetClassName(hWnd, cls, cls.Capacity);
            Rect r;
            GetWindowRect(hWnd, out r);
            sb.AppendLine(string.Format("hwnd=0x{0:X} visible={1} class={2} rect={3},{4},{5},{6} title={7}",
                (long)hWnd, IsWindowVisible(hWnd), cls, r.Left, r.Top, r.Right, r.Bottom, title));
            return true;
        }, IntPtr.Zero);
        File.WriteAllText(args[1], sb.ToString(), Encoding.UTF8);
    }
}
