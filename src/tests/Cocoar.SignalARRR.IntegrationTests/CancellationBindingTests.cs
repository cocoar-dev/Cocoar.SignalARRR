using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// What a bound <see cref="CancellationToken"/> observes when the connection dies
    /// (N-2 / N-3).
    /// </summary>
    [Collection("Simple")]
    public class CancellationBindingTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public CancellationBindingTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        private HARRRConnection CreateConnection() =>
            HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));

        private static async Task<string> ProbeState(HttpClient http, string serverUrl, string probeId, CancellationToken ct) {
            // The test endpoints are POST-only (MapSignalARRRTest).
            var response = await http.PostAsync($"{serverUrl}/__test/abort-probe?probeId={probeId}", content: null, ct);
            return (await response.Content.ReadAsStringAsync(ct)).Trim('"');
        }

        /// <summary>
        /// N-2: a client handler's token fires when its own connection ends — previously it was
        /// bound to <c>CancellationToken.None</c> and the handler kept running into the void.
        /// </summary>
        [Fact]
        public async Task A_client_handlers_token_fires_when_the_connection_closes() {
            var ct = TestContext.Current.CancellationToken;
            var connection = CreateConnection();
            var clientMethods = new TestClientMethodsImpl();
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(clientMethods);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-wait-nocancel?connectionId={connection.ConnectionId}&seconds=30";
                var response = await http.PostAsync(url, content: null, ct);
                Assert.True(response.IsSuccessStatusCode);

                // The handler is running and nobody will ever cancel it from the server side.
                await TestHelper.WaitFor(
                    () => clientMethods.LastWaitSeconds == 30,
                    "the client to observe the Wait call");
                Assert.False(clientMethods.WaitObservedCancellation);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }

            // Closing the connection is the only cancellation signal left — it must arrive. Fifteen
            // seconds, not five: on the develop runner this test shares the machine with the
            // Docker-backed backplane tests of two other target frameworks, and it timed out there
            // once while passing in the same job for the other frameworks. A slow arrival is still
            // an arrival; a missing one is what this test is about.
            await TestHelper.WaitFor(
                () => clientMethods.WaitObservedCancellation,
                "the handler's token to fire on connection close",
                attempts: 300);
        }

        /// <summary>
        /// N-3, invoke path (the control): the method's token observes the caller's disconnect —
        /// the answer would reach nobody.
        /// </summary>
        [Fact]
        public async Task An_invoked_methods_token_fires_when_the_caller_disconnects() {
            var ct = TestContext.Current.CancellationToken;
            var probeId = Guid.NewGuid().ToString("N");
            var connection = CreateConnection();
            await connection.StartAsync(ct);
            using var http = new HttpClient();

            var invokeTask = connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.InvokeAbortProbe", new object[] { probeId, 30 }), ct);
            await TestHelper.WaitFor(
                () => ProbeState(http, _fixture.ServerUrl, probeId, ct).GetAwaiter().GetResult() == "running",
                "the invoke probe to start");

            await connection.StopAsync(ct);
            await connection.DisposeAsync();
            _ = invokeTask.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);

            await TestHelper.WaitFor(
                () => ProbeState(http, _fixture.ServerUrl, probeId, ct).GetAwaiter().GetResult() == "cancelled",
                "the invoke probe's token to fire on disconnect");
        }

        /// <summary>
        /// N-3, send path: the caller asked for the *work* — the token must NOT fire just because
        /// the connection dropped afterwards. Previously both paths bound ConnectionAborted.
        /// </summary>
        [Fact]
        public async Task A_sent_methods_token_survives_the_callers_disconnect() {
            var ct = TestContext.Current.CancellationToken;
            var probeId = Guid.NewGuid().ToString("N");
            var connection = CreateConnection();
            await connection.StartAsync(ct);
            using var http = new HttpClient();

            await connection.SendCoreAsync(
                new ClientRequestMessage("ExtraMethods.SendAbortProbe", new object[] { probeId, 30 }), ct);
            await TestHelper.WaitFor(
                () => ProbeState(http, _fixture.ServerUrl, probeId, ct).GetAwaiter().GetResult() == "running",
                "the send probe to start");

            await connection.StopAsync(ct);
            await connection.DisposeAsync();

            // The work keeps running: the state must still be "running" well after the disconnect
            // (a ConnectionAborted binding flips it to "cancelled" within milliseconds).
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            Assert.Equal("running", await ProbeState(http, _fixture.ServerUrl, probeId, ct));
        }
    }
}
