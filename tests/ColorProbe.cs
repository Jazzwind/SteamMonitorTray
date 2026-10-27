using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SteamMonitorTray;

internal static class ColorProbe {
    [StructLayout(LayoutKind.Sequential)] private struct Coord { public short x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public short left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BufferInfo { public Coord size, cursor; public short attributes; public Rect window; public Coord maximum; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleScreenBufferInfo(IntPtr console, out BufferInfo info);
    [DllImport("kernel32.dll")] private static extern bool ReadConsoleOutputAttribute(IntPtr console, [Out] short[] attributes, uint count, Coord origin, out uint read);
    public static int Main() {
        string python = Environment.GetEnvironmentVariable("SMT_COLOR_TEST_PYTHON") ?? "python.exe";
        string code = "import json, os, sys, pathlib, steam_monitor as m; m.COLORED_OUTPUT=True; m.init_color_output(sys.stdout); print(m.colorize('status_online', 'SMT_GREEN_PROBE')); pathlib.Path('fixture.color.json').write_text(json.dumps({'colored':m.COLOR_ENABLED,'stdout_tty':sys.stdout.isatty(),'stdin_tty':sys.stdin.isatty(),'no_color':bool(os.getenv('NO_COLOR'))}),encoding='utf-8')";
        IntPtr output = GetStdHandle(-11); BufferInfo before;
        if (!GetConsoleScreenBufferInfo(output, out before)) return 30;
        ProcessStartInfo info = new ProcessStartInfo(python, "-c " + Native.Quote(code)); info.UseShellExecute = false;
        using (Process child = Process.Start(info)) { child.WaitForExit(); if (child.ExitCode != 0) return child.ExitCode; }
        short[] attributes = new short[15]; uint read;
        bool green = ReadConsoleOutputAttribute(output, attributes, (uint)attributes.Length, before.cursor, out read) && read > 0 && (attributes[0] & 15) == 2;
        File.WriteAllText("fixture.color-attributes.txt", green ? "green" : "not-green");
        return green ? 0 : 31;
    }
}
