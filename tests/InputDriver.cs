// Test-only input driver. Attaches exclusively to the PID supplied by the fixture harness.
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
internal static class InputDriver {
    [StructLayout(LayoutKind.Explicit, Size = 20)] private struct Record {
        [FieldOffset(0)] public short type;
        [FieldOffset(4)] public int down;
        [FieldOffset(8)] public short repeat;
        [FieldOffset(10)] public short key;
        [FieldOffset(14)] public char character;
    }
    private delegate bool Handler(uint kind);
    private static Handler handler;
    [DllImport("kernel32.dll")] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(int pid);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool WriteConsoleInputW(IntPtr handle, Record[] records, uint count, out uint written);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleCtrlHandler(Handler callback, bool add);
    [DllImport("kernel32.dll")] private static extern bool GenerateConsoleCtrlEvent(uint kind, uint group);
    public static int Main(string[] args) {
        if (args.Length != 3) return 2;
        FreeConsole(); if (!AttachConsole(Int32.Parse(args[0]))) return 3;
        if (args[1] == "ctrlc") {
            handler = delegate(uint kind) { return kind == 0; }; SetConsoleCtrlHandler(handler, true);
            bool sent = GenerateConsoleCtrlEvent(0, 0); Thread.Sleep(150); FreeConsole(); return sent ? 0 : 4;
        }
        string text = Encoding.UTF8.GetString(Convert.FromBase64String(args[2]));
        Record[] records = new Record[text.Length * 2];
        for (int i = 0; i < text.Length; i++) {
            char c = text[i] == '\n' ? '\r' : text[i];
            Record r = new Record { type = 1, down = 1, repeat = 1, character = c, key = (short)(c == '\r' ? 13 : 0) };
            records[2 * i] = r; r.down = 0; records[2 * i + 1] = r;
        }
        uint written; bool success = WriteConsoleInputW(GetStdHandle(-10), records, (uint)records.Length, out written);
        FreeConsole(); return success && written == records.Length ? 0 : 5;
    }
}
