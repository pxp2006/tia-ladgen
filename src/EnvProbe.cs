using System;
using System.Diagnostics;
using System.IO;
using System.Text;

internal static class EnvProbe
{
    private static void Main(string[] args)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("procs=" + Process.GetProcesses().Length);
        sb.AppendLine("session=" + Process.GetCurrentProcess().SessionId);
        sb.AppendLine("is64=" + Environment.Is64BitProcess);
        sb.AppendLine("user=" + Environment.UserName);
        sb.AppendLine("exe=" + Process.GetCurrentProcess().MainModule.FileName);
        File.WriteAllText(args[0], sb.ToString(), Encoding.UTF8);
    }
}
