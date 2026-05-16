using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    public sealed class MultiNodeSignalARRRServerFixture : IDisposable {
        private readonly string _redisContainerName;
        private readonly int _redisPort;
        private readonly Process _server1;
        private readonly Process _server2;
        private readonly TimeSpan? _heartbeatInterval;
        private readonly TimeSpan? _nodeTimeout;

        public string ServerUrl1 { get; }
        public string ServerUrl2 { get; }

        public MultiNodeSignalARRRServerFixture() : this(null, null) {
        }

        internal MultiNodeSignalARRRServerFixture(TimeSpan? heartbeatInterval, TimeSpan? nodeTimeout) {
            _heartbeatInterval = heartbeatInterval;
            _nodeTimeout = nodeTimeout;
            _redisPort = GetFreePort();
            _redisContainerName = $"signalarrr-redis-{Guid.NewGuid():N}";
            StartRedisContainer(_redisContainerName, _redisPort);
            WaitForPort(_redisPort);

            var serverProjectDir = FindServerProjectDir();
            var tfm = $"net{Environment.Version.Major}.0";
            IntegrationTestServerBuildCoordinator.EnsureBuilt(serverProjectDir, tfm);

            var channelPrefix = $"signalarrr-tests-{Guid.NewGuid():N}";
            var connectionString = $"127.0.0.1:{_redisPort},abortConnect=false";

            _server1 = StartServerProcess(serverProjectDir, tfm, connectionString, channelPrefix, "node-1", _heartbeatInterval, _nodeTimeout, out var serverUrl1);
            _server2 = StartServerProcess(serverProjectDir, tfm, connectionString, channelPrefix, "node-2", _heartbeatInterval, _nodeTimeout, out var serverUrl2);

            ServerUrl1 = serverUrl1;
            ServerUrl2 = serverUrl2;
        }

        public void Dispose() {
            StopProcess(_server1);
            StopProcess(_server2);
            StopRedisContainer(_redisContainerName);
        }

        public void KillServer1() => StopProcess(_server1);

        public void KillServer2() => StopProcess(_server2);

        private static Process StartServerProcess(
            string serverProjectDir,
            string tfm,
            string connectionString,
            string channelPrefix,
            string nodeId,
            TimeSpan? heartbeatInterval,
            TimeSpan? nodeTimeout,
            out string serverUrl) {
            var urlFile = Path.Combine(Path.GetTempPath(), $"signalarrr-test-{Guid.NewGuid()}.url");

            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{serverProjectDir}\" -c Debug --framework {tfm} --no-build",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.Environment["SERVER_URL_FILE"] = urlFile;
            process.StartInfo.Environment["SIGNALARRR_BACKPLANE_CONNECTION_STRING"] = connectionString;
            process.StartInfo.Environment["SIGNALARRR_BACKPLANE_CHANNEL_PREFIX"] = channelPrefix;
            process.StartInfo.Environment["SIGNALARRR_BACKPLANE_NODE_ID"] = nodeId;
            if (heartbeatInterval.HasValue) {
                process.StartInfo.Environment["SIGNALARRR_BACKPLANE_HEARTBEAT_INTERVAL_MS"] = ((int)heartbeatInterval.Value.TotalMilliseconds).ToString();
            }
            if (nodeTimeout.HasValue) {
                process.StartInfo.Environment["SIGNALARRR_BACKPLANE_NODE_TIMEOUT_MS"] = ((int)nodeTimeout.Value.TotalMilliseconds).ToString();
            }
            process.Start();

            serverUrl = WaitForUrl(process, urlFile);
            return process;
        }

        private static string WaitForUrl(Process process, string urlFile) {
            var deadline = DateTime.UtcNow.AddSeconds(300);
            while (DateTime.UtcNow < deadline) {
                if (File.Exists(urlFile)) {
                    var content = File.ReadAllText(urlFile).Trim();
                    if (!string.IsNullOrEmpty(content)) {
                        try { File.Delete(urlFile); } catch { }
                        return content;
                    }
                }

                if (process.HasExited) {
                    var stderr = process.StandardError.ReadToEnd();
                    throw new InvalidOperationException($"IntegrationTestServer exited with code {process.ExitCode}:{Environment.NewLine}{stderr}");
                }

                Thread.Sleep(500);
            }

            StopProcess(process);
            throw new TimeoutException("IntegrationTestServer did not start within 300 seconds.");
        }

        private static string FindServerProjectDir() {
            var dir = AppContext.BaseDirectory;
            while (dir != null) {
                var candidate = Path.Combine(dir, "src", "tests", "IntegrationTestServer");
                if (Directory.Exists(candidate)) {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException($"Could not find IntegrationTestServer project. Searched from: {AppContext.BaseDirectory}");
        }
        private static int GetFreePort() {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        private static void StartRedisContainer(string containerName, int hostPort) {
            StopRedisContainer(containerName);

            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "docker",
                    Arguments = $"run -d --name {containerName} -p {hostPort}:6379 redis:7-alpine",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0) {
                throw new InvalidOperationException($"Could not start Redis container: {process.StandardError.ReadToEnd()}");
            }
        }

        private static void WaitForPort(int port) {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline) {
                try {
                    using var client = new TcpClient();
                    client.Connect(IPAddress.Loopback, port);
                    if (client.Connected) {
                        return;
                    }
                } catch {
                }

                Thread.Sleep(250);
            }

            throw new TimeoutException($"Redis container did not open port {port} in time.");
        }

        private static void StopRedisContainer(string containerName) {
            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "docker",
                    Arguments = $"rm -f {containerName}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit();
        }

        private static void StopProcess(Process? process) {
            if (process == null) {
                return;
            }

            try {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                }
            } catch {
            }

            process.Dispose();
        }
    }

    [CollectionDefinition("Backplane")]
    public sealed class BackplaneSignalARRCollection : ICollectionFixture<MultiNodeSignalARRRServerFixture> {
    }
}
