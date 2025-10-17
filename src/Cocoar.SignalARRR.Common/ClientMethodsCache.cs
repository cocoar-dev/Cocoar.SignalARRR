using System;
using System.Reflection;

namespace Cocoar.SignalARRR.Common {
    public class ClientMethodsCache {

        public ClientMethodsCache(MethodInfo methodInfo, Delegate factory) {
            MethodInfo = methodInfo;
            Factory = factory;
        }

        public MethodInfo MethodInfo { get; set; }

        public Delegate Factory { get; set; }

    }
}
