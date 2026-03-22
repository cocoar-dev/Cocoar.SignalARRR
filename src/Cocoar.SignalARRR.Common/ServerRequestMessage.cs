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
