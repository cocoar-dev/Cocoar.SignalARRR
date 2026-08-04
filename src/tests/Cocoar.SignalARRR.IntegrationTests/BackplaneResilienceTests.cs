using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using StackExchange.Redis;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// Covers what happens when a node loses its heartbeat while it is still serving connections.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>ActiveCleanup_RemovesStalePresenceAfterNodeCrash</c>, which kills a node and
    /// checks that the cluster forgets it. Here the node stays alive — the cluster only *believes*
    /// it died — which is the case a GC pause, thread pool starvation or a brief partition produces.
    /// <para>
    /// Each test builds its own fixture with compressed heartbeat timings, so it runs in seconds
    /// rather than the production 5 s / 20 s.
    /// </para>
    /// </remarks>
    // Named to contain "BackplaneIntegrationTests": the CI workflows exclude Docker-dependent
    // backplane tests with the substring filter FullyQualifiedName!~BackplaneIntegrationTests.
    // A class outside that pattern runs on macOS and Windows, where there is no Redis container.
    public class BackplaneIntegrationTestsResilience {

        private static readonly TimeSpan Heartbeat = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan NodeTimeout = TimeSpan.FromMilliseconds(900);

        /// <summary>
        /// A node evicted from the registry while still running must put its connections back.
        /// </summary>
        /// <remarks>
        /// Registrations used to be written once at connect time and never re-asserted, and the
        /// cleanup another node performs is destructive. Losing the heartbeat for longer than
        /// <c>NodeTimeout</c> therefore removed every one of this node's connections from the hub,
        /// group, user and attribute indexes permanently — the node came back, looked healthy again,
        /// and its clients stayed unroutable. <c>CleanupNodeIfDeadAsync</c> skips the local node, so
        /// it could not even repair itself.
        /// </remarks>
        [Fact]
        public async Task A_node_declared_dead_while_running_re_registers_its_connections() {
            using var fixture = new MultiNodeSignalARRRServerFixture(Heartbeat, NodeTimeout);
            var ct = TestContext.Current.CancellationToken;

            var connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{fixture.ServerUrl1}/signalr/testhub?userId=alice&%40role=admin"));
            connection.RegisterInterface<TestShared.ITestClientMethods, ResilienceProbeClient>(new ResilienceProbeClient());
            await connection.StartAsync(ct);

            try {
                await TestHelper.WaitForClientRegistration(fixture.ServerUrl1, connection, ct);

                // The other node can see it: this is the state the eviction destroys.
                await WaitForPresenceCount(fixture.ServerUrl2, "role", "admin", 1, ct);

                using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
                var db = redis.GetDatabase();
                var heartbeatKey = fixture.HeartbeatKey(MultiNodeSignalARRRServerFixture.NodeId1);

                // Node 1 keeps running and keeps serving this client — the cluster is merely made to
                // believe it died. A single delete is not enough: node 1 rewrites the key every
                // heartbeat interval, so the gap is shorter than node 2's sweep and the eviction
                // never reliably happens. Suppressing it for longer than NodeTimeout is what a GC
                // pause or a brief partition actually looks like from the outside.
                using var suppression = new CancellationTokenSource();
                var suppress = Task.Run(async () => {
                    while (!suppression.IsCancellationRequested) {
                        await db.KeyDeleteAsync(heartbeatKey);
                        await Task.Delay(20);
                    }
                }, CancellationToken.None);

                try {
                    // Node 2 now sees node 1 as dead and wipes its registrations.
                    await WaitForPresenceCount(fixture.ServerUrl2, "role", "admin", 0, ct);
                } finally {
                    suppression.Cancel();
                    await suppress;
                }

                // Self-healing: node 1 notices its own heartbeat had vanished and re-registers the
                // connections it still serves. Without that, the client stays unroutable for good.
                await WaitForPresenceCount(fixture.ServerUrl2, "role", "admin", 1, ct);

                // And the restored registration is usable, not just present. Polled, not asserted at
                // a fixed moment: presence reappearing and the connection being routable again are
                // not the same instant, and asserting once right after the former is precisely the
                // fixed-moment race this file warns about elsewhere. It cost a red CI run.
                using var http = new HttpClient();
                await WaitFor(async () => {
                    var response = await http.PostAsync(
                        $"{fixture.ServerUrl2}/__test/invoke-attribute-all-getbyid?tag=admin&id=after-eviction", null, ct);

                    if (!response.IsSuccessStatusCode) {
                        return false;
                    }

                    var results = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(ct));
                    return results.GetArrayLength() == 1
                        && results[0].GetProperty("value").GetString()!.Contains("after-eviction");
                }, "the re-registered connection to answer a cluster invoke again", ct);
            } finally {
                await connection.DisposeAsync();
            }
        }

        /// <summary>
        /// A cluster query must hand back what it collected even when a node never answers.
        /// </summary>
        /// <remarks>
        /// There is no per-node deadline and no way to force an answer, so one node that is
        /// restarting, wedged or unable to resolve the types used to make the whole query throw
        /// after the full <c>InvokeTimeout</c> — discarding the local results and every other node's
        /// answers along with it. On a five-node cluster with one node mid-restart the caller waited
        /// fifteen seconds and then got nothing, although four nodes had replied in milliseconds.
        /// <para>
        /// Targeting matters here. <c>WithAttribute</c> does not reach this code at all:
        /// <c>CanUseDirectBackplaneDispatch</c> requires <c>AttributeFilters.Count == 0</c>, so an
        /// attribute-filtered query resolves connections up front and invokes them one by one,
        /// without ever entering <c>InvokeQueryAsync</c>'s wait. A first attempt at this test used
        /// that route and passed against the unfixed code. <c>WithUser</c> alone takes the direct
        /// dispatch path, which is the one that could time out.
        /// </para>
        /// <para>
        /// Node 2 is killed rather than merely stalled, and <c>NodeTimeout</c> is set far beyond the
        /// test, so it stays in the registry and the query goes on expecting an answer that can
        /// never arrive.
        /// </para>
        /// </remarks>
        [Fact]
        public async Task A_cluster_query_returns_partial_results_when_a_node_never_answers() {
            var invokeTimeout = TimeSpan.FromSeconds(2);

            using var fixture = new MultiNodeSignalARRRServerFixture(
                Heartbeat, nodeTimeout: TimeSpan.FromMinutes(5), invokeTimeout: invokeTimeout);
            var ct = TestContext.Current.CancellationToken;

            var connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{fixture.ServerUrl1}/signalr/testhub?userId=carol"));
            connection.RegisterInterface<TestShared.ITestClientMethods, ResilienceProbeClient>(new ResilienceProbeClient());
            await connection.StartAsync(ct);

            try {
                await TestHelper.WaitForClientRegistration(fixture.ServerUrl1, connection, ct);

                // Node 1 has to know node 2 before it dies — otherwise the query never waits on it
                // and the test would prove nothing. This is exactly the state
                // GetActiveRemoteNodeIdsAsync reads: membership in the node set plus a live
                // heartbeat key.
                using var redis = await ConnectionMultiplexer.ConnectAsync(fixture.RedisConnectionString);
                var db = redis.GetDatabase();
                await WaitFor(
                    async () => await db.SetContainsAsync($"{fixture.ChannelPrefix}:nodes", MultiNodeSignalARRRServerFixture.NodeId2)
                        && await db.KeyExistsAsync(fixture.HeartbeatKey(MultiNodeSignalARRRServerFixture.NodeId2)),
                    $"node 1 to see '{MultiNodeSignalARRRServerFixture.NodeId2}' as a live peer", ct);

                fixture.KillServer2();

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var started = Stopwatch.StartNew();
                var response = await http.PostAsync(
                    $"{fixture.ServerUrl1}/__test/invoke-user-all-getbyid?userId=carol&id=partial", null, ct);
                started.Stop();

                // Unfixed, this is a 500: InvokeQueryAsync threw TimeoutException and the local
                // client's answer went down with it.
                Assert.True(response.IsSuccessStatusCode,
                    $"Expected partial results, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");

                var results = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(ct));
                Assert.Equal(1, results.GetArrayLength());
                Assert.Contains("partial", results[0].GetProperty("value").GetString());

                // The query really did wait out the timeout rather than completing early. Without
                // this the test would still pass if node 2 had been swept from the registry before
                // the request, and it would then be covering nothing.
                Assert.True(started.Elapsed >= invokeTimeout * 0.8,
                    $"Query returned after {started.Elapsed}, so it never entered the timeout path.");
            } finally {
                await connection.DisposeAsync();
            }
        }

        private static async Task WaitForPresenceCount(
            string serverUrl, string key, string value, int expected, CancellationToken cancellationToken) {

            using var http = new HttpClient();
            var last = -1;

            await WaitFor(async () => {
                var response = await http.GetAsync(
                    $"{serverUrl}/__test/presence-attribute?key={Uri.EscapeDataString(key)}&value={Uri.EscapeDataString(value)}",
                    cancellationToken);

                if (!response.IsSuccessStatusCode) {
                    return false;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                last = JsonSerializer.Deserialize<JsonElement>(body).GetArrayLength();
                return last == expected;
            }, $"'{serverUrl}' to report {expected} connection(s) with {key}={value} (last saw {last})", cancellationToken);
        }

        /// <summary>
        /// Polls for a condition instead of sleeping for a guessed duration.
        /// </summary>
        /// <remarks>
        /// These tests are inherently timing-based; a fixed delay is how they turn flaky. Ten seconds
        /// is generous against the compressed 200 ms heartbeat, so a failure here means the state
        /// never arrived rather than that the runner was slow.
        /// </remarks>
        private static async Task WaitFor(Func<Task<bool>> condition, string description, CancellationToken cancellationToken) {
            for (var i = 0; i < 100; i++) {
                if (await condition()) {
                    return;
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"Timed out waiting for {description}.");
        }

        /// <summary>
        /// A client handler that only needs to exist — these tests assert on cluster registry state,
        /// not on delivered messages.
        /// </summary>
        private sealed class ResilienceProbeClient : TestShared.ITestClientMethods {
            public void Nix() { }
            public T Invoke<T>(string command, System.Collections.Generic.Dictionary<string, object>? variables = null) => default!;
            public System.Collections.Generic.List<string> GetContent(int count) => new System.Collections.Generic.List<string>();
            public string GetById(string id) => id;
            public string GetByGenericId(Guid id) => id.ToString();
            public Task<string> Wait(int seconds, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
            public bool CreateObject(string className, System.Collections.Generic.Dictionary<string, object> properties) => true;
            public bool CreateObjectFromTemplate(string templateName, System.Collections.Generic.Dictionary<string, object> properties) => true;
            public long FileLength(string id, System.IO.Stream filestream) => 0;
            public void Complex1(TestShared.ComplexTestClass compl) { }
            public TestShared.IncidentClass TestExpandableObject(TestShared.IncidentClass expandableObject) => expandableObject;
            public async System.Collections.Generic.IAsyncEnumerable<int> StreamNumbers(int count) {
                await Task.CompletedTask;
                yield break;
            }
            public System.IO.Stream GetFileStream(string content) => throw new NotSupportedException();
        }
    }
}
