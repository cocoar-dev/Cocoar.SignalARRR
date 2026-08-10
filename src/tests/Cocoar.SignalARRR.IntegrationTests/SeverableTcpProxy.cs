using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// A transparent TCP forwarder that can drop its live connections on demand, so a test can
    /// simulate the network failing underneath a client while the server keeps running.
    /// </summary>
    /// <remarks>
    /// Stateful reconnect can only be tested this way. Killing the server process does not work: the
    /// resumable state lives in that process, so a restart produces a genuinely new connection, which
    /// is the case stateful reconnect is <em>not</em> about. What has to break is the transport alone.
    /// <para>
    /// The drop is a reset, not a graceful close (<c>LingerOption(true, 0)</c>): a FIN would look like
    /// an orderly shutdown, which is the one thing a client is entitled to treat as final. The
    /// listener keeps accepting throughout, so the reconnect attempt finds its way back.
    /// </para>
    /// </remarks>
    internal sealed class SeverableTcpProxy : IAsyncDisposable {

        private readonly TcpListener _listener;
        private readonly string _targetHost;
        private readonly int _targetPort;
        private readonly ConcurrentDictionary<Conduit, byte> _live = new();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _acceptLoop;

        public Uri BaseAddress { get; }

        /// <summary>The first request line of every connection the client opened, in order.</summary>
        public ConcurrentQueue<string> RequestLines { get; } = new();

        /// <summary>What went wrong while tearing sockets down. Silence here was hiding real failures.</summary>
        public ConcurrentQueue<string> Faults { get; } = new();

        public SeverableTcpProxy(Uri target) {
            _targetHost = target.Host;
            _targetPort = target.Port;

            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            BaseAddress = new Uri($"http://127.0.0.1:{port}");

            _acceptLoop = AcceptLoopAsync();
        }

        /// <summary>Resets both ends of every open connection. Returns how many there were.</summary>
        public int SeverAll() => Sever(clientSideOnly: false);

        /// <summary>
        /// Resets only the client-facing end, leaving the socket to the server open but idle.
        /// </summary>
        /// <remarks>
        /// This is the faithful simulation of a client losing its network: the server receives no
        /// RST, sees no close, and goes on believing the connection is alive until its own timeout.
        /// Severing both ends instead tells the server immediately that the client is gone, and it
        /// then has no reason to hold anything for a resumption — which makes a stateful reconnect
        /// impossible to observe and looks, misleadingly, like the feature not working.
        /// </remarks>
        public int SeverClientSide() => Sever(clientSideOnly: true);

        private int Sever(bool clientSideOnly) {
            var severed = 0;
            foreach (var conduit in _live.Keys) {
                if (_live.TryRemove(conduit, out _)) {
                    conduit.Reset(clientSideOnly);
                    severed++;
                }
            }

            return severed;
        }

        private async Task AcceptLoopAsync() {
            while (!_shutdown.IsCancellationRequested) {
                TcpClient inbound;
                try {
                    inbound = await _listener.AcceptTcpClientAsync(_shutdown.Token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return;
                } catch (ObjectDisposedException) {
                    return;
                } catch (SocketException) {
                    return;
                }

                _ = ForwardAsync(inbound);
            }
        }

        private async Task ForwardAsync(TcpClient inbound) {
            TcpClient? outbound = null;
            Conduit? conduit = null;

            try {
                outbound = new TcpClient();
                await outbound.ConnectAsync(_targetHost, _targetPort, _shutdown.Token).ConfigureAwait(false);

                inbound.NoDelay = true;
                outbound.NoDelay = true;

                // Arm the reset up front. Setting LingerState on a socket that already has a pending
                // read does not reliably change how it closes, and a graceful FIN is precisely what
                // must not happen: the peer would see an orderly shutdown and wait for its own
                // timeout instead of failing immediately.
                inbound.LingerState = new LingerOption(true, 0);
                outbound.LingerState = new LingerOption(true, 0);

                conduit = new Conduit(inbound, outbound);
                _live[conduit] = 0;

                var inToOut = PumpAsync(inbound, outbound, recordRequestLine: true);
                var outToIn = PumpAsync(outbound, inbound);

                // Whichever side ends first ends the pair: a half-open forwarder would leave the
                // other direction waiting on a peer that is already gone.
                await Task.WhenAny(inToOut, outToIn).ConfigureAwait(false);
            } catch {
                // A connection that cannot be established or is reset mid-flight is exactly what this
                // proxy exists to produce. Nothing here is worth reporting.
            } finally {
                if (conduit != null) {
                    // Only tear the pair down if it is still ours. A severed conduit has already
                    // been taken out of _live and may deliberately be keeping its server socket
                    // open — disposing it here would defeat SeverClientSide.
                    if (_live.TryRemove(conduit, out _)) {
                        conduit.Dispose();
                    }
                } else {
                    Safe(() => inbound.Dispose());
                    if (outbound != null) Safe(() => outbound.Dispose());
                }
            }
        }

        private async Task PumpAsync(TcpClient from, TcpClient to, bool recordRequestLine = false) {
            var buffer = new byte[16 * 1024];
            var source = from.GetStream();
            var sink = to.GetStream();
            var first = recordRequestLine;

            while (true) {
                var read = await source.ReadAsync(buffer, _shutdown.Token).ConfigureAwait(false);
                if (read == 0) {
                    return;
                }

                if (first) {
                    first = false;
                    var text = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(read, 512));
                    var lineEnd = text.IndexOfAny(new[] { (char)13, (char)10 });
                    RequestLines.Enqueue(lineEnd > 0 ? text.Substring(0, lineEnd) : text);
                }

                await sink.WriteAsync(buffer.AsMemory(0, read), _shutdown.Token).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync() {
            _shutdown.Cancel();
            SeverAll();
            Safe(() => _listener.Stop());

            try {
                await _acceptLoop.ConfigureAwait(false);
            } catch {
                // Shutting down.
            }

            _shutdown.Dispose();
        }

        private static readonly ConcurrentQueue<string> FaultSink = new();

        private static void Safe(Action action) {
            try { action(); } catch (Exception ex) { FaultSink.Enqueue(ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>Drains the teardown failures recorded so far.</summary>
        public string DrainFaults() {
            var all = new System.Collections.Generic.List<string>();
            while (FaultSink.TryDequeue(out var f)) all.Add(f);
            return all.Count == 0 ? "(no teardown faults)" : string.Join(" | ", all);
        }

        private sealed class Conduit : IDisposable {
            private readonly TcpClient _inbound;
            private readonly TcpClient _outbound;

            public Conduit(TcpClient inbound, TcpClient outbound) {
                _inbound = inbound;
                _outbound = outbound;
            }

            /// <summary>
            /// Drops with a reset, so neither side sees an orderly shutdown. When
            /// <paramref name="clientSideOnly"/> is set, the socket to the server is left open and
            /// simply stops carrying traffic.
            /// </summary>
            public void Reset(bool clientSideOnly) {
                Abort(_inbound);
                if (!clientSideOnly) {
                    Abort(_outbound);
                }
            }

            public void Dispose() {
                Safe(() => _inbound.Dispose());
                Safe(() => _outbound.Dispose());
            }

            private static void Abort(TcpClient client) {
                // Linger was armed when the conduit was built, so this closes with a reset.
                Safe(() => client.Client.Close(0));
                Safe(client.Dispose);
            }
        }
    }
}
