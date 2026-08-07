using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Mvc;
using TestShared;

namespace IntegrationTestServer {

    /// <summary>
    /// Second ServerMethods class on the same hub — tests multi-class organization,
    /// [MessageName] attribute, complex types, and various parameter/return type combinations.
    /// </summary>
    public class ExtraMethods : ServerMethods<TestHub> {

        // Basic multi-class tests
        public string Greet(string name) => $"Hello, {name}!";

        public Task<int> Add(int a, int b) => Task.FromResult(a + b);

        [MessageName("CustomEcho")]
        public string EchoWithCustomName(string input) => input;

        // Complex object round-trip
        public ComplexTestClass EchoComplex(ComplexTestClass input) => input;

        // DateTime serialization
        public string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd");

        // Guid parameter
        public string GuidToString(Guid id) => id.ToString();

        // List return
        public List<string> GenerateItems(int count) {
            var items = new List<string>();
            for (int i = 0; i < count; i++) items.Add($"item-{i}");
            return items;
        }

        // Dictionary return
        public Dictionary<string, int> WordLengths(string sentence) {
            var result = new Dictionary<string, int>();
            foreach (var word in sentence.Split(' ')) {
                result[word] = word.Length;
            }
            return result;
        }

        // Multiple parameters of different types
        public string Combine(string text, int number, bool flag) =>
            $"{text}-{number}-{flag}";

        // [FromServices] injection — IServiceProvider is injected by DI, not by the client
        public string GetServiceInfo([FromServices] IServiceProvider sp) =>
            sp != null ? "ServiceProviderInjected" : "null";

        // Receives a Stream argument (uploaded via HTTP by the client)
        public string ReadStreamContent(System.IO.Stream data) {
            using var reader = new System.IO.StreamReader(data);
            return reader.ReadToEnd();
        }

        // Telemetry propagation probe: what the server observes as its current trace id while
        // handling this client-to-server call. Non-empty only when the server-side span joined
        // the caller's trace (or a listener started one).
        public string GetTraceId() => System.Diagnostics.Activity.Current?.TraceId.ToString() ?? "";

        // Overloads resolved by argument count (F-6): same name, told apart by how many
        // arguments the message carries.
        public string PickOverload(int id) => "one";
        public string PickOverload(int id, int count) => "two";

        // Trailing default: callable with one argument (the binder fills the default) or two.
        public string PageInfo(int index, int size = 25) => $"{index}:{size}";

        // Throws a specific exception for error handling tests
        public string ThrowArgumentException(string paramName) =>
            throw new ArgumentException("Invalid value provided", paramName);

        public string ThrowInvalidOperation() =>
            throw new InvalidOperationException("This operation is not allowed");

        // Application-defined error code — must reach the client verbatim (O-7).
        public string ThrowRoomFull() =>
            throw new Cocoar.SignalARRR.Server.HARRRException("room_full", "The room is full.");

        // N-3 probes: what happens to a method's CancellationToken when the caller's connection
        // dies. The invoke path binds ConnectionAborted (token fires), the send path binds
        // ApplicationStopping (the work continues). State is polled via /__test/abort-probe.
        public async Task<string> InvokeAbortProbe(string probeId, int seconds, CancellationToken cancellationToken) {
            await RunAbortProbe(probeId, seconds, cancellationToken);
            return "completed";
        }

        public Task SendAbortProbe(string probeId, int seconds, CancellationToken cancellationToken) =>
            RunAbortProbe(probeId, seconds, cancellationToken);

        private static async Task RunAbortProbe(string probeId, int seconds, CancellationToken cancellationToken) {
            AbortProbes.State[probeId] = "running";
            try {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                AbortProbes.State[probeId] = "completed";
            } catch (OperationCanceledException) {
                AbortProbes.State[probeId] = "cancelled";
                throw;
            }
        }
    }

    public static class AbortProbes {
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> State = new();
    }

    /// <summary>
    /// N-4 probes: cancellation sources behind broadcast calls that carry a token, cancelled on
    /// request by a second endpoint so the test controls the timing instead of a fixed delay.
    /// </summary>
    public static class BroadcastCancelProbes {
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.CancellationTokenSource> Sources = new();
    }
}
