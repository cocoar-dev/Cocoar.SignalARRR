using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// Covers the long-polling transport, which nothing else in the suite exercises.
    /// </summary>
    /// <remarks>
    /// Every other integration test runs over WebSockets, where the connect request stays in flight
    /// for the life of the connection. Long polling is the interesting case for anything SignalARRR
    /// keeps from that request: <c>ClientContext</c> holds <c>httpContext.RequestServices</c>, and
    /// the server-to-client dispatch opens a scope on it long after the poll that produced it
    /// returned. This asserts that a full round trip — server calls the client, awaits its return
    /// value — works over that transport.
    /// </remarks>
    [Collection("Simple")]
    public class LongPollingTransportTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly HARRRConnection _connection;

        public LongPollingTransportTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
            _connection = HARRRConnection.Create(builder => {
                builder.WithUrl($"{fixture.ServerUrl}/signalr/testhub", options => {
                    options.Transports = HttpTransportType.LongPolling;
                    options.SkipNegotiation = false;
                });
            });
        }

        public async ValueTask InitializeAsync() {
            _connection.RegisterInterface<TestShared.ITestClientMethods, TestClientMethodsImpl>(
                new TestClientMethodsImpl());
            await _connection.StartAsync();
            await TestHelper.WaitForClientRegistration(
                _fixture.ServerUrl, _connection, TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }

        [Fact]
        public async Task Server_to_client_call_over_long_polling() {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();

            // A round trip: the server calls the client and awaits its return value, which is the
            // path that opens a scope on the retained connect-time provider.
            var response = await http.PostAsync(
                $"{_fixture.ServerUrl}/__test/trigger-client-getbyid?connectionId={_connection.ConnectionId}&id=probe-42",
                null, ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            Assert.True(response.IsSuccessStatusCode, $"trigger returned {(int)response.StatusCode}: {body}");
            Assert.Contains("probe-42", body);
        }
    }
}
