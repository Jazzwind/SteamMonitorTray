using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace SteamMonitorTray {
    internal static class SessionHost {
        private static NamedPipeServerStream controller, shell;
        private static StreamWriter controllerWriter, shellWriter;
        private static readonly object controllerGate = new object(), shellGate = new object();
        private static Native.ControlHandler handler;
        private static IntPtr window;
        private static bool monitored, exiting;
        private static Process powerShell;
        private static SessionSpec spec;
        private static Timer minimizeTimer;
        private static void Send(Wire message) {
            lock (controllerGate) {
                try { if (controllerWriter != null) controllerWriter.WriteLine(Json.Text(message)); }
                catch (IOException) { } catch (ObjectDisposedException) { }
            }
        }
        private static void ToShell(string command) {
            lock (shellGate) {
                if (shellWriter == null) { Send(new Wire("Busy", "ShellNotReady")); return; }
                try { shellWriter.WriteLine(Json.Text(new Wire(command, ""))); }
                catch (IOException) { Send(new Wire("Fault", "ShellDisconnected")); }
            }
        }
        private static bool OnControl(uint kind) {
            if (kind == 0) { Send(new Wire("Interrupted", "")); return true; }
            return false;
        }
        private static void ReadShell() {
            try {
                shell.WaitForConnection();
                using (StreamReader reader = Pipes.Reader(shell)) {
                    Wire hello = Json.ReadText<Wire>(reader.ReadLine());
                    if (hello.Kind != "Hello" || hello.Value != spec.Token) return;
                    shellWriter = Pipes.Writer(shell);
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        Wire message = Json.ReadText<Wire>(line);
                        if (message.Kind == "Started") { monitored = true; Native.AllowClose(window, false); }
                        if (message.Kind == "Stopped") { monitored = false; Native.AllowClose(window, true); }
                        if (message.Kind == "ExitRequested") exiting = true;
                        Send(message);
                    }
                }
            } catch (IOException) { } catch (ObjectDisposedException) { }
        }
        private static void ReadController() {
            try {
                using (StreamReader reader = Pipes.Reader(controller)) {
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        Wire message = Json.ReadText<Wire>(line);
                        switch (message.Kind) {
                            case "Start": ToShell("Start"); break;
                            case "Exit":
                                if (monitored) Send(new Wire("Busy", "MonitorRunning")); else ToShell("Exit");
                                break;
                            case "Stop":
                                if (monitored && !Native.GenerateConsoleCtrlEvent(0, 0)) Send(new Wire("Fault", "CtrlCFailed"));
                                break;
                            case "Show": Native.ShowWindow(window, message.Value == "NoActivate" ? 4 : 9); if (message.Value != "NoActivate") Native.SetForegroundWindow(window); Send(new Wire("Visible", "")); break;
                            case "Hide": Native.ShowWindow(window, 0); Send(new Wire("Hidden", "")); break;
                        }
                    }
                }
                // An unexpected controller loss must not kill a running monitor or leave it inaccessible.
                Native.ShowWindow(window, 9); Native.AllowClose(window, true);
            } catch (IOException) { Native.ShowWindow(window, 9); Native.AllowClose(window, true); }
              catch (ObjectDisposedException) { }
        }
        public static int Main(string[] args) {
            try {
                if (args.Length != 1) return 2;
                spec = Json.Read<SessionSpec>(args[0]); L.SetLanguage(spec.Language); window = Native.GetConsoleWindow();
                if (window == IntPtr.Zero) return 3;
                Console.Title = "SteamMonitorTray — " + spec.Profile.Name;
                handler = OnControl; Native.SetConsoleCtrlHandler(handler, true);
                using (controller = Pipes.Server(spec.ControllerPipe))
                using (shell = Pipes.Server(spec.ShellPipe)) {
                    IAsyncResult connection = controller.BeginWaitForConnection(null, null);
                    if (!connection.AsyncWaitHandle.WaitOne(15000)) return 4;
                    controller.EndWaitForConnection(connection);
                    StreamReader authentication = Pipes.Reader(controller);
                    Wire hello = Json.ReadText<Wire>(authentication.ReadLine());
                    if (hello.Kind != "Hello" || hello.Value != spec.Token) return 5;
                    // The authentication line is the only line sent before Welcome; the reader cannot buffer subsequent commands.
                    controllerWriter = Pipes.Writer(controller);
                    Send(new Wire("Welcome", "") { Handle = window.ToInt64() });
                    Thread shellThread = new Thread(ReadShell); shellThread.IsBackground = true; shellThread.Start();
                    ProcessStartInfo info = new ProcessStartInfo(spec.ShellPath,
                        "-NoLogo -NoProfile -NoExit -ExecutionPolicy Bypass -File " + Native.Quote(Path.Combine(Files.Bin, "Session.ps1")) + " -SessionFile " + Native.Quote(args[0]));
                    info.UseShellExecute = false; info.WorkingDirectory = spec.Profile.WorkingDirectory;
                    powerShell = Process.Start(info);
                    Thread controlThread = new Thread(ReadController); controlThread.IsBackground = true; controlThread.Start();
                    minimizeTimer = new Timer(delegate {
                        // A hidden console may remain iconic. Hide/report only a visible minimized window.
                        if (Native.IsWindowVisible(window) && Native.IsIconic(window)) { Native.ShowWindow(window, 0); Send(new Wire("Hidden", "")); }
                    }, null, 100, 100);
                    powerShell.WaitForExit();
                    Send(new Wire("SessionEnded", exiting ? "Requested" : "UserExit") { Code = powerShell.ExitCode });
                    minimizeTimer.Dispose(); powerShell.Dispose();
                }
                return 0;
            } catch (Exception ex) {
                try { if (args.Length == 1) File.WriteAllText(args[0] + ".error", ex.GetType().Name + " " + ex.HResult); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                Files.Log("session", ex.GetType().Name); Send(new Wire("Fault", ex.GetType().Name)); return 1;
            }
        }
    }
}
