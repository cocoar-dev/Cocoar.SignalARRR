using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class BroadcastTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly HARRRConnection _connection1;
        private readonly HARRRConnection _connection2;
        private int _nixCallCount1;
        private int _nixCallCount2;
        private NixCounter _client1 = null!;
        private NixCounter _client2 = null!;

        public BroadcastTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;

            _connection1 = HARRRConnection.Create(builder => {
                builder.WithUrl($"{fixture.ServerUrl}/signalr/testhub");
            });

            _connection2 = HARRRConnection.Create(builder => {
                builder.WithUrl($"{fixture.ServerUrl}/signalr/testhub");
            });
        }

        public async ValueTask InitializeAsync() {
            _nixCallCount1 = 0;
            _nixCallCount2 = 0;

            _client1 = new NixCounter(() => Interlocked.Increment(ref _nixCallCount1));
            _client2 = new NixCounter(() => Interlocked.Increment(ref _nixCallCount2));

            _connection1.RegisterInterface<TestShared.ITestClientMethods, NixCounter>(_client1);
            _connection2.RegisterInterface<TestShared.ITestClientMethods, NixCounter>(_client2);

            await _connection1.StartAsync();
            await _connection2.StartAsync();
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, _connection1, TestContext.Current.CancellationToken);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, _connection2, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() {
            await _connection1.StopAsync();
            await _connection1.DisposeAsync();
            await _connection2.StopAsync();
            await _connection2.DisposeAsync();
        }

        [Fact]
        public async Task WithHub_WithGroup_OnlyGroupMembersReceive() {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();

            // Only connection1 joins the group
            await http.GetAsync($"{_fixture.ServerUrl}/__test/join-group?connectionId={_connection1.ConnectionId}&group=testgroup", ct);

            // Broadcast via WithHub<TestHub>().WithGroup("testgroup").SendAsync
            var response = await http.PostAsync($"{_fixture.ServerUrl}/__test/broadcast-group-nix?group=testgroup", null, ct);
            response.EnsureSuccessStatusCode();

            await Task.Delay(500, ct);

            Assert.True(_nixCallCount1 >= 1, $"Connection1 (in group) should have received Nix but got {_nixCallCount1} calls");
            Assert.Equal(0, _nixCallCount2);
        }

        [Fact]
        public async Task WithHub_SendAsync_AllClientsReceive() {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();

            // Broadcast via WithHub<TestHub>().SendAsync
            var response = await http.PostAsync($"{_fixture.ServerUrl}/__test/broadcast-all-nix", null, ct);
            response.EnsureSuccessStatusCode();

            await Task.Delay(500, ct);

            Assert.True(_nixCallCount1 >= 1, $"Connection1 should have received Nix but got {_nixCallCount1} calls");
            Assert.True(_nixCallCount2 >= 1, $"Connection2 should have received Nix but got {_nixCallCount2} calls");
        }

        [Fact]
        public async Task WithHub_WithAttributeFilter_SendAsync() {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();

            // SendAsync with no tag filter — all clients on TestHub
            var response = await http.PostAsync($"{_fixture.ServerUrl}/__test/broadcast-filtered-nix", null, ct);
            response.EnsureSuccessStatusCode();

            await Task.Delay(500, ct);

            Assert.True(_nixCallCount1 >= 1, $"Connection1 should have received Nix but got {_nixCallCount1} calls");
            Assert.True(_nixCallCount2 >= 1, $"Connection2 should have received Nix but got {_nixCallCount2} calls");
        }

        [Fact]
        public async Task AddToGroupAsync_TracksGroupInClientContext() {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();

            // Add connection1 to two groups
            await http.GetAsync($"{_fixture.ServerUrl}/__test/join-group?connectionId={_connection1.ConnectionId}&group=group-a", ct);
            await http.GetAsync($"{_fixture.ServerUrl}/__test/join-group?connectionId={_connection1.ConnectionId}&group=group-b", ct);

            // Query groups via test endpoint
            var response = await http.GetAsync($"{_fixture.ServerUrl}/__test/client-groups?connectionId={_connection1.ConnectionId}", ct);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(ct);

            Assert.Contains("group-a", body);
            Assert.Contains("group-b", body);
        }

        [Fact]
        public async Task InvokeAllAsync_ReturnsResultPerClient() {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();

            // InvokeAllAsync — calls GetById("test") on each client, collects results
            var response = await http.PostAsync($"{_fixture.ServerUrl}/__test/invoke-all-getbyid?id=test", null, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            var results = JsonSerializer.Deserialize<JsonElement>(body);

            // Should have results from both clients
            Assert.Equal(2, results.GetArrayLength());

            // Each result should contain "result-test" (from GetById implementation)
            foreach (var item in results.EnumerateArray()) {
                var value = item.GetProperty("value").GetString();
                Assert.Contains("result-test", value);
            }
        }

        [Fact]
        public async Task InvokeOneAsync_ReturnsFirstResponder() {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();

            var response = await http.PostAsync($"{_fixture.ServerUrl}/__test/invoke-one-getbyid?id=hello", null, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<JsonElement>(body);

            // Should have exactly one result from one of the two clients
            var clientId = result.GetProperty("clientId").GetString();
            var value = result.GetProperty("value").GetString();

            Assert.NotNull(clientId);
            Assert.Contains("result-hello", value);

            // The responding client should be one of our two connections
            Assert.True(
                clientId == _connection1.ConnectionId || clientId == _connection2.ConnectionId,
                $"Responding client {clientId} is not one of our connections");
        }

        [Fact]
        public async Task SendAsync_ServerMethodJoinsGroup_GroupBroadcastReachesClient() {
            // Regression for the fire-and-forget SendMessage bug: a `send`-routed server method that
            // joins a group used to run after the Hub was disposed, so the join silently failed and
            // later group broadcasts never reached the client. With the fix (await within the hub
            // invocation) the join takes effect and the broadcast is delivered.
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();
            var group = "send-join-" + Guid.NewGuid().ToString("N");

            // connection1 joins the group via a fire-and-forget `send` (Task-returning server method).
            await _connection1.SendAsync("SubscribeViaSend", group, ct);

            // Wait until the server has actually processed the join (proves the send-routed method ran
            // against a live Hub). Pre-fix this never happened, so this poll would time out.
            await WaitForGroupMembership(_fixture.ServerUrl, _connection1.ConnectionId!, group, ct);

            // Broadcast to the group; only members receive Nix.
            var response = await http.PostAsync($"{_fixture.ServerUrl}/__test/broadcast-group-nix?group={group}", null, ct);
            response.EnsureSuccessStatusCode();

            await Task.Delay(500, ct);

            Assert.True(_nixCallCount1 >= 1, $"Connection1 joined '{group}' via send; expected Nix but got {_nixCallCount1}");
            Assert.Equal(0, _nixCallCount2);
        }

        private static async Task WaitForGroupMembership(
            string serverUrl, string connectionId, string group, CancellationToken ct) {
            using var http = new HttpClient();
            for (int i = 0; i < 50; i++) {
                var response = await http.GetAsync(
                    $"{serverUrl}/__test/client-groups?connectionId={Uri.EscapeDataString(connectionId)}", ct);
                if (response.IsSuccessStatusCode) {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    if (body.Contains(group)) return;
                }
                await Task.Delay(100, ct);
            }
            throw new TimeoutException(
                $"Connection {connectionId} did not join group '{group}' within 5 seconds — " +
                "the send-routed server method did not run against a live Hub.");
        }

        /// <summary>
        /// A broadcast call may carry a cancellation token, and cancelling it reaches every
        /// recipient (N-4, variant C).
        /// </summary>
        /// <remarks>
        /// Two things must hold. The token argument travels as a reference in its declared slot,
        /// so the preceding argument binds unshifted — <c>seconds</c> must arrive as 30 on both
        /// clients, exactly what the earlier rejection protected. And cancelling the server-side
        /// token sends <c>CancelTokenFromServer</c> to the same set the call went to, so both
        /// recipients observe the cancellation. The cancel is triggered only after the test has
        /// seen the call arrive — the timing belongs to the test, not to a fixed server delay.
        /// </remarks>
        [Fact]
        public async Task BroadcastWithCancellationToken_ReachesAllRecipientsAndCancels() {
            var ct = TestContext.Current.CancellationToken;

            using var http = new HttpClient();
            var response = await http.PostAsync(
                $"{_fixture.ServerUrl}/__test/broadcast-wait-with-token", null, ct);
            response.EnsureSuccessStatusCode();
            var probeId = (await response.Content.ReadAsStringAsync(ct)).Trim('"');

            await TestHelper.WaitFor(
                () => _client1.LastWaitSeconds == 30 && _client2.LastWaitSeconds == 30,
                "both broadcast recipients receiving Wait with seconds bound to 30");
            Assert.False(_client1.WaitObservedCancellation);
            Assert.False(_client2.WaitObservedCancellation);

            var cancelResponse = await http.PostAsync(
                $"{_fixture.ServerUrl}/__test/broadcast-wait-cancel?probeId={Uri.EscapeDataString(probeId)}", null, ct);
            cancelResponse.EnsureSuccessStatusCode();

            await TestHelper.WaitFor(
                () => _client1.WaitObservedCancellation && _client2.WaitObservedCancellation,
                "both broadcast recipients observing the cancellation");
        }

        private class NixCounter : TestShared.ITestClientMethods {
            private readonly Action _onNix;
            public NixCounter(Action onNix) => _onNix = onNix;

            /// <summary>The <c>seconds</c> the last <see cref="Wait"/> received, to catch a shifted argument.</summary>
            public int? LastWaitSeconds { get; private set; }

            /// <summary>Whether the token this recipient was given actually fired.</summary>
            public bool WaitObservedCancellation { get; private set; }

            public void Nix() => _onNix();
            public T Invoke<T>(string command, Dictionary<string, object>? variables = null) => default!;
            public List<string> GetContent(int count) => new();
            public string GetById(string id) => $"result-{id}";
            public string GetByGenericId(Guid id) => $"guid-{id}";
            public Task<string> Wait(int seconds, CancellationToken cancellationToken) {
                LastWaitSeconds = seconds;
                return Task.Run(async () => {
                    try {
                        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                        return "done";
                    } catch (OperationCanceledException) {
                        WaitObservedCancellation = true;
                        throw;
                    }
                }, cancellationToken);
            }
            public bool CreateObject(string className, Dictionary<string, object> properties) => false;
            public bool CreateObjectFromTemplate(string templateName, Dictionary<string, object> properties) => false;
            public long FileLength(string id, System.IO.Stream filestream) => 0;
            public void Complex1(TestShared.ComplexTestClass compl) { }
            public TestShared.IncidentClass TestExpandableObject(TestShared.IncidentClass expandableObject) => expandableObject;
            public IAsyncEnumerable<int> StreamNumbers(int count) => throw new NotSupportedException();
            public System.IO.Stream GetFileStream(string content) => throw new NotSupportedException();
        }
    }
}
