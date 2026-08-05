using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Cocoar.SignalARRR.Common {
    /// <summary>
    /// The library's telemetry roots. Consumers opt in by adding
    /// <c>"Cocoar.SignalARRR"</c> as a source to their OpenTelemetry (or any other
    /// <see cref="ActivityListener"/>/<see cref="MeterListener"/>-based) pipeline; without a
    /// listener every instrumentation point is a no-op.
    /// </summary>
    public static class SignalARRRTelemetry {
        public const string ActivitySourceName = "Cocoar.SignalARRR";
        public const string MeterName = "Cocoar.SignalARRR";

        private static readonly string Version =
            typeof(SignalARRRTelemetry).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(SignalARRRTelemetry).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        public static readonly ActivitySource ActivitySource = new ActivitySource(ActivitySourceName, Version);

        public static readonly Meter Meter = new Meter(MeterName, Version);

        /// <summary>
        /// Parses the trace context carried by an incoming message. Returns <c>default</c> when the
        /// sender supplied none (older SDKs, TypeScript/Swift clients) or the value is malformed —
        /// the receiving span then simply starts a new trace instead of failing the message.
        /// </summary>
        public static ActivityContext ParseTraceContext(string? traceParent, string? traceState) {
            if (string.IsNullOrWhiteSpace(traceParent)) {
                return default;
            }

            return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var context)
                ? context
                : default;
        }
    }
}
