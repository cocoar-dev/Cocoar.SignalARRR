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

        public ClientRequestMessage WithAuthorization(Func<Task<string>> authorization) {
            Authorization = authorization?.Invoke().GetAwaiter().GetResult() ?? string.Empty;
            return this;
        }

    }
}
