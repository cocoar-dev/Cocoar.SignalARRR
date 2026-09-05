using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// What a Postgres node does about the messages published while its subscription was down.
    /// </summary>
    /// <remarks>
    /// The subscription is severed from the database side with <c>pg_terminate_backend</c>, which
    /// is what a failover, a connection reset or an idle-timeout on a proxy look like to the node.
    /// The node reconnects with backoff (500 ms and doubling), so a gap of a couple of seconds is
    /// long enough for a stream of pushes to fall into it deterministically.
    /// </remarks>
    // Named to contain "BackplaneIntegrationTests": the CI workflows exclude Docker-dependent
    // backplane tests with the substring filter FullyQualifiedName!~BackplaneIntegrationTests.
    public sealed class BackplaneIntegrationTestsCatchUpPostgres {

        private const int Pushes = 20;
        private static readonly TimeSpan Outage = TimeSpan.FromSeconds(2);

        /// <summary>
        /// With catch-up on, a node that resubscribes reads everything past its cursor: every push
        /// sent into the gap arrives, once, and in order.
        /// </summary>
        [Fact]
        public async Task Messages_published_while_the_subscription_is_down_are_replayed_in_order() {
            using var fixture = new MultiNodeSignalARRRServerFixture(BackplaneProvider.Postgres, null, null, catchUp: true);
            var ct = TestContext.Current.CancellationToken;
            var handler = new CollectingPushClient();

            var connection = await ConnectAsync(fixture, handler, ct);
            try {
                await PushThroughAnOutageAsync(fixture, connection, ct);

                await WaitFor(() => handler.Count >= Pushes, $"all {Pushes} pushes to arrive after the reconnect (got {handler.Count})", ct);

                var received = handler.Snapshot();
                Assert.Equal(Enumerable.Range(0, Pushes).Select(i => $"catch-up-{i}"), received);
            } finally {
                await connection.DisposeAsync();
            }
        }

        /// <summary>
        /// With catch-up off, the same gap loses messages. This is the contract the Redis backplane
        /// has too, and the test exists so the previous one is known to cover a real outage rather
        /// than a reconnect fast enough to miss nothing.
        /// </summary>
        [Fact]
        public async Task Without_catch_up_messages_published_while_the_subscription_is_down_are_lost() {
            using var fixture = new MultiNodeSignalARRRServerFixture(BackplaneProvider.Postgres, null, null, catchUp: false);
            var ct = TestContext.Current.CancellationToken;
            var handler = new CollectingPushClient();

            var connection = await ConnectAsync(fixture, handler, ct);
            try {
                await PushThroughAnOutageAsync(fixture, connection, ct);

                // Give the reconnect and any straggler ample time; nothing can bring the lost ones back.
                await Task.Delay(TimeSpan.FromSeconds(5), ct);

                Assert.True(handler.Count < Pushes,
                    $"Expected the outage to lose pushes without catch-up, but all {Pushes} arrived — the subscription was never actually severed.");
            } finally {
                await connection.DisposeAsync();
            }
        }

        private static async Task<HARRRConnection> ConnectAsync(MultiNodeSignalARRRServerFixture fixture, CollectingPushClient handler, CancellationToken ct) {
            // The client sits on node 2; node 1 pushes to it, so every push crosses the backplane
            // and depends on node 2's subscription.
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{fixture.ServerUrl2}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, CollectingPushClient>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(fixture.ServerUrl2, connection, ct);

            using var http = new HttpClient();
            await WaitFor(async () => {
                var response = await http.GetAsync($"{fixture.ServerUrl1}/__test/presence-all", ct);
                return response.IsSuccessStatusCode
                    && (await response.Content.ReadAsStringAsync(ct)).Contains(connection.ConnectionId!, StringComparison.Ordinal);
            }, "node 1 to see the client on node 2", ct);

            return connection;
        }

        /// <summary>
        /// Keeps node 2's listener session terminated for <see cref="Outage"/> while node 1 pushes
        /// <see cref="Pushes"/> messages to node 2's client, spread across the outage.
        /// </summary>
        private static async Task PushThroughAnOutageAsync(MultiNodeSignalARRRServerFixture fixture, HARRRConnection connection, CancellationToken ct) {
            using var severing = new CancellationTokenSource();
            var sever = Task.Run(async () => {
                var until = DateTime.UtcNow + Outage;
                while (!severing.IsCancellationRequested && DateTime.UtcNow < until) {
                    await fixture.TerminateListenerAsync(MultiNodeSignalARRRServerFixture.NodeId2);
                    await Task.Delay(100);
                }
            }, CancellationToken.None);

            try {
                // The first termination lands before the first push; the rest keep the listener down.
                await Task.Delay(150, ct);

                using var http = new HttpClient();
                var connectionId = Uri.EscapeDataString(connection.ConnectionId ?? string.Empty);
                for (var i = 0; i < Pushes; i++) {
                    var response = await http.PostAsync(
                        $"{fixture.ServerUrl1}/__test/push-notification?connectionId={connectionId}&message=catch-up-{i}", null, ct);
                    response.EnsureSuccessStatusCode();
                    await Task.Delay(50, ct);
                }
            } finally {
                await sever;
            }
        }

        private static async Task WaitFor(Func<Task<bool>> condition, string description, CancellationToken cancellationToken) {
            for (var i = 0; i < 200; i++) {
                if (await condition()) {
                    return;
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for {description}.");
        }

        private static Task WaitFor(Func<bool> condition, string description, CancellationToken cancellationToken) {
            return WaitFor(() => Task.FromResult(condition()), description, cancellationToken);
        }

        private sealed class CollectingPushClient : ITestServerPushClient {
            private readonly List<string> _messages = new List<string>();
            private readonly object _sync = new object();

            public int Count {
                get {
                    lock (_sync) {
                        return _messages.Count;
                    }
                }
            }

            public IReadOnlyList<string> Snapshot() {
                lock (_sync) {
                    return _messages.ToArray();
                }
            }

            public void PushNotification(string message) {
                lock (_sync) {
                    _messages.Add(message);
                }
            }

            public Task<string> RequestClientInfo() => Task.FromResult("collecting-client");

            public void ConfigUpdated(string? path, string configJson) {
            }
        }
    }
}
