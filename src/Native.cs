using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamMonitorTray {
    public static class Native {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo {
            public int cb; public string reserved, desktop, title;
            public int x, y, xSize, ySize, xChars, yChars, fill, flags;
            public short show, reservedSize; public IntPtr reservedData, input, output, error;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInfo { public IntPtr process, thread; public int pid, tid; }
        [StructLayout(LayoutKind.Explicit, Size = 20)]
        private struct InputRecord {
            [FieldOffset(0)] public short type;
            [FieldOffset(4)] public int down;
            [FieldOffset(8)] public short repeat;
            [FieldOffset(10)] public short virtualKey;
            [FieldOffset(12)] public short scan;
            [FieldOffset(14)] public char character;
            [FieldOffset(16)] public int control;
        }
        public delegate bool ControlHandler(uint kind);
        public delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] public static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool SetConsoleCtrlHandler(ControlHandler handler, bool add);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GenerateConsoleCtrlEvent(uint evt, uint group);
        [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int which);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WriteConsoleInputW(IntPtr input, InputRecord[] records, uint length, out uint written);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr window, bool reset);
        [DllImport("user32.dll")] private static extern uint EnableMenuItem(IntPtr menu, uint item, uint flags);
        [DllImport("user32.dll")] private static extern bool DrawMenuBar(IntPtr window);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon);
        public static Process StartHiddenConsole(string executable, string arguments, string directory) {
            StartupInfo info = new StartupInfo(); info.cb = Marshal.SizeOf(info); info.flags = 1; info.show = 0;
            ProcessInfo result;
            if (!CreateProcess(executable, new StringBuilder(Quote(executable) + " " + arguments), IntPtr.Zero, IntPtr.Zero, false, 0x10, IntPtr.Zero, directory, ref info, out result))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            CloseHandle(result.thread); CloseHandle(result.process);
            return Process.GetProcessById(result.pid);
        }
        public static void WakePrompt() {
            InputRecord key = new InputRecord(); key.type = 1; key.down = 1; key.repeat = 1; key.virtualKey = 0x87;
            InputRecord up = key; up.down = 0;
            uint written;
            if (!WriteConsoleInputW(GetStdHandle(-10), new InputRecord[] { key, up }, 2, out written) || written != 2)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        public static void AllowClose(IntPtr window, bool allow) {
            EnableMenuItem(GetSystemMenu(window, false), 0xF060, (uint)(allow ? 0 : 1)); DrawMenuBar(window);
        }
        public static string Quote(string value) {
            StringBuilder result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); slashes = 0; continue; }
                result.Append('\\', slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }
    }
}
