using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cocoar.SignalARRR.Common;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Server-side instrumentation points. Everything here is a no-op unless the consumer
    /// registered a listener for <see cref="SignalARRRTelemetry.ActivitySourceName"/> /
    /// <see cref="SignalARRRTelemetry.MeterName"/>.
    /// </summary>
    internal static class SignalARRRServerTelemetry {

        public static readonly Histogram<double> InvocationDuration = SignalARRRTelemetry.Meter.CreateHistogram<double>(
            "signalarrr.server.invocation.duration", unit: "ms",
            description: "Duration of a client-to-server invocation, from dispatch to completion.");

        public static readonly UpDownCounter<long> ActiveConnections = SignalARRRTelemetry.Meter.CreateUpDownCounter<long>(
            "signalarrr.server.active_connections",
            description: "Connections currently registered with a hub.");

        public static readonly Histogram<double> ConnectionSetupDuration = SignalARRRTelemetry.Meter.CreateHistogram<double>(
            "signalarrr.server.connection.setup.duration", unit: "ms",
            description: "Duration of the connection setup phases (register-local, register-registry, base, total).");

        public static readonly Histogram<double> ConnectionTeardownDuration = SignalARRRTelemetry.Meter.CreateHistogram<double>(
            "signalarrr.server.connection.teardown.duration", unit: "ms",
            description: "Duration of the connection teardown phases (unregister-local, unregister-registry, base, total).");

        public static readonly UpDownCounter<long> ActiveStreams = SignalARRRTelemetry.Meter.CreateUpDownCounter<long>(
            "signalarrr.server.active_streams",
            description: "Client-to-server streams currently tracked. Grows without shrinking when streams leak.");

        public static readonly Counter<long> StreamsReaped = SignalARRRTelemetry.Meter.CreateCounter<long>(
            "signalarrr.server.streams.reaped",
            description: "Streams that were created but never consumed and got reaped. Near zero in a healthy application.");

        public static readonly Counter<long> UploadSlotsSwept = SignalARRRTelemetry.Meter.CreateCounter<long>(
            "signalarrr.server.upload_slots.swept",
            description: "Upload slots that expired unused. A sustained rate points at clients requesting slots and never uploading.");

        public static readonly Counter<long> BackplaneHeartbeatFailures = SignalARRRTelemetry.Meter.CreateCounter<long>(
            "signalarrr.backplane.heartbeat.failures",
            description: "Failed heartbeat iterations. A sustained non-zero rate is the signal that used to be invisible: the node keeps serving while the cluster is about to declare it dead.");

        public static readonly Counter<long> BackplaneNodesSwept = SignalARRRTelemetry.Meter.CreateCounter<long>(
            "signalarrr.backplane.nodes.swept",
            description: "Remote nodes whose registrations this node cleaned up as dead. Near zero in a healthy cluster.");

        public static readonly Counter<long> BackplaneSelfEvictions = SignalARRRTelemetry.Meter.CreateCounter<long>(
            "signalarrr.backplane.self_evictions",
            description: "Times this node found its own heartbeat gone and re-registered its live connections — another node had already wiped them.");

        public static readonly Counter<long> BackplaneListenerReconnects = SignalARRRTelemetry.Meter.CreateCounter<long>(
            "signalarrr.backplane.listener.reconnects",
            description: "Times this node's subscription to the backplane dropped and was re-established. Each one is a window in which cluster messages were missed, unless catch-up replayed them.");

        public static readonly Counter<long> BackplaneMessagesReplayed = SignalARRRTelemetry.Meter.CreateCounter<long>(
            "signalarrr.backplane.messages.replayed",
            description: "Messages read back from the store after a subscription drop, because the node's cursor was behind. Postgres backplane with catch-up only.");

        public static readonly Counter<long> BackplaneCatchUpGaps = SignalARRRTelemetry.Meter.CreateCounter<long>(
            "signalarrr.backplane.catch_up.gaps",
            description: "Subscription drops that outlasted the message retention, so part of what was missed could no longer be replayed. Any non-zero value is a real loss.");

        public static void RecordConnectionPhase(Histogram<double> histogram, string hub, string phase, double elapsedMs) {
            histogram.Record(elapsedMs,
                new KeyValuePair<string, object?>("signalarrr.hub", hub),
                new KeyValuePair<string, object?>("signalarrr.phase", phase));
        }

        /// <summary>
        /// Starts the client-kind span for an outgoing server-to-client call and stamps the trace
        /// context onto the message — after the span starts, so the receiver's span becomes its
        /// child rather than a sibling.
        /// </summary>
        public static Activity? StartClientCall(string connectionId, ServerRequestMessage message) {
            var activity = SignalARRRTelemetry.ActivitySource.StartActivity(
                string.IsNullOrEmpty(message.Method) ? "signalarrr.client_call" : message.Method,
                ActivityKind.Client);

            if (activity != null) {
                activity.SetTag("rpc.system", "signalarrr");
                activity.SetTag("rpc.method", message.Method);
                activity.SetTag("signalarrr.connection_id", connectionId);
            }

            message.WithTraceContext();
            return activity;
        }

        /// <summary>
        /// Starts the server-side span and duration measurement for one incoming invocation.
        /// </summary>
        /// <remarks>
        /// The span joins the caller's trace via the message's <c>TraceParent</c> — without it,
        /// every RPC on the wire started a fresh, unconnected trace. Cancellation is an expected
        /// outcome for RPC (the caller chose to stop), so it is recorded as
        /// <c>signalarrr.outcome=cancelled</c> with span status Ok, not as an error.
        /// </remarks>
        public static ServerInvocationScope StartInvocation(string hub, ClientRequestMessage message, string connectionId) {
            var activity = SignalARRRTelemetry.ActivitySource.StartActivity(
                $"{hub}/{message.Method}",
                ActivityKind.Server,
                SignalARRRTelemetry.ParseTraceContext(message.TraceParent, message.TraceState));

            if (activity != null) {
                activity.SetTag("rpc.system", "signalarrr");
                activity.SetTag("rpc.service", hub);
                activity.SetTag("rpc.method", message.Method);
                activity.SetTag("signalarrr.connection_id", connectionId);
                if (message.InvocationId is { } invocationId) {
                    activity.SetTag("signalarrr.invocation_id", invocationId);
                }
            }

            return new ServerInvocationScope(activity, hub, message.Method, Stopwatch.GetTimestamp());
        }
    }

    internal sealed class ServerInvocationScope : IDisposable {
        private readonly Activity? _activity;
        private readonly string _hub;
        private readonly string _method;
        private readonly long _startTimestamp;
        private string _outcome = "ok";

        internal ServerInvocationScope(Activity? activity, string hub, string method, long startTimestamp) {
            _activity = activity;
            _hub = hub;
            _method = method;
            _startTimestamp = startTimestamp;
        }

        public void RecordFailure(Exception exception) {
            if (exception is OperationCanceledException) {
                _outcome = "cancelled";
                _activity?.SetStatus(ActivityStatusCode.Ok);
                return;
            }

            _outcome = "error";
            if (_activity != null) {
                _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                _activity.SetTag("error.type", exception.GetType().FullName);
            }
        }

        public void Dispose() {
            var elapsedMs = (Stopwatch.GetTimestamp() - _startTimestamp) * 1000.0 / Stopwatch.Frequency;
            SignalARRRServerTelemetry.InvocationDuration.Record(elapsedMs,
                new KeyValuePair<string, object?>("signalarrr.hub", _hub),
                new KeyValuePair<string, object?>("signalarrr.method", _method),
                new KeyValuePair<string, object?>("signalarrr.outcome", _outcome));

            _activity?.Dispose();
        }
    }
}
