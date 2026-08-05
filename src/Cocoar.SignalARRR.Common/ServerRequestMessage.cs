using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Cocoar.SignalARRR.Common {
    public class ServerRequestMessage {

        [JsonPropertyName("Id")]
        public Guid Id { get; set; }

        [JsonPropertyName("Method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("Arguments")]
        public object[] Arguments { get; set; } = Array.Empty<object>();

        [JsonPropertyName("GenericArguments")]
        public string[] GenericArguments { get; set; } = Array.Empty<string>();

        [JsonPropertyName("CancellationGuid")]
        public Guid? CancellationGuid { get; set; }

        [JsonPropertyName("StreamId")]
        public Guid? StreamId { get; set; }

        /// <summary>
        /// W3C trace context of the sender, so the client-side span joins the server's trace —
        /// including across backplane nodes, where the message travels inside the envelope.
        /// Optional and additive: receivers without tracing ignore it.
        /// </summary>
        [JsonPropertyName("TraceParent")]
        public string? TraceParent { get; set; }

        [JsonPropertyName("TraceState")]
        public string? TraceState { get; set; }

        /// <summary>
        /// Stamps the ambient <see cref="System.Diagnostics.Activity"/> onto the message so the
        /// receiving side can continue the trace. A no-op without a current W3C activity.
        /// </summary>
        public ServerRequestMessage WithTraceContext() {
            var activity = System.Diagnostics.Activity.Current;
            if (activity != null && activity.IdFormat == System.Diagnostics.ActivityIdFormat.W3C) {
                TraceParent = activity.Id;
                TraceState = activity.TraceStateString;
            }

            return this;
        }

        public ServerRequestMessage() {
            Id = Guid.NewGuid();
        }

        public ServerRequestMessage(string methodName) : this() {
            Method = methodName;
        }

        public ServerRequestMessage(string methodName, IEnumerable<object> arguments) : this(methodName) {
            Arguments = arguments.ToArray();
        }

        public ServerRequestMessage(string methodName, params object[] arguments) : this(methodName, arguments?.ToList() ?? new List<object>()) {

        }

    }
}
