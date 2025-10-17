using System;
using System.Collections.Concurrent;
using System.Reflection;
using Cocoar.SignalARRR.Common.Interfaces;

namespace Cocoar.SignalARRR.Common {
    public class SignalARRRMethodsCollection: ISignalARRRMethodsCollection {

        private readonly ConcurrentDictionary<string, (Delegate Factory, MethodInfo MethodInfo)> _collection = new ConcurrentDictionary<string, (Delegate Factory, MethodInfo MethodInfo)>();

        public void AddMethod(string name, MethodInfo methodInfo) {

            object Factory(IServiceProvider sp) {
                var fromServiceProvider = sp.GetService(methodInfo.DeclaringType);
                if (fromServiceProvider != null) {
                    return fromServiceProvider;
                }

                return Activator.CreateInstance(methodInfo.DeclaringType);
            }

            AddMethod(name, methodInfo, Factory);
        }

        public void AddMethod(string name, MethodInfo methodInfo, object instance) {
            AddMethod(name, methodInfo, (sp) => instance);
        }

        public void AddMethod<T>(string name, MethodInfo methodInfo, Func<IServiceProvider, T> factory = null) {
            _collection.AddOrUpdate(name, (factory, methodInfo), (s, tuple) => (factory, methodInfo));
        }

        public (Delegate Factory, MethodInfo MethodInfo) GetMethodInformations(string name) {
            if (_collection.TryGetValue(name, out var methodsCache)) {
                return methodsCache;
            }

            throw new Exception($"Method '{name}' not found!");
        }
    }
}
