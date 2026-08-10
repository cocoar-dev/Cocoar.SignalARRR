using System.Net.Http;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class CancellationTokenTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public CancellationTokenTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        /// <summary>
        /// A token the server passes to a client method must arrive as a working token.
        /// </summary>
        /// <remarks>
        /// This asserts on the *reason* the call ended, which it previously could not. The trigger
        /// endpoint used to answer "cancelled" for any exception at all, and the call really was
        /// throwing: the generated proxy left the token out of the arguments while the client's
        /// binder still consumed a slot for it, so binding ran off the end of the array and threw
        /// <c>IndexOutOfRangeException</c>. From the outside that was indistinguishable from a clean
        /// cancellation, so the test passed while the feature was broken.
        /// </remarks>
        [Fact]
        public async Task ServerCancelsClientCancellationToken_ClientReceivesCancellation() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            var clientMethods = new TestClientMethodsImpl();
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(clientMethods);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                var connectionId = connection.ConnectionId;

                using var http = new HttpClient();
                http.Timeout = System.TimeSpan.FromSeconds(30);
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-cancellation?connectionId={connectionId}&delayMs=200";
                var response = await http.PostAsync(url, content: null, ct);
                var result = await response.Content.ReadAsStringAsync(ct);
                Assert.True(response.IsSuccessStatusCode, $"Server returned {response.StatusCode}: {result}");

                // The argument arrived where it was sent. With the token missing from the arguments
                // and the binder still counting a slot for it, this bound the wrong value -- or ran
                // off the end of the array entirely.
                //
                // Waited for, not asserted immediately: the trigger endpoint returns once the
                // *server-side* await ended (cancelled after delayMs), which does not guarantee the
                // client has processed the invocation message yet. Under gate load — three TFMs in
                // parallel on one runner — the client can lose that race.
                await TestHelper.WaitFor(
                    () => clientMethods.LastWaitSeconds != null,
                    "the client to observe the Wait call");
                Assert.Equal(30, clientMethods.LastWaitSeconds);

                // And the token the server passed is a working one. Asserted here rather than on the
                // endpoint's answer: SignalR aborts the pending invocation itself, so the server
                // sees a HubException either way and cannot tell the two apart.
                await TestHelper.WaitFor(
                    () => clientMethods.WaitObservedCancellation,
                    "the client's cancellation token to fire");
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        /// <summary>
        /// A finished invocation must let go of the cancellation sources it created.
        /// </summary>
        /// <remarks>
        /// The client mirrored the server's DI-6 defect: the call-level source was removed from the
        /// dictionary but never disposed, and the per-parameter one — the kind
        /// <c>Wait(int, CancellationToken)</c> uses — was never even removed unless the server
        /// happened to cancel it. <c>CreateLinkedTokenSource</c> registers a callback on the parent,
        /// and the parent is the connection lifetime, so every leaked source stayed attached for as
        /// long as the connection lived. Nothing fails while that happens, which is why this asserts
        /// on a count rather than on behaviour: the integration suite never saw it because it opens
        /// and closes connections constantly, and closing is what used to clean up.
        /// </remarks>
        [Fact]
        public async Task Finished_invocations_release_their_cancellation_sources() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            var clientMethods = new TestClientMethodsImpl();
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(clientMethods);
            await connection.StartAsync(ct);
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, ct);

            try {
                var connectionId = connection.ConnectionId;
                using var http = new HttpClient();
                http.Timeout = System.TimeSpan.FromSeconds(30);
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-cancellation?connectionId={connectionId}&delayMs=100";

                for (var i = 0; i < 3; i++) {
                    clientMethods.ResetWaitObservations();

                    var response = await http.PostAsync(url, content: null, ct);
                    Assert.True(response.IsSuccessStatusCode,
                        $"Trigger {i} returned {response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");

                    await TestHelper.WaitFor(
                        () => clientMethods.WaitObservedCancellation,
                        $"the client's cancellation token to fire on call {i}");
                }

                // Waited for rather than asserted outright: the handler observes cancellation
                // slightly before its invocation unwinds, so the release happens just after the
                // flag flips.
                await TestHelper.WaitFor(
                    () => connection.TrackedCancellationSourceCount == 0,
                    "the client to release every cancellation source it created");
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }
    }
}
