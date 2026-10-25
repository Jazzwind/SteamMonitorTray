using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace SteamMonitorTray {
    // Loaded only inside the dedicated interactive PowerShell. No background thread executes PowerShell code.
    public static class SessionBridge {
        private static readonly object gate = new object();
        private static StreamWriter writer;
        private static NamedPipeClientStream pipe;
        private static string pending;
        private static bool atPrompt;
        public static void Connect(string name, string token) {
            pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            pipe.Connect(15000); writer = Pipes.Writer(pipe);
            Send(new Wire("Hello", token));
            Thread thread = new Thread(ReadLoop); thread.IsBackground = true; thread.Start();
        }
        private static void ReadLoop() {
            try {
                using (StreamReader reader = Pipes.Reader(pipe)) {
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        Wire message = Json.ReadText<Wire>(line);
                        if (message.Kind != "Start" && message.Kind != "Exit") continue;
                        lock (gate) {
                            if (!atPrompt || pending != null) { Send(new Wire("Busy", "CommandRunning")); continue; }
                            pending = message.Kind;
                        }
                        Native.WakePrompt();
                    }
                }
            } catch (IOException) { } catch (ObjectDisposedException) { }
              catch (Exception ex) { Send(new Wire("Fault", ex.GetType().Name)); }
        }
        private static void Send(Wire message) {
            lock (gate) {
                try { if (writer != null) writer.WriteLine(Json.Text(message)); }
                catch (IOException) { } catch (ObjectDisposedException) { }
            }
        }
        public static string TakeRequest() { lock (gate) { string request = pending; pending = null; return request; } }
        public static void Prompt() { lock (gate) atPrompt = true; Send(new Wire("Prompt", "")); }
        public static void Busy() { lock (gate) atPrompt = false; Send(new Wire("ShellBusy", "")); }
        public static void RejectInput() { Send(new Wire("Busy", "UnsubmittedInput")); }
        public static void Started() { lock (gate) atPrompt = false; Send(new Wire("Started", "")); }
        public static void Stopped(int code) { Send(new Wire("Stopped", "") { Code = code }); }
        public static void Exiting() { lock (gate) atPrompt = false; Send(new Wire("ExitRequested", "")); }
    }
}
