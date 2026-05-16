using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    public class SignalARRRServerInstanceFixture : IDisposable {

        private Process? _serverProcess;
        public string ServerUrl { get; private set; } = null!;

        public SignalARRRServerInstanceFixture() {
            // If environment variable is set (unified script or CI), use it directly
            var envUrl = Environment.GetEnvironmentVariable("SIGNALARRR_TEST_SERVER_URL");
            if (!string.IsNullOrEmpty(envUrl)) {
                ServerUrl = envUrl;
                return;
            }

            // Otherwise, start IntegrationTestServer as a child process
            var serverProjectDir = FindServerProjectDir();
            var urlFile = Path.Combine(Path.GetTempPath(), $"signalarrr-test-{Guid.NewGuid()}.url");

            // Detect the current target framework from the runtime to pass to dotnet run
            var tfm = $"net{Environment.Version.Major}.0";
            IntegrationTestServerBuildCoordinator.EnsureBuilt(serverProjectDir, tfm);

            _serverProcess = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{serverProjectDir}\" -c Debug --framework {tfm} --no-build",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            _serverProcess.StartInfo.Environment["SERVER_URL_FILE"] = urlFile;

            _serverProcess.Start();

            // Wait for server to write its URL (up to 300s for first build + start)
            var deadline = DateTime.UtcNow.AddSeconds(300);
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
                        $"IntegrationTestServer exited with code {_serverProcess.ExitCode}:\n{stderr}");
                }
                Thread.Sleep(500);
            }

            try { _serverProcess.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(
                $"IntegrationTestServer did not start within 300 seconds. Project: {serverProjectDir}");
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
                "Could not find IntegrationTestServer project. " +
                $"Searched from: {AppContext.BaseDirectory}");
        }
        public void Dispose() {
            if (_serverProcess != null && !_serverProcess.HasExited) {
                try { _serverProcess.Kill(entireProcessTree: true); } catch { }
                _serverProcess.Dispose();
            }
        }
    }

    [CollectionDefinition("Simple")]
    public class SimpleSignalARRCollection : ICollectionFixture<SignalARRRServerInstanceFixture> {

    }
}
