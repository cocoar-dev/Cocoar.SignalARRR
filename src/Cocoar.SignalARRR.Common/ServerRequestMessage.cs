using System;
using System.Collections.Generic;
using System.Linq;

namespace Cocoar.SignalARRR.Common {
    public class ServerRequestMessage {

        public Guid Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public object[] Arguments { get; set; } = Array.Empty<object>();
        public string[] GenericArguments { get; set; } = Array.Empty<string>();
        public Guid? CancellationGuid { get; set; }

        public ServerRequestMessage()
        {
            Id = Guid.NewGuid();
        }

        public ServerRequestMessage(string methodName): this() {
            Method = methodName;
        }

        public ServerRequestMessage(string methodName, IEnumerable<object> arguments) : this(methodName) {
            Arguments = arguments.ToArray();
        }

        public ServerRequestMessage(string methodName, params object[] arguments) : this(methodName, arguments?.ToList() ?? new List<object>()) {

        }

    }
}
