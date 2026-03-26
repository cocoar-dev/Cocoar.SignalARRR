using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace Cocoar.SignalARRR.Client.FullFramework.Tests {
    public class ServerFixture : IDisposable {

        private Process _serverProcess;
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
            _serverProcess.Start();

            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline) {
                if (File.Exists(urlFile)) {
                    var content = File.ReadAllText(urlFile).Trim();
                    if (!string.IsNullOrEmpty(content)) {
                        ServerUrl = content;
                        try { File.Delete(urlFile); } catch { }
                        return;
                    }
                }
                if (_serverProcess.HasExited) {
                    var stderr = _serverProcess.StandardError.ReadToEnd();
                    throw new InvalidOperationException(
                        "IntegrationTestServer exited with code " + _serverProcess.ExitCode + ":\n" + stderr);
                }
                Thread.Sleep(500);
            }

            try { _serverProcess.Kill(); } catch { }
            throw new TimeoutException("IntegrationTestServer did not start within 120 seconds.");
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
