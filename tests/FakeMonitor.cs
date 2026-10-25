using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal static class FakeMonitor {
    private delegate bool Handler(uint kind);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(Handler handler, bool add);
    private static Handler handler;
    private static readonly ManualResetEvent stopped = new ManualResetEvent(false);
    public static int Main(string[] args) {
        string config = args[Array.IndexOf(args, "--config-file") + 1];
        string marker = Path.Combine(Path.GetDirectoryName(config), "fixture");
        handler = delegate(uint kind) { if (kind == 0) { stopped.Set(); return true; } return false; };
        SetConsoleCtrlHandler(handler, true);
        File.AppendAllText(marker + ".starts", "start\n");
        Console.WriteLine("Fixture monitor: Ctrl+C completes cleanup. 测试输出。");
        if (File.ReadAllText(config).Contains("TEST_FAIL")) return 42;
        stopped.WaitOne();
        File.WriteAllText(marker + ".cleanup", "CTRL_C_EVENT\ncleanup-complete");
        Console.WriteLine("Fixture cleanup complete."); return 0;
    }
}
