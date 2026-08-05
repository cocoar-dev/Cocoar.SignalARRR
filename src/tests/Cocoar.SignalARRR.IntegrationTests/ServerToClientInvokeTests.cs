using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class ServerToClientInvokeTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public ServerToClientInvokeTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        [Fact]
        public async Task ServerCallsClient_StreamNumbers_ReturnsAllItems() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-stream?connectionId={connection.ConnectionId}&count=4";
                var response = await http.PostAsync(url, content: null, ct);
                var result = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode) {
                    Assert.Fail($"Server returned {response.StatusCode}: {result}");
                }

                var items = JsonSerializer.Deserialize<int[]>(result);
                Assert.NotNull(items);
                Assert.Equal(new[] { 0, 1, 2, 3 }, items);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task ServerCallsClient_CancelToken_CancelsOperation() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            var clientMethods = new TestClientMethodsImpl();
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(clientMethods);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-cancellation?connectionId={connection.ConnectionId}&delayMs=200";
                var response = await http.PostAsync(url, content: null, ct);
                var result = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode) {
                    Assert.Fail($"Server returned {response.StatusCode}: {result}");
                }

                // Asserted on what the client saw, not on how the server-side await ended: SignalR
                // aborts the pending invocation itself and reports a HubException either way.
                Assert.Equal(30, clientMethods.LastWaitSeconds);
                await TestHelper.WaitFor(
                    () => clientMethods.WaitObservedCancellation,
                    "the client's cancellation token to fire");
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task ServerCallsClient_Nix_VoidMethodCompletes() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-typed-call?connectionId={connection.ConnectionId}";
                var response = await http.PostAsync(url, content: null, ct);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync(ct);
                Assert.Equal("\"Sent\"", result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task ServerCallsClient_GetById_ReturnsSyncStringValue() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-getbyid?connectionId={connection.ConnectionId}&id=test-42";
                var response = await http.PostAsync(url, content: null, ct);
                var result = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode) {
                    Assert.Fail($"Server returned {response.StatusCode}: {result}");
                }

                Assert.Equal("\"test-42\"", result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task ServerCallsClient_GetContent_ReturnsListOfStrings() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-getcontent?connectionId={connection.ConnectionId}&count=3";
                var response = await http.PostAsync(url, content: null, ct);
                var result = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode) {
                    Assert.Fail($"Server returned {response.StatusCode}: {result}");
                }

                var items = JsonSerializer.Deserialize<string[]>(result);
                Assert.NotNull(items);
                Assert.Equal(new[] { "item-0", "item-1", "item-2" }, items);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task ServerCallsClient_GetFileStream_ReturnsStreamViaHttpUpload() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-getfilestream?connectionId={connection.ConnectionId}&content=HelloFromStream";
                var response = await http.PostAsync(url, content: null, ct);
                var result = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode) {
                    Assert.Fail($"Server returned {response.StatusCode}: {result}");
                }

                // Server received the stream content via HTTP upload
                Assert.Contains("HelloFromStream", result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }
    }
}
