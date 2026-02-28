using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Cocoar.SignalARRR.Common {
    public class ClientInterfaceMethodsCache {

        private ConcurrentDictionary<string, MethodInfo> Methods = new ConcurrentDictionary<string, MethodInfo>();
        internal Delegate Factory { get; }
        public ClientInterfaceMethodsCache(Delegate factory, Type interfaceType) {

            Factory = factory;

            var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var methodInfo in methods) {
                Methods.AddOrUpdate(methodInfo.Name, methodInfo, (s, info) => methodInfo);
            }
        }

        internal (Delegate Factory, MethodInfo MethodInfo) GetInvokeInformations(string methodName) {
            var method = Methods.TryGetValue(methodName, out var methodInfo) ? methodInfo : throw new Exception($"Method '{methodName}' not found!");
            return (Factory, method);
        }

    }
}
