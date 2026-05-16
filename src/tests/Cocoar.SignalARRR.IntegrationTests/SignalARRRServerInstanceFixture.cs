using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    public class SignalARRRServerInstanceFixture : IDisposable {

        private Process? _serverProcess;
        private string? _diagnosticsLogFilePath;
        public string ServerUrl { get; private set; } = null!;

        public SignalARRRServerInstanceFixture() {
            // If environment variable is set (unified script or CI), use it directly
            var envUrl = Environment.GetEnvironmentVariable("SIGNALARRR_TEST_SERVER_URL");
            if (!string.IsNullOrEmpty(envUrl)) {
                ServerUrl = envUrl;
                return;
            }

            // Otherwise, start IntegrationTestServer as a child process
            var tfm = $"net{Environment.Version.Major}.0";
            var serverAssemblyPath = IntegrationTestServerPathResolver.GetAssemblyPath(tfm);
            var urlFile = Path.Combine(Path.GetTempPath(), $"signalarrr-test-{Guid.NewGuid()}.url");
            var diagnosticsLogFilePath = Environment.GetEnvironmentVariable("SIGNALARRR_TEST_DIAGNOSTICS_LOG_FILE");
            _diagnosticsLogFilePath = IsDiagnosticsEnabled()
                ? (!string.IsNullOrWhiteSpace(diagnosticsLogFilePath)
                    ? diagnosticsLogFilePath
                    : Path.Combine(Path.GetTempPath(), $"signalarrr-diagnostics-{Guid.NewGuid()}.log"))
                : null;

            WriteDiagnostics($"fixture using-prebuilt-server tfm={tfm} assembly={serverAssemblyPath}");

            _serverProcess = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "dotnet",
                    Arguments = $"\"{serverAssemblyPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            _serverProcess.StartInfo.Environment["SERVER_URL_FILE"] = urlFile;
            if (!string.IsNullOrWhiteSpace(_diagnosticsLogFilePath)) {
                _serverProcess.StartInfo.Environment["SIGNALARRR_DIAGNOSTICS_LOG_FILE"] = _diagnosticsLogFilePath;
            }

            var startStopwatch = Stopwatch.StartNew();
            _serverProcess.Start();
            WriteDiagnostics($"fixture server-process-started pid={_serverProcess.Id}");

            // Wait for server to write its URL (up to 300s for first build + start)
            var deadline = DateTime.UtcNow.AddSeconds(300);
            while (DateTime.UtcNow < deadline) {
                if (File.Exists(urlFile)) {
                    var content = File.ReadAllText(urlFile).Trim();
                    if (!string.IsNullOrEmpty(content)) {
                        ServerUrl = content;
                        startStopwatch.Stop();
                        WriteDiagnostics($"fixture server-ready url={ServerUrl} elapsedMs={startStopwatch.ElapsedMilliseconds}");
                        try { File.Delete(urlFile); } catch { }
                        return;
                    }
                }
                if (_serverProcess.HasExited) {
                    var stderr = _serverProcess.StandardError.ReadToEnd();
                    throw new InvalidOperationException(
                        $"IntegrationTestServer exited with code {_serverProcess.ExitCode}:\n{stderr}\n{BuildDiagnosticsSummary()}");
                }
                Thread.Sleep(500);
            }

            try { _serverProcess.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(
                $"IntegrationTestServer did not start within 300 seconds. Assembly: {serverAssemblyPath}\n{BuildDiagnosticsSummary()}");
        }
        public void Dispose() {
            if (_serverProcess != null && !_serverProcess.HasExited) {
                var shutdownStopwatch = Stopwatch.StartNew();
                try {
                    _serverProcess.Kill(entireProcessTree: true);
                    _serverProcess.WaitForExit(5000);
                } catch {
                }
                shutdownStopwatch.Stop();
                WriteDiagnostics($"fixture server-process-stopped elapsedMs={shutdownStopwatch.ElapsedMilliseconds} exited={_serverProcess.HasExited}");
                _serverProcess.Dispose();
            }

            if (!string.IsNullOrWhiteSpace(_diagnosticsLogFilePath) && File.Exists(_diagnosticsLogFilePath)) {
                Console.WriteLine("=== SignalARRR diagnostics ===");
                Console.WriteLine(File.ReadAllText(_diagnosticsLogFilePath));
                Console.WriteLine("=== End SignalARRR diagnostics ===");
            }
        }

        private static bool IsDiagnosticsEnabled() {
            var value = Environment.GetEnvironmentVariable("SIGNALARRR_TEST_DIAGNOSTICS");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private void WriteDiagnostics(string message) {
            if (string.IsNullOrWhiteSpace(_diagnosticsLogFilePath)) {
                return;
            }

            var directory = Path.GetDirectoryName(_diagnosticsLogFilePath);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Directory.CreateDirectory(directory);
            }

            for (int attempt = 0; attempt < 3; attempt++) {
                try {
                    File.AppendAllText(
                        _diagnosticsLogFilePath,
                        $"{DateTime.UtcNow:O} [Fixture] {message}{Environment.NewLine}");
                    break;
                } catch (IOException) when (attempt < 2) {
                    Thread.Sleep(10);
                }
            }
        }

        private string BuildDiagnosticsSummary() {
            if (string.IsNullOrWhiteSpace(_diagnosticsLogFilePath) || !File.Exists(_diagnosticsLogFilePath)) {
                return "Diagnostics log unavailable.";
            }

            return $"Diagnostics log:{Environment.NewLine}{File.ReadAllText(_diagnosticsLogFilePath)}";
        }
    }

    [CollectionDefinition("Simple")]
    public class SimpleSignalARRCollection : ICollectionFixture<SignalARRRServerInstanceFixture> {

    }
}
