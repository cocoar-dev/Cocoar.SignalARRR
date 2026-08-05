using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// Trace propagation over the real wire (O-1).
    /// </summary>
    /// <remarks>
    /// Before this, no message carried a <c>traceparent</c>: a server-to-client RPC started a
    /// fresh, unconnected trace — on the library's core promise, bidirectional RPC. Both
    /// directions are asserted end to end: the trace id observed on the far side must equal the
    /// one the near side started with. The server process registers an
    /// <see cref="ActivityListener"/> of its own (see IntegrationTestServer's Program).
    /// </remarks>
    [Collection("Simple")]
    public class TelemetryPropagationTests : IAsyncLifetime, IDisposable {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly ActivityListener _listener;
        private readonly ConcurrentBag<Activity> _stopped = new();
        private HARRRConnection _connection = null!;

        public TelemetryPropagationTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;

            // Without a listener the client-side StartActivity returns null and nothing is stamped
            // onto the outgoing message — the instrumentation is deliberately opt-in.
            _listener = new ActivityListener {
                ShouldListenTo = source => source.Name == SignalARRRTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => _stopped.Add(activity),
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public async ValueTask InitializeAsync() {
            _connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            await _connection.StartAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }

        public void Dispose() {
            _listener.Dispose();
        }

        [Fact]
        public async Task A_client_to_server_call_carries_the_clients_trace() {
            var ct = TestContext.Current.CancellationToken;

            var serverTraceId = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.GetTraceId"), ct);

            Assert.False(string.IsNullOrEmpty(serverTraceId), "the server observed no trace at all");

            // The outgoing client span must be the same trace the server observed.
            var outgoing = Assert.Single(_stopped, a =>
                a.Kind == ActivityKind.Client && a.OperationName == "ExtraMethods.GetTraceId");
            Assert.Equal(outgoing.TraceId.ToString(), serverTraceId);
        }

        [Fact]
        public async Task A_server_to_client_call_carries_the_servers_trace() {
            var ct = TestContext.Current.CancellationToken;
            _connection.RegisterInterface<ITelemetryProbeMethods, TelemetryProbeImpl>(new TelemetryProbeImpl());
            await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, _connection, ct);

            using var http = new HttpClient();
            var url = $"{_fixture.ServerUrl}/__test/trace-probe?connectionId={_connection.ConnectionId}";
            var response = await http.PostAsync(url, content: null, ct);
            var result = (await response.Content.ReadAsStringAsync(ct)).Trim('"');
            Assert.True(response.IsSuccessStatusCode, $"Server returned {response.StatusCode}: {result}");

            var parts = result.Split('|');
            Assert.Equal(2, parts.Length);
            Assert.False(string.IsNullOrEmpty(parts[0]), "the server started no span");
            Assert.False(string.IsNullOrEmpty(parts[1]), "the client observed no trace while handling the call");
            Assert.Equal(parts[0], parts[1]);
        }

        private sealed class TelemetryProbeImpl : ITelemetryProbeMethods {
            public string TraceProbe() => Activity.Current?.TraceId.ToString() ?? "";
        }
    }
}
