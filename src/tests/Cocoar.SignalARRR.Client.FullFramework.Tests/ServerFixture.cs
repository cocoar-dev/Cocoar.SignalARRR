using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Cocoar.SignalARRR.TestInfrastructure;
using Xunit;

namespace Cocoar.SignalARRR.Client.FullFramework.Tests {
    public class ServerFixture : IDisposable {

        private Process _serverProcess;
        private readonly StringBuilder _serverOutput = new StringBuilder();
        public string ServerUrl { get; private set; }

        public ServerFixture() {
            var envUrl = Environment.GetEnvironmentVariable("SIGNALARRR_TEST_SERVER_URL");
            if (!string.IsNullOrEmpty(envUrl)) {
                ServerUrl = envUrl;
                return;
            }

            var serverProjectDir = FindServerProjectDir();
            var urlFile = Path.Combine(Path.GetTempPath(), "signalarrr-ff-test-" + Guid.NewGuid() + ".url");

            _serverProcess = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "dotnet",
                    Arguments = "run --project \"" + serverProjectDir + "\" -c Release --framework net10.0",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            _serverProcess.StartInfo.Environment["SERVER_URL_FILE"] = urlFile;

            // Both pipes have to be drained while the child runs. `dotnet run` writes its restore
            // and build output to stdout long before the server starts listening, and once the
            // pipe buffer is full the child blocks in its own write — it never reaches the line
            // that publishes SERVER_URL, so every test in the collection times out below.
            _serverProcess.OutputDataReceived += AppendOutput;
            _serverProcess.ErrorDataReceived += AppendOutput;
            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline) {
                if (ServerUrlFile.TryRead(urlFile, out var content)) {
                    ServerUrl = content;
                    try { File.Delete(urlFile); } catch { }
                    return;
                }
                if (_serverProcess.HasExited) {
                    // Lets the asynchronous readers flush what the child wrote before exiting.
                    _serverProcess.WaitForExit();
                    throw new InvalidOperationException(
                        "IntegrationTestServer exited with code " + _serverProcess.ExitCode + ":\n" + SnapshotOutput());
                }
                Thread.Sleep(500);
            }

            try { _serverProcess.Kill(); } catch { }
            throw new TimeoutException(
                "IntegrationTestServer did not start within 120 seconds. Output so far:\n" + SnapshotOutput());
        }

        private void AppendOutput(object sender, DataReceivedEventArgs e) {
            if (e.Data == null)
                return;
            lock (_serverOutput) {
                _serverOutput.AppendLine(e.Data);
            }
        }

        private string SnapshotOutput() {
            lock (_serverOutput) {
                return _serverOutput.Length == 0 ? "(no output)" : _serverOutput.ToString();
            }
        }

        private static string FindServerProjectDir() {
            var dir = AppContext.BaseDirectory;
            while (dir != null) {
                var candidate = Path.Combine(dir, "src", "tests", "IntegrationTestServer");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Path.GetDirectoryName(dir);
            }
            throw new DirectoryNotFoundException(
                "Could not find IntegrationTestServer project. Searched from: " + AppContext.BaseDirectory);
        }

        public void Dispose() {
            if (_serverProcess != null && !_serverProcess.HasExited) {
                try { _serverProcess.Kill(); } catch { }
                _serverProcess.Dispose();
            }
        }
    }

    [CollectionDefinition("FullFramework")]
    public class FullFrameworkCollection : ICollectionFixture<ServerFixture> { }
}
