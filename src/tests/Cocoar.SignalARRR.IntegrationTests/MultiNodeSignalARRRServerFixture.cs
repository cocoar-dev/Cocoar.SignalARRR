using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Cocoar.SignalARRR.TestInfrastructure;
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

        /// <summary>Node id of the server behind <see cref="ServerUrl1"/>.</summary>
        public const string NodeId1 = "node-1";

        /// <summary>Node id of the server behind <see cref="ServerUrl2"/>.</summary>
        public const string NodeId2 = "node-2";

        /// <summary>
        /// Connection string of this fixture's Redis instance, so a test can act on the backplane's
        /// state directly — for instance to make a node look dead while it keeps running.
        /// </summary>
        public string RedisConnectionString { get; }

        /// <summary>Key prefix this fixture's nodes use, unique per fixture.</summary>
        public string ChannelPrefix { get; }

        /// <summary>The key whose presence tells the other nodes that <paramref name="nodeId"/> is alive.</summary>
        public string HeartbeatKey(string nodeId) => $"{ChannelPrefix}:nodes:{nodeId}:heartbeat";

        /// <summary>The set of connection ids <paramref name="nodeId"/> has registered.</summary>
        public string NodeConnectionsKey(string nodeId) => $"{ChannelPrefix}:nodes:{nodeId}:connections";

        public MultiNodeSignalARRRServerFixture() : this(null, null) {
        }

        internal MultiNodeSignalARRRServerFixture(TimeSpan? heartbeatInterval, TimeSpan? nodeTimeout) {
            _heartbeatInterval = heartbeatInterval;
            _nodeTimeout = nodeTimeout;
            _redisPort = GetFreePort();
            _redisContainerName = $"signalarrr-redis-{Guid.NewGuid():N}";
            StartRedisContainer(_redisContainerName, _redisPort);
            WaitForPort(_redisPort);

            var tfm = $"net{Environment.Version.Major}.0";
            var serverAssemblyPath = IntegrationTestServerPathResolver.GetAssemblyPath(tfm);

            var channelPrefix = $"signalarrr-tests-{Guid.NewGuid():N}";
            var connectionString = $"127.0.0.1:{_redisPort},abortConnect=false";

            ChannelPrefix = channelPrefix;
            RedisConnectionString = connectionString;

            _server1 = StartServerProcess(serverAssemblyPath, connectionString, channelPrefix, NodeId1, _heartbeatInterval, _nodeTimeout, out var serverUrl1);
            _server2 = StartServerProcess(serverAssemblyPath, connectionString, channelPrefix, NodeId2, _heartbeatInterval, _nodeTimeout, out var serverUrl2);

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
            string serverAssemblyPath,
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
                    Arguments = $"\"{serverAssemblyPath}\"",
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
                if (ServerUrlFile.TryRead(urlFile, out var content)) {
                    try { File.Delete(urlFile); } catch { }
                    return content;
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
