using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Common {
    public class ClientRequestMessage {
        public string Method { get; set; }
        public string Authorization { get; set; }
        public object[] Arguments { get; set; }
        public string[] GenericArguments { get; set; }

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
            Authorization = authorization?.Invoke().GetAwaiter().GetResult();
            return this;
        }

    }
}
