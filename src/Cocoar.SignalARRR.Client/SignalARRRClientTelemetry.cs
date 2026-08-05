using System;
using System.Diagnostics;
using Cocoar.SignalARRR.Common;

namespace Cocoar.SignalARRR.Client {
    /// <summary>
    /// Client-side instrumentation points. No-ops unless the consumer registered a listener for
    /// <see cref="SignalARRRTelemetry.ActivitySourceName"/>.
    /// </summary>
    internal static class SignalARRRClientTelemetry {

        /// <summary>
        /// Starts the client-kind span for an outgoing invocation and stamps the trace context onto
        /// the message — after the span starts, so the server's span becomes its child.
        /// </summary>
        public static Activity? StartOutgoingCall(ClientRequestMessage message) {
            var activity = SignalARRRTelemetry.ActivitySource.StartActivity(message.Method, ActivityKind.Client);

            if (activity != null) {
                activity.SetTag("rpc.system", "signalarrr");
                activity.SetTag("rpc.method", message.Method);
            }

            message.WithTraceContext();
            return activity;
        }

        /// <summary>
        /// Starts the server-kind span for an incoming server-to-client call, joined to the
        /// server's trace via the message's <c>TraceParent</c>.
        /// </summary>
        public static Activity? StartIncomingCall(ServerRequestMessage message) {
            var activity = SignalARRRTelemetry.ActivitySource.StartActivity(
                string.IsNullOrEmpty(message.Method) ? "signalarrr.server_request" : message.Method,
                ActivityKind.Server,
                SignalARRRTelemetry.ParseTraceContext(message.TraceParent, message.TraceState));

            if (activity != null) {
                activity.SetTag("rpc.system", "signalarrr");
                activity.SetTag("rpc.method", message.Method);
            }

            return activity;
        }

        public static void RecordFailure(Activity? activity, Exception exception) {
            // A cancelled call is the caller's choice, not a failure.
            if (activity != null && exception is not OperationCanceledException) {
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                activity.SetTag("error.type", exception.GetType().FullName);
            }
        }
    }
}
