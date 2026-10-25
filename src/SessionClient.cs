using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace SteamMonitorTray {
    public sealed class SessionClient : IDisposable {
        private NamedPipeClientStream pipe;
        private StreamWriter writer;
        private readonly object gate = new object();
        private string specFile;
        public Process Process { get; private set; }
        public IntPtr Window { get; private set; }
        public event Action<Wire> Message;
        public bool Alive { get { try { return Process != null && !Process.HasExited; } catch (InvalidOperationException) { return false; } } }
        private void Emit(Wire message) { Action<Wire> handler = Message; if (handler != null) handler(message); }
        public void Start(Profile profile, Settings settings, string runtimeDirectory) {
            Files.EnsurePrivateDirectory(runtimeDirectory);
            string key = Guid.NewGuid().ToString("N");
            SessionSpec spec = new SessionSpec { Profile = profile.Copy(), ShellPath = settings.ShellPath, Language = L.Resolve(settings.Language, L.PreferredLanguages()), Token = Guid.NewGuid().ToString("N"), ControllerPipe = "SMT.C." + key, ShellPipe = "SMT.S." + key };
            specFile = Path.Combine(runtimeDirectory, key + ".json"); Json.Save(specFile, spec);
            Process = Native.StartHiddenConsole(Path.Combine(Files.Bin, "SteamMonitorTray.Session.exe"), Native.Quote(specFile), profile.WorkingDirectory);
            Thread thread = new Thread(delegate() {
                try {
                    pipe = new NamedPipeClientStream(".", spec.ControllerPipe, PipeDirection.InOut, PipeOptions.Asynchronous);
                    pipe.Connect(15000); writer = Pipes.Writer(pipe); writer.WriteLine(Json.Text(new Wire("Hello", spec.Token)));
                    using (StreamReader reader = Pipes.Reader(pipe)) {
                        string line;
                        while ((line = reader.ReadLine()) != null) {
                            Wire message = Json.ReadText<Wire>(line);
                            if (message.Kind == "Welcome") Window = new IntPtr(message.Handle);
                            Emit(message);
                        }
                    }
                } catch (IOException) { Emit(new Wire("Disconnected", "")); }
                  catch (TimeoutException) { Emit(new Wire("Fault", "SessionConnectTimeout") { Code = Alive ? -1 : Process.ExitCode }); }
                  catch (ObjectDisposedException) { }
                finally { Emit(new Wire("ConnectionEnded", "")); CleanupSpec(); }
            });
            thread.IsBackground = true; thread.Start();
        }
        public bool Send(string command) {
            return Send(command, "");
        }
        public bool Send(string command, string value) {
            lock (gate) {
                try { if (writer == null) return false; writer.WriteLine(Json.Text(new Wire(command, value))); return true; }
                catch (IOException) { return false; } catch (ObjectDisposedException) { return false; }
            }
        }
        private void CleanupSpec() { try { if (specFile != null) File.Delete(specFile); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        public void Dispose() {
            lock (gate) { if (pipe != null) pipe.Dispose(); }
            if (Process != null) Process.Dispose(); CleanupSpec();
        }
    }
}
