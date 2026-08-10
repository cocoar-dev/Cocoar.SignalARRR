using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// Answers what the README declared unverified (D-1): does SignalARRR's connection-bound state
    /// survive a stateful reconnect?
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>WithAutomaticReconnect()</c> always yields a new <c>ConnectionId</c>, so anything keyed on
    /// it is gone by design. Stateful reconnect is supposed to resume the <em>same</em> logical
    /// connection and keep the id — which is what SignalARRR's own state hangs off: stream ownership
    /// (S-7 checks the owning connection), upload slots, and the <c>ClientManager</c> registration
    /// with its group membership.
    /// </para>
    /// <para>
    /// <strong>A resume raises no events.</strong> <c>Reconnecting</c> and <c>Reconnected</c> belong
    /// to automatic reconnect, where the connection was genuinely lost. A stateful resume swaps the
    /// transport underneath while the connection stays <c>Connected</c> throughout — so waiting for
    /// <c>Reconnected</c> waits forever, and their <em>absence</em> is what distinguishes a resume
    /// from a fallback to a fresh connection.
    /// </para>
    /// <para>
    /// Do not "help" detection along by shortening the client's <c>ServerTimeout</c>: it has to stay
    /// above the server's keep-alive interval (15s by default), or the client times out on an idle
    /// but healthy connection and falls back to a full reconnect — which looks exactly like stateful
    /// reconnect being broken.
    /// </para>
    /// </remarks>
    [Collection("Simple")]
    public class StatefulReconnectTests {

        private static readonly TimeSpan ResumeDeadline = TimeSpan.FromSeconds(30);

        private readonly SignalARRRServerInstanceFixture _fixture;

        public StatefulReconnectTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        [Fact]
        public async Task Connection_bound_state_survives_a_severed_transport() {
            var cancellationToken = TestContext.Current.CancellationToken;

            await using var proxy = new SeverableTcpProxy(new Uri(_fixture.ServerUrl));
            using var http = new HttpClient { BaseAddress = new Uri(_fixture.ServerUrl) };

            var connection = HARRRConnection.Create(builder => {
                builder
                    // WebSockets explicitly: stateful reconnect exists only there, and a silent
                    // fallback to long polling would look like the feature failing.
                    .WithUrl($"{proxy.BaseAddress.ToString().TrimEnd('/')}/signalr/testhub",
                        options => options.Transports = HttpTransportType.WebSockets)
                    .WithStatefulReconnect()
                    .WithAutomaticReconnect();
            });

            var hub = connection.AsSignalRHubConnection();
            var lostConnection = CaptureConnectionLoss(hub);

            try {
                await connection.StartAsync(cancellationToken);

                var originalConnectionId = hub.ConnectionId;
                Assert.False(string.IsNullOrEmpty(originalConnectionId));

                // Connection-bound state to check afterwards.
                var group = $"stateful-reconnect-{Guid.NewGuid():N}";
                await WaitForClientRegistrationAsync(http, originalConnectionId!, cancellationToken);
                (await http.GetAsync($"/__test/join-group?connectionId={originalConnectionId}&group={group}", cancellationToken))
                    .EnsureSuccessStatusCode();
                Assert.Contains(group, await http.GetStringAsync($"/__test/client-groups?connectionId={originalConnectionId}", cancellationToken));

                var requestsBeforeSever = proxy.RequestLines.Count;
                Assert.True(proxy.SeverAll() > 0, "the proxy had no live connection to sever — the client was not routed through it");

                // The transport is gone; the connection is supposed to carry on regardless.
                await WaitUntilUsableAgainAsync(
                    () => connection.InvokeAsync<string>("GetName", cancellationToken),
                    proxy, lostConnection, cancellationToken);

                AssertTransportWasResumed(proxy, requestsBeforeSever);

                // 1. Resumed, not re-established. Either half alone would be satisfied by an
                //    ordinary automatic reconnect.
                Assert.Equal(originalConnectionId, hub.ConnectionId);
                Assert.True(lostConnection.IsEmpty,
                    "the connection was re-established rather than resumed: " + string.Join(" | ", lostConnection));

                // 2. The server still knows this connection, and still has it in its group — the
                //    ClientManager registration and group tracking outlived the drop.
                Assert.Contains(group, await http.GetStringAsync($"/__test/client-groups?connectionId={originalConnectionId}", cancellationToken));

                // 3. And it still works end to end.
                Assert.Equal("MyName", await connection.InvokeAsync<string>("GetName", cancellationToken));
            } finally {
                try { await connection.StopAsync(CancellationToken.None); } catch { /* teardown */ }
                await connection.DisposeAsync();
            }
        }

        /// <summary>
        /// The control: the same sequence against a plain SignalR hub with no SignalARRR in it.
        /// </summary>
        /// <remarks>
        /// A failure above means nothing on its own — it could be the harness, the proxy, or SignalR.
        /// Only a passing control turns it into a statement about SignalARRR. This one earned its
        /// keep: it was what showed that an earlier failure was the test's fault, not the library's.
        /// </remarks>
        [Fact]
        public async Task Control_a_plain_signalr_hub_resumes_the_same_connection() {
            var cancellationToken = TestContext.Current.CancellationToken;

            await using var proxy = new SeverableTcpProxy(new Uri(_fixture.ServerUrl));

            var hub = new HubConnectionBuilder()
                .WithUrl($"{proxy.BaseAddress.ToString().TrimEnd('/')}/signalr/plainhub",
                    options => options.Transports = HttpTransportType.WebSockets)
                .WithStatefulReconnect()
                .WithAutomaticReconnect()
                .Build();

            var lostConnection = CaptureConnectionLoss(hub);

            try {
                await hub.StartAsync(cancellationToken);
                var originalConnectionId = hub.ConnectionId;
                Assert.Equal("ping", await hub.InvokeAsync<string>("Echo", "ping", cancellationToken));

                var requestsBeforeSever = proxy.RequestLines.Count;
                Assert.True(proxy.SeverAll() > 0);

                await WaitUntilUsableAgainAsync(
                    () => hub.InvokeAsync<string>("Echo", "pong", cancellationToken),
                    proxy, lostConnection, cancellationToken);

                AssertTransportWasResumed(proxy, requestsBeforeSever);

                Assert.Equal(originalConnectionId, hub.ConnectionId);
                Assert.True(lostConnection.IsEmpty,
                    "the connection was re-established rather than resumed: " + string.Join(" | ", lostConnection));
            } finally {
                try { await hub.StopAsync(CancellationToken.None); } catch { /* teardown */ }
                await hub.DisposeAsync();
            }
        }

        /// <summary>
        /// Proves the transport really was replaced, and replaced by a <em>resume</em>.
        /// </summary>
        /// <remarks>
        /// Without this the test would also pass if the sever quietly did nothing: a call that still
        /// works proves the connection is usable, not that anything happened to it. A resume opens a
        /// new connection carrying the existing id (<c>GET ...?id=</c>); a fallback would negotiate
        /// first, which is exactly what must not appear here.
        /// </remarks>
        private static void AssertTransportWasResumed(SeverableTcpProxy proxy, int requestsBeforeSever) {
            var afterSever = proxy.RequestLines.Skip(requestsBeforeSever).ToArray();

            Assert.True(afterSever.Length > 0,
                "no new connection reached the proxy after severing — the transport was never actually replaced, " +
                "so this test proved nothing about resumption");

            Assert.DoesNotContain(afterSever, line => line.Contains("/negotiate", StringComparison.Ordinal));
        }

        /// <summary>
        /// Records the events that only fire when the connection was actually <em>lost</em>. A
        /// stateful resume leaves this empty.
        /// </summary>
        private static ConcurrentQueue<string> CaptureConnectionLoss(HubConnection hub) {
            var events = new ConcurrentQueue<string>();
            hub.Reconnecting += ex => { events.Enqueue($"Reconnecting: {ex?.GetType().Name}: {ex?.Message}"); return Task.CompletedTask; };
            hub.Closed += ex => { events.Enqueue($"Closed: {ex?.GetType().Name}: {ex?.Message}"); return Task.CompletedTask; };
            return events;
        }

        /// <summary>
        /// Waits until a call goes through again. There is no event to await — a resume is silent —
        /// so the only honest signal that the transport is back is that the connection carries a call.
        /// </summary>
        private static async Task WaitUntilUsableAgainAsync(
            Func<Task> call,
            SeverableTcpProxy proxy,
            ConcurrentQueue<string> lostConnection,
            CancellationToken cancellationToken) {

            var deadline = DateTime.UtcNow + ResumeDeadline;
            Exception? last = null;

            while (DateTime.UtcNow < deadline) {
                try {
                    await call();
                    return;
                } catch (Exception ex) {
                    last = ex;
                    await Task.Delay(100, cancellationToken);
                }
            }

            Assert.Fail(
                $"the connection never carried a call again within {ResumeDeadline.TotalSeconds:0}s. " +
                $"Last failure: {last?.GetType().Name}: {last?.Message}. " +
                $"Connection-loss events: {(lostConnection.IsEmpty ? "(none)" : string.Join(" | ", lostConnection))}. " +
                $"Requests the client made: {string.Join(" ; ", proxy.RequestLines)}. " +
                $"Teardown faults: {proxy.DrainFaults()}");
        }

        /// <summary>
        /// Registration in <c>ClientManager</c> happens in <c>OnConnectedAsync</c>, which finishes
        /// after <c>StartAsync</c> returns. Joining a group before it lands would fail for a reason
        /// that has nothing to do with reconnects.
        /// </summary>
        private static async Task WaitForClientRegistrationAsync(HttpClient http, string connectionId, CancellationToken cancellationToken) {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline) {
                var exists = await http.GetStringAsync($"/__test/client-exists?connectionId={connectionId}", cancellationToken);
                if (exists.Contains("true", StringComparison.OrdinalIgnoreCase)) {
                    return;
                }

                await Task.Delay(50, cancellationToken);
            }

            Assert.Fail($"client {connectionId} was never registered in ClientManager");
        }
    }
}
