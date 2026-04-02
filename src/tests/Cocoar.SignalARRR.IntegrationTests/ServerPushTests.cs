using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// Client-side implementation of the cross-assembly server→client contract.
    /// Defined in the TEST project, NOT in SharedModels — mirrors the real-world pattern
    /// where the server defines the contract and the client implements it.
    /// </summary>
    public class TestServerPushClientImpl : ITestServerPushClient {
        public string? LastPushMessage { get; private set; }
        public string? LastConfigPath { get; private set; }
        public string? LastConfigJson { get; private set; }
        public int PushCount { get; private set; }
        public SemaphoreSlim PushReceived { get; } = new SemaphoreSlim(0);
        public SemaphoreSlim ConfigUpdateReceived { get; } = new SemaphoreSlim(0);

        public void PushNotification(string message) {
            LastPushMessage = message;
            PushCount++;
            PushReceived.Release();
        }

        public void ConfigUpdated(string? path, string configJson) {
            LastConfigPath = path;
            LastConfigJson = configJson;
            ConfigUpdateReceived.Release();
        }

        public Task<string> RequestClientInfo() {
            return Task.FromResult($"TestClient-{System.Environment.ProcessId}");
        }
    }

    /// <summary>
    /// Tests server-to-client push using a contract interface defined in a SEPARATE assembly
    /// (Cocoar.SignalARRR.Tests.SharedModels). This is the real-world pattern where:
    /// - SharedModels defines [SignalARRRContract] interfaces
    /// - Server references SharedModels and uses GetTypedMethods&lt;T&gt;() to push
    /// - Client references SharedModels and registers an implementation via RegisterInterface&lt;T&gt;()
    /// </summary>
    [Collection("Simple")]
    public class ServerPushTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public ServerPushTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        [Fact]
        public async Task PushNotification_CrossAssembly_ClientReceives() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new TestServerPushClientImpl();

            var connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/push-notification?connectionId={connection.ConnectionId}&message=hello-from-server";
                var response = await http.PostAsync(url, null, ct);
                response.EnsureSuccessStatusCode();

                // Wait for the push to arrive (fire-and-forget, need to wait)
                var received = await handler.PushReceived.WaitAsync(TimeSpan.FromSeconds(5), ct);
                Assert.True(received, "Client did not receive push notification within 5 seconds");
                Assert.Equal("hello-from-server", handler.LastPushMessage);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task RequestClientInfo_CrossAssembly_ReturnsValue() {
            var ct = TestContext.Current.CancellationToken;
            var handler = new TestServerPushClientImpl();

            var connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/request-client-info?connectionId={connection.ConnectionId}";
                var response = await http.PostAsync(url, null, ct);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync(ct);
                Assert.Contains("TestClient-", result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task ConfigUpdated_NullablePath_ClientReceives() {
            // Exact ConfigHub pattern: void ConfigUpdated(string? path, string configJson)
            // with null as the first argument
            var ct = TestContext.Current.CancellationToken;
            var handler = new TestServerPushClientImpl();

            var connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/config-updated?connectionId={connection.ConnectionId}&configJson={{\"key\":\"value\"}}";
                var response = await http.PostAsync(url, null, ct);
                response.EnsureSuccessStatusCode();

                var received = await handler.ConfigUpdateReceived.WaitAsync(TimeSpan.FromSeconds(5), ct);
                Assert.True(received, "Client did not receive ConfigUpdated push within 5 seconds");
                Assert.Null(handler.LastConfigPath);
                Assert.Contains("key", handler.LastConfigJson);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task PushNotification_MultipleClients_AllReceive() {
            var ct = TestContext.Current.CancellationToken;
            var handler1 = new TestServerPushClientImpl();
            var handler2 = new TestServerPushClientImpl();

            var conn1 = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            conn1.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler1);
            await conn1.StartAsync(ct);

            var conn2 = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            conn2.RegisterInterface<ITestServerPushClient, TestServerPushClientImpl>(handler2);
            await conn2.StartAsync(ct);

            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, conn1, ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, conn2, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/push-notification-all?message=broadcast-test";
                var response = await http.PostAsync(url, null, ct);
                response.EnsureSuccessStatusCode();

                // Both clients should receive
                var received1 = await handler1.PushReceived.WaitAsync(TimeSpan.FromSeconds(5), ct);
                var received2 = await handler2.PushReceived.WaitAsync(TimeSpan.FromSeconds(5), ct);

                Assert.True(received1, "Client 1 did not receive push");
                Assert.True(received2, "Client 2 did not receive push");
                Assert.Equal("broadcast-test", handler1.LastPushMessage);
                Assert.Equal("broadcast-test", handler2.LastPushMessage);
            } finally {
                await conn1.StopAsync(ct);
                await conn1.DisposeAsync();
                await conn2.StopAsync(ct);
                await conn2.DisposeAsync();
            }
        }
    }
}
