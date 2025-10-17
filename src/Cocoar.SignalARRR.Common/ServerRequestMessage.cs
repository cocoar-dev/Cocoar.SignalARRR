using System;
using System.Collections.Generic;
using System.Linq;

namespace Cocoar.SignalARRR.Common {
    public class ServerRequestMessage {

        public Guid Id { get; set; }
        public string Method { get; set; }
        public object[] Arguments { get; set; }
        public string[] GenericArguments { get; set; }
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
