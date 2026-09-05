using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.TestInfrastructure;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    /// <summary>Which backplane package a multi-node fixture wires the two servers with.</summary>
    public enum BackplaneProvider {
        Redis,
        Postgres
    }

    /// <summary>
    /// Two IntegrationTestServer processes sharing one backplane store in a Docker container.
    /// </summary>
    /// <remarks>
    /// The provider decides the container (Redis or Postgres) and the environment the servers
    /// read their backplane configuration from. Everything a test does goes through the servers'
    /// HTTP test endpoints; the two store accessors at the bottom exist for the resilience tests,
    /// which have to make a node look dead from the outside.
    /// </remarks>
    public class MultiNodeSignalARRRServerFixture : IDisposable {
        private readonly string _containerName;
        private readonly int _storePort;
        private readonly Process _server1;
        private readonly Process _server2;
        private readonly TimeSpan? _heartbeatInterval;
        private readonly TimeSpan? _nodeTimeout;
        private readonly TimeSpan? _invokeTimeout;
        private readonly bool? _catchUp;

        private ConnectionMultiplexer? _redis;
        private NpgsqlDataSource? _postgres;

        public BackplaneProvider Provider { get; }

        public string ServerUrl1 { get; }
        public string ServerUrl2 { get; }

        /// <summary>Node id of the server behind <see cref="ServerUrl1"/>.</summary>
        public const string NodeId1 = "node-1";

        /// <summary>Node id of the server behind <see cref="ServerUrl2"/>.</summary>
        public const string NodeId2 = "node-2";

        /// <summary>
        /// Connection string of this fixture's store, so a test can act on the backplane's state
        /// directly — for instance to make a node look dead while it keeps running.
        /// </summary>
        public string ConnectionString { get; }

        /// <summary>
        /// The isolation prefix this fixture's nodes use, unique per fixture: the Redis key prefix,
        /// or the Postgres schema.
        /// </summary>
        public string ChannelPrefix { get; }

        protected MultiNodeSignalARRRServerFixture(BackplaneProvider provider) : this(provider, null, null) {
        }

        internal MultiNodeSignalARRRServerFixture(BackplaneProvider provider, TimeSpan? heartbeatInterval, TimeSpan? nodeTimeout, TimeSpan? invokeTimeout = null, bool? catchUp = null) {
            Provider = provider;
            _heartbeatInterval = heartbeatInterval;
            _nodeTimeout = nodeTimeout;
            _invokeTimeout = invokeTimeout;
            _catchUp = catchUp;

            var suffix = Guid.NewGuid().ToString("N");
            _containerName = $"signalarrr-{provider.ToString().ToLowerInvariant()}-{suffix}";

            switch (provider) {
                case BackplaneProvider.Redis:
                    _storePort = StartContainer(_containerName, "redis:7-alpine", 6379, environment: null);
                    WaitForPort(_storePort);
                    ConnectionString = $"127.0.0.1:{_storePort},abortConnect=false";
                    ChannelPrefix = $"signalarrr-tests-{suffix}";
                    break;

                case BackplaneProvider.Postgres:
                    _storePort = StartContainer(_containerName, "postgres:16-alpine", 5432, environment: "-e POSTGRES_PASSWORD=signalarrr");
                    ConnectionString = $"Host=127.0.0.1;Port={_storePort};Username=postgres;Password=signalarrr;Database=postgres;Timeout=5";
                    WaitForPostgres(ConnectionString);
                    // A schema name: lowercase, underscores, and short enough for the channel suffixes.
                    ChannelPrefix = $"signalarrr_tests_{suffix}";
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(provider));
            }

            var tfm = $"net{Environment.Version.Major}.0";
            var serverAssemblyPath = IntegrationTestServerPathResolver.GetAssemblyPath(tfm);

            _server1 = StartServerProcess(serverAssemblyPath, provider, ConnectionString, ChannelPrefix, NodeId1, _heartbeatInterval, _nodeTimeout, _invokeTimeout, _catchUp, out var serverUrl1);
            _server2 = StartServerProcess(serverAssemblyPath, provider, ConnectionString, ChannelPrefix, NodeId2, _heartbeatInterval, _nodeTimeout, _invokeTimeout, _catchUp, out var serverUrl2);

            ServerUrl1 = serverUrl1;
            ServerUrl2 = serverUrl2;
        }

        /// <summary>A second, independent two-node cluster on the same provider, with its own store and timings.</summary>
        public MultiNodeSignalARRRServerFixture CreateIsolatedFixture(TimeSpan? heartbeatInterval = null, TimeSpan? nodeTimeout = null, TimeSpan? invokeTimeout = null) {
            return new MultiNodeSignalARRRServerFixture(Provider, heartbeatInterval, nodeTimeout, invokeTimeout);
        }

        public void Dispose() {
            StopProcess(_server1);
            StopProcess(_server2);
            _redis?.Dispose();
            _postgres?.Dispose();
            StopContainer(_containerName);
        }

        public void KillServer1() => StopProcess(_server1);

        public void KillServer2() => StopProcess(_server2);

        // --- Store access for the resilience tests ---

        /// <summary>
        /// Erases <paramref name="nodeId"/>'s heartbeat once. The node rewrites it every interval,
        /// so a test that wants the cluster to believe the node died has to call this in a loop.
        /// </summary>
        public async Task SuppressHeartbeatOnceAsync(string nodeId) {
            switch (Provider) {
                case BackplaneProvider.Redis:
                    await GetRedis().GetDatabase().KeyDeleteAsync($"{ChannelPrefix}:nodes:{nodeId}:heartbeat");
                    break;

                case BackplaneProvider.Postgres:
                    await using (var command = GetPostgres().CreateCommand($"DELETE FROM \"{ChannelPrefix}\".nodes WHERE node_id = $1")) {
                        command.Parameters.AddWithValue(nodeId);
                        await command.ExecuteNonQueryAsync();
                    }
                    break;
            }
        }

        /// <summary>
        /// Whether the store currently shows <paramref name="nodeId"/> as a live member — the same
        /// state the other node reads before it decides whom to wait for in a cluster query.
        /// </summary>
        public async Task<bool> IsNodeAliveInStoreAsync(string nodeId) {
            switch (Provider) {
                case BackplaneProvider.Redis: {
                    var db = GetRedis().GetDatabase();
                    return await db.SetContainsAsync($"{ChannelPrefix}:nodes", nodeId)
                        && await db.KeyExistsAsync($"{ChannelPrefix}:nodes:{nodeId}:heartbeat");
                }

                case BackplaneProvider.Postgres: {
                    await using var command = GetPostgres().CreateCommand(
                        $"SELECT EXISTS (SELECT 1 FROM \"{ChannelPrefix}\".nodes WHERE node_id = $1 AND last_seen > now() - $2::interval)");
                    command.Parameters.AddWithValue(nodeId);
                    command.Parameters.AddWithValue(_nodeTimeout ?? TimeSpan.FromSeconds(20));
                    return await command.ExecuteScalarAsync() is true;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// Severs <paramref name="nodeId"/>'s subscription from the database side, the way a
        /// network blip or a failover would. Postgres only: the listener session is identified by
        /// its <c>application_name</c>. The node reconnects with backoff; what it does about the
        /// messages published in between is what the catch-up tests are about.
        /// </summary>
        public async Task TerminateListenerAsync(string nodeId) {
            if (Provider != BackplaneProvider.Postgres) {
                throw new NotSupportedException("Only the Postgres fixture can terminate a listener session.");
            }

            await using var command = GetPostgres().CreateCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = $1 AND pid <> pg_backend_pid()");
            command.Parameters.AddWithValue($"signalarrr-backplane-listener:{nodeId}");
            await command.ExecuteNonQueryAsync();
        }

        private ConnectionMultiplexer GetRedis() {
            return _redis ??= ConnectionMultiplexer.Connect(ConnectionString);
        }

        private NpgsqlDataSource GetPostgres() {
            return _postgres ??= NpgsqlDataSource.Create(ConnectionString);
        }

        // --- Processes ---

        private static Process StartServerProcess(
            string serverAssemblyPath,
            BackplaneProvider provider,
            string connectionString,
            string channelPrefix,
            string nodeId,
            TimeSpan? heartbeatInterval,
            TimeSpan? nodeTimeout,
            TimeSpan? invokeTimeout,
            bool? catchUp,
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
            process.StartInfo.Environment["SIGNALARRR_BACKPLANE_PROVIDER"] = provider.ToString().ToLowerInvariant();
            process.StartInfo.Environment["SIGNALARRR_BACKPLANE_CONNECTION_STRING"] = connectionString;
            process.StartInfo.Environment["SIGNALARRR_BACKPLANE_CHANNEL_PREFIX"] = channelPrefix;
            process.StartInfo.Environment["SIGNALARRR_BACKPLANE_NODE_ID"] = nodeId;
            if (heartbeatInterval.HasValue) {
                process.StartInfo.Environment["SIGNALARRR_BACKPLANE_HEARTBEAT_INTERVAL_MS"] = ((int)heartbeatInterval.Value.TotalMilliseconds).ToString();
            }
            if (nodeTimeout.HasValue) {
                process.StartInfo.Environment["SIGNALARRR_BACKPLANE_NODE_TIMEOUT_MS"] = ((int)nodeTimeout.Value.TotalMilliseconds).ToString();
            }
            if (invokeTimeout.HasValue) {
                process.StartInfo.Environment["SIGNALARRR_BACKPLANE_INVOKE_TIMEOUT_MS"] = ((int)invokeTimeout.Value.TotalMilliseconds).ToString();
            }
            if (catchUp.HasValue) {
                process.StartInfo.Environment["SIGNALARRR_BACKPLANE_CATCH_UP"] = catchUp.Value ? "true" : "false";
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

        // --- Containers ---

        /// <summary>
        /// Starts this fixture's store container and returns the host port it was bound to.
        /// </summary>
        /// <remarks>
        /// Docker picks the host port, and the container is asked afterwards which one it got. The
        /// fixture used to pick it itself by opening a <see cref="TcpListener"/> on port 0, reading
        /// the assigned number and closing the listener again — which reserves nothing. Between that
        /// close and Docker's bind the port can be taken by anything, including another fixture doing
        /// the very same thing, and the container then fails with "port is already allocated".
        /// <para>
        /// The window is not always small: <c>docker run</c> pulls the image first if it is not
        /// cached, so on a cold runner several seconds pass between the port being chosen and being
        /// bound. Letting Docker allocate removes the window entirely rather than narrowing it.
        /// </para>
        /// </remarks>
        private static int StartContainer(string containerName, string image, int containerPort, string? environment) {
            StopContainer(containerName);

            // An empty host port means "Docker, choose one" -- it binds and reserves atomically.
            var (exitCode, _, stderr) = RunDocker($"run -d --name {containerName} {environment} -p 127.0.0.1::{containerPort} {image}");
            if (exitCode != 0) {
                throw new InvalidOperationException($"Could not start {image} container: {stderr}");
            }

            var (portExit, portOutput, portError) = RunDocker($"port {containerName} {containerPort}/tcp");
            if (portExit != 0) {
                throw new InvalidOperationException($"Could not read the {image} container's host port: {portError}");
            }

            // One line per binding, "127.0.0.1:49153". Take the first and keep only the port.
            var lines = portOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var firstLine = lines.Length > 0 ? lines[0].Trim() : null;

            var separator = firstLine?.LastIndexOf(':') ?? -1;
            if (separator < 0 || !int.TryParse(firstLine!.Substring(separator + 1), out var hostPort)) {
                throw new InvalidOperationException(
                    $"Could not parse the {image} container's host port from '{portOutput}'.");
            }

            return hostPort;
        }

        /// <summary>
        /// Runs a docker command and returns its exit code and captured output.
        /// </summary>
        /// <remarks>
        /// Both streams are drained before waiting. With redirected pipes and nothing reading them, a
        /// child that outruns the pipe buffer blocks on write while the parent blocks in
        /// <see cref="Process.WaitForExit()"/> — and an uncached <c>docker run</c> emits a full image
        /// pull log, which is more than enough to get there.
        /// </remarks>
        private static (int ExitCode, string StandardOutput, string StandardError) RunDocker(string arguments) {
            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "docker",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
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

            throw new TimeoutException($"Store container did not open port {port} in time.");
        }

        /// <summary>
        /// Waits until Postgres accepts a real connection. An open port is not enough here: the
        /// image initializes the cluster first and only then starts the server that listens on TCP,
        /// so the first successful login is the readiness signal.
        /// </summary>
        private static void WaitForPostgres(string connectionString) {
            var deadline = DateTime.UtcNow.AddSeconds(90);
            Exception? last = null;
            while (DateTime.UtcNow < deadline) {
                try {
                    using var connection = new NpgsqlConnection(connectionString);
                    connection.Open();
                    return;
                } catch (Exception ex) {
                    last = ex;
                }

                Thread.Sleep(250);
            }

            throw new TimeoutException($"Postgres container did not accept connections in time: {last?.Message}");
        }

        private static void StopContainer(string containerName) {
            // Failure is fine and expected on the pre-start call: there is nothing to remove yet.
            RunDocker($"rm -f {containerName}");
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

    /// <summary>The shared two-node cluster on the Redis backplane.</summary>
    public sealed class RedisMultiNodeSignalARRRServerFixture : MultiNodeSignalARRRServerFixture {
        public RedisMultiNodeSignalARRRServerFixture() : base(BackplaneProvider.Redis) {
        }
    }

    /// <summary>The shared two-node cluster on the Postgres backplane.</summary>
    public sealed class PostgresMultiNodeSignalARRRServerFixture : MultiNodeSignalARRRServerFixture {
        public PostgresMultiNodeSignalARRRServerFixture() : base(BackplaneProvider.Postgres) {
        }
    }

    [CollectionDefinition("Backplane")]
    public sealed class BackplaneSignalARRCollection : ICollectionFixture<RedisMultiNodeSignalARRRServerFixture> {
    }

    [CollectionDefinition("PostgresBackplane")]
    public sealed class PostgresBackplaneSignalARRCollection : ICollectionFixture<PostgresMultiNodeSignalARRRServerFixture> {
    }
}
