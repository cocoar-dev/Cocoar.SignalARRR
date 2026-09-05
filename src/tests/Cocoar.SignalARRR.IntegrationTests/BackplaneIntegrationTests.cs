using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    /// <summary>
    /// The cross-node behaviour every backplane has to provide, run once per provider: the
    /// concrete classes at the bottom of this file bind it to the Redis and the Postgres cluster.
    /// </summary>
    /// <remarks>
    /// Their names contain "BackplaneIntegrationTests" on purpose: the CI workflows exclude the
    /// Docker-dependent backplane tests on macOS and Windows with the substring filter
    /// <c>FullyQualifiedName!~BackplaneIntegrationTests</c>.
    /// </remarks>
    public abstract class BackplaneIntegrationTestsBase {
        private readonly MultiNodeSignalARRRServerFixture _fixture;

        protected BackplaneIntegrationTestsBase(MultiNodeSignalARRRServerFixture fixture) {
            _fixture = fixture;
        }

        /// <summary>
        /// An envelope that does not fit a Postgres notification (under 8 kB) has to travel through
        /// the table-backed path — and arrive intact, in one piece, on the other node. The Redis
        /// backplane has no such boundary; there the test is a plain size check.
        /// </summary>
        [Fact]
        public async Task PushNotification_CrossNode_LargePayloadArrivesIntact() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new TestServerPushClientImpl();
            const int size = 64 * 1024;

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            try {
                using var http = new HttpClient();
                await WaitForCrossNodeVisibility(_fixture.ServerUrl2, connection, ct);
                var connectionId = Uri.EscapeDataString(connection.ConnectionId ?? string.Empty);
                var response = await http.PostAsync(
                    $"{_fixture.ServerUrl2}/__test/push-notification-sized?connectionId={connectionId}&size={size}",
                    null,
                    ct);

                response.EnsureSuccessStatusCode();
                Assert.True(await handler.PushReceived.WaitAsync(TimeSpan.FromSeconds(10), ct));
                Assert.True(TestShared.SizedTestPayload.IsValid(handler.LastPushMessage, size),
                    $"Expected a {size}-character payload, got {handler.LastPushMessage?.Length} characters.");
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        /// <summary>
        /// The same boundary for an awaited call, in both directions: the request envelope carries
        /// an oversized argument, and the response envelope carries the oversized result back.
        /// </summary>
        /// <remarks>
        /// 20 kB, not 64: the result travels client-to-server over SignalR, whose hub rejects
        /// messages above its default 32 kB receive limit. That limit is the test server's, not the
        /// backplane's, and 20 kB is still well past the 8 kB notification boundary.
        /// </remarks>
        [Fact]
        public async Task Invoke_CrossNode_LargeArgumentAndResultRoundTrip() {
            var ct = TestContext.Current.CancellationToken;
            const int size = 20 * 1024;

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(new BackplaneNixCounter());
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            try {
                using var http = new HttpClient();
                await WaitForCrossNodeVisibility(_fixture.ServerUrl2, connection, ct);
                var connectionId = Uri.EscapeDataString(connection.ConnectionId ?? string.Empty);
                var response = await http.PostAsync(
                    $"{_fixture.ServerUrl2}/__test/trigger-client-getbyid-sized?connectionId={connectionId}&size={size}",
                    null,
                    ct);

                var body = await response.Content.ReadAsStringAsync(ct);
                Assert.True(response.IsSuccessStatusCode, $"Expected success, got {(int)response.StatusCode}: {body}");

                var result = JsonSerializer.Deserialize<JsonElement>(body);
                Assert.Equal(size, result.GetProperty("length").GetInt32());
                Assert.True(result.GetProperty("valid").GetBoolean(), "The round-tripped payload was corrupted.");
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        /// <summary>
        /// A cluster subject event raised on one node reaches the subscriber on that node and the
        /// subscriber on the other node — once each. Not zero on the remote node (the relay
        /// works), not two on the local one (no echo).
        /// </summary>
        [Fact]
        public async Task ClusterSubject_EventRaisedOnOneNode_ReachesBothNodesExactlyOnce() {
            var ct = TestContext.Current.CancellationToken;
            var prefix = $"once-{Guid.NewGuid():N}-";
            using var http = new HttpClient();

            var response = await http.PostAsync($"{_fixture.ServerUrl1}/__test/cluster-subject-publish?value={prefix}a", null, ct);
            response.EnsureSuccessStatusCode();

            await WaitForClusterEvents(_fixture.ServerUrl1, prefix, 1, ct);
            await WaitForClusterEvents(_fixture.ServerUrl2, prefix, 1, ct);

            // A duplicate would arrive right behind the original; give it the chance.
            await Task.Delay(750, ct);
            Assert.Equal(1, (await GetClusterEvents(_fixture.ServerUrl1, prefix, ct)).GetArrayLength());
            Assert.Equal(1, (await GetClusterEvents(_fixture.ServerUrl2, prefix, ct)).GetArrayLength());
        }

        /// <summary>
        /// Twenty events raised in a burst on one node arrive on the other in the order they were
        /// raised — the per-subject relay loop and the sequential hand-off on the receiver.
        /// </summary>
        [Fact]
        public async Task ClusterSubject_BurstOfEvents_ArrivesInOrderOnTheOtherNode() {
            var ct = TestContext.Current.CancellationToken;
            var prefix = $"order-{Guid.NewGuid():N}-";
            const int count = 20;
            using var http = new HttpClient();

            for (var i = 0; i < count; i++) {
                var response = await http.PostAsync($"{_fixture.ServerUrl2}/__test/cluster-subject-publish?value={prefix}{i:D2}", null, ct);
                response.EnsureSuccessStatusCode();
            }

            await WaitForClusterEvents(_fixture.ServerUrl1, prefix, count, ct);

            var received = (await GetClusterEvents(_fixture.ServerUrl1, prefix, ct))
                .EnumerateArray().Select(e => e.GetProperty("value").GetString()).ToArray();
            Assert.Equal(Enumerable.Range(0, count).Select(i => $"{prefix}{i:D2}"), received);
        }

        /// <summary>
        /// An event well above the Postgres notification limit arrives intact on the other node,
        /// and an awaited publish returns only once the backplane has it.
        /// </summary>
        [Fact]
        public async Task ClusterSubject_LargeAwaitedEvent_ArrivesIntactOnTheOtherNode() {
            var ct = TestContext.Current.CancellationToken;
            var prefix = $"large-{Guid.NewGuid():N}-";
            const int size = 64 * 1024;
            using var http = new HttpClient();

            var response = await http.PostAsync($"{_fixture.ServerUrl1}/__test/cluster-subject-publish?value={prefix}big&size={size}&awaited=true", null, ct);
            response.EnsureSuccessStatusCode();

            await WaitForClusterEvents(_fixture.ServerUrl2, prefix, 1, ct);

            var received = (await GetClusterEvents(_fixture.ServerUrl2, prefix, ct))[0];
            Assert.Equal(size, received.GetProperty("payloadLength").GetInt32());
            Assert.True(received.GetProperty("payloadValid").GetBoolean(), "The relayed payload was corrupted.");
        }

        private static async Task<JsonElement> GetClusterEvents(string serverUrl, string prefix, CancellationToken cancellationToken) {
            using var http = new HttpClient();
            var response = await http.GetAsync($"{serverUrl}/__test/cluster-subject-received?prefix={Uri.EscapeDataString(prefix)}", cancellationToken);
            response.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        private static async Task WaitForClusterEvents(string serverUrl, string prefix, int expected, CancellationToken cancellationToken) {
            var last = -1;
            for (var i = 0; i < 100; i++) {
                last = (await GetClusterEvents(serverUrl, prefix, cancellationToken)).GetArrayLength();
                if (last >= expected) {
                    return;
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"'{serverUrl}' saw {last} cluster event(s) with prefix '{prefix}', expected {expected}.");
        }

        [Fact]
        public async Task PushNotification_CrossNode_ClientReceives() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new TestServerPushClientImpl();

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            try {
                using var http = new HttpClient();
                await WaitForPresenceContainsConnection($"{_fixture.ServerUrl2}/__test/presence-all", connection.ConnectionId!, ct);
                var connectionId = Uri.EscapeDataString(connection.ConnectionId ?? string.Empty);
                var message = Uri.EscapeDataString("hello-cross-node");
                var response = await http.PostAsync(
                    $"{_fixture.ServerUrl2}/__test/push-notification?connectionId={connectionId}&message={message}",
                    null,
                    ct);

                response.EnsureSuccessStatusCode();
                Assert.True(await handler.PushReceived.WaitAsync(TimeSpan.FromSeconds(5), ct));
                Assert.Equal("hello-cross-node", handler.LastPushMessage);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task RequestClientInfo_CrossNode_ReturnsValue() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new TestServerPushClientImpl();

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            try {
                using var http = new HttpClient();
                await WaitForPresenceContainsConnection($"{_fixture.ServerUrl2}/__test/presence-all", connection.ConnectionId!, ct);
                var connectionId = Uri.EscapeDataString(connection.ConnectionId ?? string.Empty);
                var response = await http.PostAsync(
                    $"{_fixture.ServerUrl2}/__test/request-client-info?connectionId={connectionId}",
                    null,
                    ct);

                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync(ct);
                Assert.Contains("TestClient-", body);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task RequestClientInfo_AfterDisconnect_FailsFast() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new TestServerPushClientImpl();

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            var connectionId = connection.ConnectionId!;
            await connection.StopAsync(ct);
            await connection.DisposeAsync();

            using var http = new HttpClient();
            var escapedConnectionId = Uri.EscapeDataString(connectionId);
            var response = await http.PostAsync(
                $"{_fixture.ServerUrl2}/__test/request-client-info?connectionId={escapedConnectionId}",
                null,
                ct);

            Assert.False(response.IsSuccessStatusCode);
        }

        [Fact]
        public async Task BroadcastAll_CrossNode_AllClientsReceive() {
            var ct = TestContext.Current.CancellationToken;
            var handler1 = new BackplaneNixCounter();
            var handler2 = new BackplaneNixCounter();

            var connection1 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection1.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(handler1);
            await connection1.StartAsync(ct);

            var connection2 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub"));
            connection2.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(handler2);
            await connection2.StartAsync(ct);

            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection1, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, connection2, ct);

            // Broadcast is issued on node 1 and has to reach connection2 on node 2.
            await WaitForCrossNodeVisibility(_fixture.ServerUrl1, connection2, ct);

            try {
                using var http = new HttpClient();
                var response = await http.PostAsync($"{_fixture.ServerUrl1}/__test/broadcast-all-nix", null, ct);
                response.EnsureSuccessStatusCode();

                Assert.True(await handler1.Received.WaitAsync(TimeSpan.FromSeconds(5), ct));
                Assert.True(await handler2.Received.WaitAsync(TimeSpan.FromSeconds(5), ct));
            } finally {
                await connection1.StopAsync(ct);
                await connection1.DisposeAsync();
                await connection2.StopAsync(ct);
                await connection2.DisposeAsync();
            }
        }

        [Fact]
        public async Task RemoteJoinGroup_AllowsCrossNodeGroupBroadcast() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new BackplaneNixCounter();

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            try {
                using var http = new HttpClient();
                await WaitForPresenceContainsConnection($"{_fixture.ServerUrl2}/__test/presence-all", connection.ConnectionId!, ct);
                var joinResponse = await http.GetAsync(
                    $"{_fixture.ServerUrl2}/__test/join-group?connectionId={Uri.EscapeDataString(connection.ConnectionId ?? string.Empty)}&group={Uri.EscapeDataString("remote-group")}",
                    ct);
                joinResponse.EnsureSuccessStatusCode();

                await WaitForClientGroup(_fixture.ServerUrl1, connection.ConnectionId!, "remote-group", ct);

                var broadcastResponse = await http.PostAsync(
                    $"{_fixture.ServerUrl2}/__test/broadcast-group-nix?group={Uri.EscapeDataString("remote-group")}",
                    null,
                    ct);
                broadcastResponse.EnsureSuccessStatusCode();

                Assert.True(await handler.Received.WaitAsync(TimeSpan.FromSeconds(5), ct));
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task InvokeAll_CrossNode_ReturnsResultsFromAllNodes() {
            var ct = TestContext.Current.CancellationToken;
            var handler1 = new BackplaneNixCounter();
            var handler2 = new BackplaneNixCounter();

            var connection1 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection1.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(handler1);
            await connection1.StartAsync(ct);

            var connection2 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub"));
            connection2.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(handler2);
            await connection2.StartAsync(ct);

            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection1, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, connection2, ct);

            // The invoke is issued on node 1 and has to reach connection2 on node 2.
            await WaitForCrossNodeVisibility(_fixture.ServerUrl1, connection2, ct);

            try {
                using var http = new HttpClient();
                var response = await http.PostAsync($"{_fixture.ServerUrl1}/__test/invoke-all-getbyid?id=cluster", null, ct);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync(ct);
                var results = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
                Assert.Equal(2, results.GetArrayLength());

                foreach (var item in results.EnumerateArray()) {
                    Assert.Contains("cluster", item.GetProperty("value").GetString());
                }
            } finally {
                await connection1.StopAsync(ct);
                await connection1.DisposeAsync();
                await connection2.StopAsync(ct);
                await connection2.DisposeAsync();
            }
        }

        [Fact]
        public async Task InvokeOne_CrossNode_ReturnsRemoteValueWhenCallerHasNoLocalClients() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new BackplaneNixCounter();

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            // The invoke is issued on node 2, which has no local clients, so it can only succeed
            // once node 2 sees this node-1 connection in the distributed registry.
            await WaitForCrossNodeVisibility(_fixture.ServerUrl2, connection, ct);

            try {
                using var http = new HttpClient();
                var response = await http.PostAsync($"{_fixture.ServerUrl2}/__test/invoke-one-getbyid?id=remote-only", null, ct);
                response.EnsureSuccessStatusCode();

                var body = await response.Content.ReadAsStringAsync(ct);
                var result = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
                Assert.Contains("remote-only", result.GetProperty("value").GetString());
                Assert.Equal(connection.ConnectionId, result.GetProperty("clientId").GetString());
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task UserTargeting_CrossNode_OnlyMatchingUserConnectionsReceiveAndInvoke() {
            var ct = TestContext.Current.CancellationToken;
            var alice1 = new BackplaneNixCounter();
            var alice2 = new BackplaneNixCounter();
            var bob = new BackplaneNixCounter();

            var connectionAlice1 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub?userId=alice"));
            connectionAlice1.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(alice1);
            await connectionAlice1.StartAsync(ct);

            var connectionAlice2 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub?userId=alice"));
            connectionAlice2.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(alice2);
            await connectionAlice2.StartAsync(ct);

            var connectionBob = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub?userId=bob"));
            connectionBob.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(bob);
            await connectionBob.StartAsync(ct);

            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connectionAlice1, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, connectionAlice2, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, connectionBob, ct);

            // Targeting happens on node 1 and has to reach connections on node 2. Bob is waited for
            // as well: the assertion that he receives nothing is only meaningful once node 1 could
            // have targeted him.
            await WaitForCrossNodeVisibility(_fixture.ServerUrl1, connectionAlice2, ct);
            await WaitForCrossNodeVisibility(_fixture.ServerUrl1, connectionBob, ct);

            try {
                using var http = new HttpClient();

                var broadcastResponse = await http.PostAsync($"{_fixture.ServerUrl1}/__test/broadcast-user-nix?userId=alice", null, ct);
                broadcastResponse.EnsureSuccessStatusCode();

                Assert.True(await alice1.Received.WaitAsync(TimeSpan.FromSeconds(5), ct));
                Assert.True(await alice2.Received.WaitAsync(TimeSpan.FromSeconds(5), ct));
                Assert.False(await bob.Received.WaitAsync(TimeSpan.FromMilliseconds(500), ct));

                var invokeResponse = await http.PostAsync($"{_fixture.ServerUrl1}/__test/invoke-user-all-getbyid?userId=alice&id=user-scope", null, ct);
                invokeResponse.EnsureSuccessStatusCode();

                var body = await invokeResponse.Content.ReadAsStringAsync(ct);
                var results = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
                Assert.Equal(2, results.GetArrayLength());

                foreach (var item in results.EnumerateArray()) {
                    Assert.Contains("user-scope", item.GetProperty("value").GetString());
                }
            } finally {
                await connectionAlice1.StopAsync(ct);
                await connectionAlice1.DisposeAsync();
                await connectionAlice2.StopAsync(ct);
                await connectionAlice2.DisposeAsync();
                await connectionBob.StopAsync(ct);
                await connectionBob.DisposeAsync();
            }
        }

        [Fact]
        public async Task AttributeTargeting_CrossNode_OnlyMatchingConnectionsReceiveAndInvoke() {
            var ct = TestContext.Current.CancellationToken;
            var admin1 = new BackplaneNixCounter();
            var admin2 = new BackplaneNixCounter();
            var viewer = new BackplaneNixCounter();

            var connectionAdmin1 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub?%40role=admin"));
            connectionAdmin1.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(admin1);
            await connectionAdmin1.StartAsync(ct);

            var connectionAdmin2 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub?%40role=admin"));
            connectionAdmin2.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(admin2);
            await connectionAdmin2.StartAsync(ct);

            var connectionViewer = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub?%40role=viewer"));
            connectionViewer.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(viewer);
            await connectionViewer.StartAsync(ct);

            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connectionAdmin1, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, connectionAdmin2, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, connectionViewer, ct);

            // Targeting below happens on node 1 and has to reach connections on node 2.
            await WaitForCrossNodeVisibility(_fixture.ServerUrl1, connectionAdmin2, ct);
            await WaitForCrossNodeVisibility(_fixture.ServerUrl1, connectionViewer, ct);

            try {
                using var http = new HttpClient();

                var broadcastResponse = await http.PostAsync($"{_fixture.ServerUrl1}/__test/broadcast-filtered-nix?tag=admin", null, ct);
                broadcastResponse.EnsureSuccessStatusCode();

                Assert.True(await admin1.Received.WaitAsync(TimeSpan.FromSeconds(5), ct));
                Assert.True(await admin2.Received.WaitAsync(TimeSpan.FromSeconds(5), ct));
                Assert.False(await viewer.Received.WaitAsync(TimeSpan.FromMilliseconds(500), ct));

                var invokeResponse = await http.PostAsync($"{_fixture.ServerUrl1}/__test/invoke-attribute-all-getbyid?tag=admin&id=attr-scope", null, ct);
                invokeResponse.EnsureSuccessStatusCode();

                var body = await invokeResponse.Content.ReadAsStringAsync(ct);
                var results = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
                Assert.Equal(2, results.GetArrayLength());

                foreach (var item in results.EnumerateArray()) {
                    Assert.Contains("attr-scope", item.GetProperty("value").GetString());
                }
            } finally {
                await connectionAdmin1.StopAsync(ct);
                await connectionAdmin1.DisposeAsync();
                await connectionAdmin2.StopAsync(ct);
                await connectionAdmin2.DisposeAsync();
                await connectionViewer.StopAsync(ct);
                await connectionViewer.DisposeAsync();
            }
        }

        [Fact]
        public async Task PresenceMetadata_CrossNode_ReportsUsersNodesGroupsAndAttributes() {
            var ct = TestContext.Current.CancellationToken;

            var connectionAlice1 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub?userId=alice&%40role=admin"));
            connectionAlice1.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(new BackplaneNixCounter());
            await connectionAlice1.StartAsync(ct);

            var connectionAlice2 = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub?userId=alice&%40role=editor"));
            connectionAlice2.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(new BackplaneNixCounter());
            await connectionAlice2.StartAsync(ct);

            var connectionBob = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub?userId=bob&%40role=viewer"));
            connectionBob.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(new BackplaneNixCounter());
            await connectionBob.StartAsync(ct);

            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connectionAlice1, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, connectionAlice2, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, connectionBob, ct);

            try {
                using var http = new HttpClient();
                await WaitForPresenceContainsConnection($"{_fixture.ServerUrl2}/__test/presence-all", connectionAlice1.ConnectionId!, ct);

                var joinResponse = await http.GetAsync(
                    $"{_fixture.ServerUrl2}/__test/join-group?connectionId={Uri.EscapeDataString(connectionAlice1.ConnectionId ?? string.Empty)}&group={Uri.EscapeDataString("doc-123")}",
                    ct);
                joinResponse.EnsureSuccessStatusCode();

                await WaitForClientGroup(_fixture.ServerUrl1, connectionAlice1.ConnectionId!, "doc-123", ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl1}/__test/presence-user?userId=alice", 2, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl1}/__test/presence-group?group=doc-123", 1, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl1}/__test/presence-attribute?key=role&value=admin", 1, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl1}/__test/presence-online-users", 2, ct);

                var aliceResponse = await http.GetAsync($"{_fixture.ServerUrl1}/__test/presence-user?userId=alice", ct);
                aliceResponse.EnsureSuccessStatusCode();
                var aliceSnapshots = JsonSerializer.Deserialize<JsonElement>(await aliceResponse.Content.ReadAsStringAsync(ct));
                Assert.Equal(2, aliceSnapshots.GetArrayLength());

                var nodeIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in aliceSnapshots.EnumerateArray()) {
                    nodeIds.Add(item.GetProperty("nodeId").GetString()!);
                }
                Assert.Contains("node-1", nodeIds);
                Assert.Contains("node-2", nodeIds);

                var groupResponse = await http.GetAsync($"{_fixture.ServerUrl1}/__test/presence-group?group=doc-123", ct);
                groupResponse.EnsureSuccessStatusCode();
                var groupSnapshots = JsonSerializer.Deserialize<JsonElement>(await groupResponse.Content.ReadAsStringAsync(ct));
                Assert.Single(groupSnapshots.EnumerateArray());
                Assert.Equal(connectionAlice1.ConnectionId, groupSnapshots[0].GetProperty("connectionId").GetString());

                var attributeResponse = await http.GetAsync($"{_fixture.ServerUrl1}/__test/presence-attribute?key=role&value=admin", ct);
                attributeResponse.EnsureSuccessStatusCode();
                var attributeSnapshots = JsonSerializer.Deserialize<JsonElement>(await attributeResponse.Content.ReadAsStringAsync(ct));
                Assert.Single(attributeSnapshots.EnumerateArray());
                Assert.Equal(connectionAlice1.ConnectionId, attributeSnapshots[0].GetProperty("connectionId").GetString());

                var usersResponse = await http.GetAsync($"{_fixture.ServerUrl1}/__test/presence-online-users", ct);
                usersResponse.EnsureSuccessStatusCode();
                var users = JsonSerializer.Deserialize<JsonElement>(await usersResponse.Content.ReadAsStringAsync(ct));
                Assert.Equal(2, users.GetArrayLength());

                var alicePresence = users.EnumerateArray().First(u => u.GetProperty("userId").GetString() == "alice");
                Assert.Equal(2, alicePresence.GetProperty("connectionIds").GetArrayLength());
                Assert.Equal(2, alicePresence.GetProperty("nodeIds").GetArrayLength());

                var onlineResponse = await http.GetAsync($"{_fixture.ServerUrl1}/__test/presence-user-online?userId=alice", ct);
                onlineResponse.EnsureSuccessStatusCode();
                Assert.Equal("true", await onlineResponse.Content.ReadAsStringAsync(ct));
            } finally {
                await connectionAlice1.StopAsync(ct);
                await connectionAlice1.DisposeAsync();
                await connectionAlice2.StopAsync(ct);
                await connectionAlice2.DisposeAsync();
                await connectionBob.StopAsync(ct);
                await connectionBob.DisposeAsync();
            }
        }

        [Fact]
        public async Task ActiveCleanup_RemovesStalePresenceAfterNodeCrash() {
            using var isolatedFixture = _fixture.CreateIsolatedFixture(
                heartbeatInterval: TimeSpan.FromMilliseconds(200),
                nodeTimeout: TimeSpan.FromMilliseconds(900));

            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{isolatedFixture.ServerUrl1}/signalr/testhub?userId=alice&%40role=admin"));
            connection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(new BackplaneNixCounter());
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(isolatedFixture.ServerUrl1, connection, ct);

            try {
                using var http = new HttpClient();
                await WaitForPresenceContainsConnection($"{isolatedFixture.ServerUrl2}/__test/presence-all", connection.ConnectionId!, ct);

                var joinResponse = await http.GetAsync(
                    $"{isolatedFixture.ServerUrl2}/__test/join-group?connectionId={Uri.EscapeDataString(connection.ConnectionId ?? string.Empty)}&group={Uri.EscapeDataString("doc-crash")}",
                    ct);
                joinResponse.EnsureSuccessStatusCode();

                await WaitForPresenceArrayLength($"{isolatedFixture.ServerUrl2}/__test/presence-user?userId=alice", 1, ct);
                await WaitForPresenceArrayLength($"{isolatedFixture.ServerUrl2}/__test/presence-group?group=doc-crash", 1, ct);
                await WaitForPresenceArrayLength($"{isolatedFixture.ServerUrl2}/__test/presence-attribute?key=role&value=admin", 1, ct);

                isolatedFixture.KillServer1();

                await WaitForPresenceArrayLength($"{isolatedFixture.ServerUrl2}/__test/presence-user?userId=alice", 0, ct);
                await WaitForPresenceArrayLength($"{isolatedFixture.ServerUrl2}/__test/presence-group?group=doc-crash", 0, ct);
                await WaitForPresenceArrayLength($"{isolatedFixture.ServerUrl2}/__test/presence-attribute?key=role&value=admin", 0, ct);

                var onlineResponse = await http.GetAsync($"{isolatedFixture.ServerUrl2}/__test/presence-user-online?userId=alice", ct);
                onlineResponse.EnsureSuccessStatusCode();
                Assert.Equal("false", await onlineResponse.Content.ReadAsStringAsync(ct));
            } finally {
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task RemoteLeaveGroup_RemovesCrossNodeMembershipAndStopsDelivery() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new BackplaneNixCounter();

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            try {
                using var http = new HttpClient();
                await WaitForPresenceContainsConnection($"{_fixture.ServerUrl2}/__test/presence-all", connection.ConnectionId!, ct);
                var connectionId = Uri.EscapeDataString(connection.ConnectionId ?? string.Empty);
                var groupName = Uri.EscapeDataString("remote-leave");

                var joinResponse = await http.GetAsync($"{_fixture.ServerUrl2}/__test/join-group?connectionId={connectionId}&group={groupName}", ct);
                joinResponse.EnsureSuccessStatusCode();

                await WaitForClientGroup(_fixture.ServerUrl1, connection.ConnectionId!, "remote-leave", ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl2}/__test/presence-group?group=remote-leave", 1, ct);

                var leaveResponse = await http.GetAsync($"{_fixture.ServerUrl2}/__test/leave-group?connectionId={connectionId}&group={groupName}", ct);
                leaveResponse.EnsureSuccessStatusCode();

                await WaitForClientNotInGroup(_fixture.ServerUrl1, connection.ConnectionId!, "remote-leave", ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl2}/__test/presence-group?group=remote-leave", 0, ct);

                var broadcastResponse = await http.PostAsync($"{_fixture.ServerUrl2}/__test/broadcast-group-nix?group=remote-leave", null, ct);
                broadcastResponse.EnsureSuccessStatusCode();

                Assert.False(await handler.Received.WaitAsync(TimeSpan.FromMilliseconds(500), ct));
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task DisconnectReconnect_ReplacesDistributedUserAndAttributeRouting() {
            var ct = TestContext.Current.CancellationToken;
            var firstHandler = new BackplaneNixCounter();
            var secondHandler = new BackplaneNixCounter();

            HARRRConnection? firstConnection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub?userId=alice&%40role=author"));
            firstConnection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(firstHandler);
            await firstConnection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, firstConnection, ct);

            HARRRConnection? secondConnection = null;
            try {
                using var http = new HttpClient();
                await WaitForPresenceContainsConnection($"{_fixture.ServerUrl2}/__test/presence-all", firstConnection.ConnectionId!, ct);

                var joinResponse = await http.GetAsync(
                    $"{_fixture.ServerUrl2}/__test/join-group?connectionId={Uri.EscapeDataString(firstConnection.ConnectionId ?? string.Empty)}&group={Uri.EscapeDataString("doc-reconnect")}",
                    ct);
                joinResponse.EnsureSuccessStatusCode();

                await WaitForPresenceArrayLength($"{_fixture.ServerUrl2}/__test/presence-user?userId=alice", 1, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl2}/__test/presence-group?group=doc-reconnect", 1, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl2}/__test/presence-attribute?key=role&value=author", 1, ct);

                await firstConnection.StopAsync(ct);
                await firstConnection.DisposeAsync();
                firstConnection = null;

                await WaitForPresenceArrayLength($"{_fixture.ServerUrl2}/__test/presence-user?userId=alice", 0, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl2}/__test/presence-group?group=doc-reconnect", 0, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl2}/__test/presence-attribute?key=role&value=author", 0, ct);

                var onlineResponse = await http.GetAsync($"{_fixture.ServerUrl2}/__test/presence-user-online?userId=alice", ct);
                onlineResponse.EnsureSuccessStatusCode();
                Assert.Equal("false", await onlineResponse.Content.ReadAsStringAsync(ct));

                secondConnection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl2}/signalr/testhub?userId=alice&%40role=author"));
                secondConnection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(secondHandler);
                await secondConnection.StartAsync(ct);
                await TestHelper.WaitForClientRegistration(_fixture.ServerUrl2, secondConnection, ct);

                await WaitForPresenceArrayLength($"{_fixture.ServerUrl1}/__test/presence-user?userId=alice", 1, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl1}/__test/presence-group?group=doc-reconnect", 0, ct);
                await WaitForPresenceArrayLength($"{_fixture.ServerUrl1}/__test/presence-attribute?key=role&value=author", 1, ct);

                var broadcastResponse = await http.PostAsync($"{_fixture.ServerUrl1}/__test/broadcast-user-nix?userId=alice", null, ct);
                broadcastResponse.EnsureSuccessStatusCode();
                Assert.True(await secondHandler.Received.WaitAsync(TimeSpan.FromSeconds(5), ct));

                var invokeResponse = await http.PostAsync($"{_fixture.ServerUrl1}/__test/invoke-user-all-getbyid?userId=alice&id=reconnected", null, ct);
                invokeResponse.EnsureSuccessStatusCode();

                var body = await invokeResponse.Content.ReadAsStringAsync(ct);
                var results = JsonSerializer.Deserialize<JsonElement>(body);
                Assert.Single(results.EnumerateArray());
                Assert.Equal(secondConnection.ConnectionId, results[0].GetProperty("clientId").GetString());
                Assert.Contains("reconnected", results[0].GetProperty("value").GetString());
            } finally {
                if (firstConnection != null) {
                    await firstConnection.StopAsync(ct);
                    await firstConnection.DisposeAsync();
                }

                if (secondConnection != null) {
                    await secondConnection.StopAsync(ct);
                    await secondConnection.DisposeAsync();
                }
            }
        }

        [Fact]
        public async Task SequentialCrossNodePushes_PreserveOrderWithoutDuplicates() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new OrderedPushClient();
            var messages = new[] { "seq-1", "seq-2", "seq-3" };

            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl1}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, OrderedPushClient>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl1, connection, ct);

            // The pushes are issued on node 2 for a connection that lives on node 1, so node 2 can
            // only route them once it sees this connection in the distributed registry. This was the
            // one cross-node test in this class that went straight from the node-1 registration wait
            // to acting on node 2, and it lost that race on the slowest CI leg.
            await WaitForCrossNodeVisibility(_fixture.ServerUrl2, connection, ct);

            try {
                using var http = new HttpClient();
                var connectionId = Uri.EscapeDataString(connection.ConnectionId ?? string.Empty);

                foreach (var message in messages) {
                    var response = await http.PostAsync(
                        $"{_fixture.ServerUrl2}/__test/push-notification?connectionId={connectionId}&message={Uri.EscapeDataString(message)}",
                        null,
                        ct);
                    response.EnsureSuccessStatusCode();
                }

                await WaitForPushCount(handler, messages.Length, ct);
                Assert.Equal(messages, handler.GetMessages());

                await Task.Delay(300, ct);
                Assert.Equal(messages.Length, handler.PushCount);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task InvokeAll_RemainsOperationalAfterRemoteNodeCrashAndCleanup() {
            using var isolatedFixture = _fixture.CreateIsolatedFixture(
                heartbeatInterval: TimeSpan.FromMilliseconds(200),
                nodeTimeout: TimeSpan.FromMilliseconds(900));

            var ct = TestContext.Current.CancellationToken;
            var localHandler = new BackplaneNixCounter();
            var remoteHandler = new BackplaneNixCounter();

            var remoteConnection = HARRRConnection.Create(builder => builder.WithUrl($"{isolatedFixture.ServerUrl1}/signalr/testhub"));
            remoteConnection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(remoteHandler);
            await remoteConnection.StartAsync(ct);

            var localConnection = HARRRConnection.Create(builder => builder.WithUrl($"{isolatedFixture.ServerUrl2}/signalr/testhub"));
            localConnection.RegisterInterface<TestShared.ITestClientMethods, BackplaneNixCounter>(localHandler);
            await localConnection.StartAsync(ct);

            await TestHelper.WaitForClientRegistration(isolatedFixture.ServerUrl1, remoteConnection, ct);
            await TestHelper.WaitForClientRegistration(isolatedFixture.ServerUrl2, localConnection, ct);

            try {
                using var http = new HttpClient();
                await WaitForPresenceArrayLength($"{isolatedFixture.ServerUrl2}/__test/presence-all", 2, ct);

                isolatedFixture.KillServer1();

                await WaitForPresenceArrayLength($"{isolatedFixture.ServerUrl2}/__test/presence-all", 1, ct);

                var invokeResponse = await http.PostAsync($"{isolatedFixture.ServerUrl2}/__test/invoke-all-getbyid?id=after-crash", null, ct);
                invokeResponse.EnsureSuccessStatusCode();

                var body = await invokeResponse.Content.ReadAsStringAsync(ct);
                var results = JsonSerializer.Deserialize<JsonElement>(body);
                Assert.Single(results.EnumerateArray());
                Assert.Equal(localConnection.ConnectionId, results[0].GetProperty("clientId").GetString());
                Assert.Contains("after-crash", results[0].GetProperty("value").GetString());
            } finally {
                await localConnection.StopAsync(ct);
                await localConnection.DisposeAsync();
                await remoteConnection.DisposeAsync();
            }
        }

        private static async Task WaitForClientGroup(string serverUrl, string connectionId, string groupName, CancellationToken cancellationToken) {
            using var http = new HttpClient();
            for (int i = 0; i < 50; i++) {
                var response = await http.GetAsync($"{serverUrl}/__test/client-groups?connectionId={Uri.EscapeDataString(connectionId)}", cancellationToken);
                if (response.IsSuccessStatusCode) {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (body.Contains(groupName, StringComparison.Ordinal)) {
                        return;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"Client '{connectionId}' was not added to group '{groupName}' in time.");
        }

        private static async Task WaitForClientNotInGroup(string serverUrl, string connectionId, string groupName, CancellationToken cancellationToken) {
            using var http = new HttpClient();
            for (int i = 0; i < 50; i++) {
                var response = await http.GetAsync($"{serverUrl}/__test/client-groups?connectionId={Uri.EscapeDataString(connectionId)}", cancellationToken);
                if (response.IsSuccessStatusCode) {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!body.Contains(groupName, StringComparison.Ordinal)) {
                        return;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"Client '{connectionId}' was not removed from group '{groupName}' in time.");
        }

        /// <summary>
        /// Wait until <paramref name="observerUrl"/> can see <paramref name="connection"/> in the
        /// distributed registry, i.e. until it could target that connection across nodes.
        /// </summary>
        /// <remarks>
        /// <see cref="TestHelper.WaitForClientRegistration"/> is not enough before a cross-node
        /// operation: /__test/client-exists asks the in-memory ClientManager of the node it is sent
        /// to, and a connection is registered locally *before* it is written to the distributed
        /// registry. So the owning node answers "yes, I know this client" while other nodes still
        /// cannot see it — measured at around 100 ms under test load.
        /// <para>
        /// That window is enough to lose a message permanently, because a broadcast is a one-shot:
        /// whoever is invisible at the moment it is sent never receives it, and waiting for the
        /// message afterwards cannot recover it. Cross-node tests must therefore wait for
        /// convergence *before* acting.
        /// </para>
        /// </remarks>
        private static Task WaitForCrossNodeVisibility(string observerUrl, HARRRConnection connection, CancellationToken cancellationToken) =>
            WaitForPresenceContainsConnection($"{observerUrl}/__test/presence-all", connection.ConnectionId!, cancellationToken);

        private static async Task WaitForPresenceArrayLength(string url, int expectedLength, CancellationToken cancellationToken) {
            using var http = new HttpClient();
            for (int i = 0; i < 50; i++) {
                var response = await http.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode) {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var payload = JsonSerializer.Deserialize<JsonElement>(body);
                    if (payload.ValueKind == JsonValueKind.Array && payload.GetArrayLength() == expectedLength) {
                        return;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"Presence endpoint '{url}' did not reach expected array length {expectedLength} in time.");
        }

        private static async Task WaitForPresenceContainsConnection(string url, string connectionId, CancellationToken cancellationToken) {
            using var http = new HttpClient();
            for (int i = 0; i < 50; i++) {
                var response = await http.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode) {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    var payload = JsonSerializer.Deserialize<JsonElement>(body);
                    if (payload.ValueKind == JsonValueKind.Array
                        && payload.EnumerateArray().Any(item => string.Equals(item.GetProperty("connectionId").GetString(), connectionId, StringComparison.Ordinal))) {
                        return;
                    }
                }

                await Task.Delay(100, cancellationToken);
            }

            throw new TimeoutException($"Presence endpoint '{url}' did not report connection '{connectionId}' in time.");
        }

        private static async Task WaitForPushCount(OrderedPushClient handler, int expectedCount, CancellationToken cancellationToken) {
            while (handler.PushCount < expectedCount) {
                var received = await handler.PushReceived.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                Assert.True(received);
            }
        }

        private sealed class BackplaneNixCounter : TestShared.ITestClientMethods {
            public SemaphoreSlim Received { get; } = new SemaphoreSlim(0);

            public void Nix() => Received.Release();
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

        private sealed class OrderedPushClient : ITestServerPushClient {
            private readonly List<string> _messages = new List<string>();
            private readonly object _sync = new object();

            public int PushCount { get; private set; }
            public SemaphoreSlim PushReceived { get; } = new SemaphoreSlim(0);

            public void PushNotification(string message) {
                lock (_sync) {
                    _messages.Add(message);
                    PushCount++;
                }

                PushReceived.Release();
            }

            public void ConfigUpdated(string? path, string configJson) {
            }

            public Task<string> RequestClientInfo() => Task.FromResult("ordered-client");

            public IReadOnlyList<string> GetMessages() {
                lock (_sync) {
                    return _messages.ToArray();
                }
            }
        }
    }

    [Collection("Backplane")]
    public sealed class BackplaneIntegrationTests : BackplaneIntegrationTestsBase {
        public BackplaneIntegrationTests(RedisMultiNodeSignalARRRServerFixture fixture) : base(fixture) {
        }
    }

    [Collection("PostgresBackplane")]
    public sealed class PostgresBackplaneIntegrationTests : BackplaneIntegrationTestsBase {
        public PostgresBackplaneIntegrationTests(PostgresMultiNodeSignalARRRServerFixture fixture) : base(fixture) {
        }
    }
}
