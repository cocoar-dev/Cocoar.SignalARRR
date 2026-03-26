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

            _connection1.RegisterInterface<TestShared.ITestClientMethods, NixCounter>(new NixCounter(() => Interlocked.Increment(ref _nixCallCount1)));
            _connection2.RegisterInterface<TestShared.ITestClientMethods, NixCounter>(new NixCounter(() => Interlocked.Increment(ref _nixCallCount2)));

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

        private class NixCounter : TestShared.ITestClientMethods {
            private readonly Action _onNix;
            public NixCounter(Action onNix) => _onNix = onNix;

            public void Nix() => _onNix();
            public T Invoke<T>(string command, Dictionary<string, object>? variables = null) => default!;
            public List<string> GetContent(int count) => new();
            public string GetById(string id) => $"result-{id}";
            public string GetByGenericId(Guid id) => $"guid-{id}";
            public Task<string> Wait(int seconds, CancellationToken cancellationToken) => Task.FromResult("");
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
