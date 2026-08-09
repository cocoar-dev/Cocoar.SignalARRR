using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Common {
    public class ClientRequestMessage {
        [JsonPropertyName("Method")]
        public string Method { get; set; } = string.Empty;

        [JsonPropertyName("Authorization")]
        public string Authorization { get; set; } = string.Empty;

        [JsonPropertyName("Arguments")]
        public object[] Arguments { get; set; } = Array.Empty<object>();

        [JsonPropertyName("GenericArguments")]
        public string[] GenericArguments { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Identifies this invocation across both sides' logs. Without it, two concurrent calls to
        /// the same method on the same connection are indistinguishable, and a server log line
        /// cannot be matched to the client line that caused it. Optional and additive: clients
        /// without one (older SDKs, raw TypeScript/Swift callers) simply leave it unset.
        /// </summary>
        [JsonPropertyName("InvocationId")]
        public Guid? InvocationId { get; set; }

        /// <summary>
        /// W3C trace context of the sender, so the server-side span joins the caller's trace.
        /// Optional and additive: older SDKs and clients without tracing simply leave it unset.
        /// </summary>
        [JsonPropertyName("TraceParent")]
        public string? TraceParent { get; set; }

        [JsonPropertyName("TraceState")]
        public string? TraceState { get; set; }

        public ClientRequestMessage() { }

        public ClientRequestMessage(string methodName) {
            Method = methodName;
        }

        public ClientRequestMessage(string methodName, IEnumerable<object> arguments) : this(methodName) {
            Arguments = arguments.ToArray();
        }

        public ClientRequestMessage(string methodName, params object[] arguments) : this(methodName, arguments.ToList()) {

        }

        public ClientRequestMessage WithAuthorization(string authorization) {
            Authorization = authorization;
            return this;
        }

        /// <summary>
        /// Resolves the token provider without blocking the calling thread.
        /// </summary>
        public async Task<ClientRequestMessage> WithAuthorizationAsync(Func<Task<string>> authorization) {
            Authorization = authorization != null ? await authorization().ConfigureAwait(false) ?? string.Empty : string.Empty;
            return this;
        }

        /// <summary>
        /// Assigns an invocation id if the caller has not set one already.
        /// </summary>
        public ClientRequestMessage WithInvocationId() {
            InvocationId ??= Guid.NewGuid();
            return this;
        }

        /// <summary>
        /// Stamps the ambient <see cref="System.Diagnostics.Activity"/> onto the message so the
        /// receiving side can continue the trace. A no-op without a current W3C activity.
        /// </summary>
        public ClientRequestMessage WithTraceContext() {
            var activity = System.Diagnostics.Activity.Current;
            if (activity != null && activity.IdFormat == System.Diagnostics.ActivityIdFormat.W3C) {
                TraceParent = activity.Id;
                TraceState = activity.TraceStateString;
            }

            return this;
        }

    }
}
