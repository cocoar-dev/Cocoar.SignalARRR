using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Cocoar.SignalARRR.Common {
    public class ClientInterfaceMethodsCache {

        private ConcurrentDictionary<string, MethodInfo> Methods = new ConcurrentDictionary<string, MethodInfo>();
        internal Delegate Factory { get; }

        public ClientInterfaceMethodsCache(Delegate factory, Type interfaceType)
            : this(factory, interfaceType, implementationType: null) {
        }

        /// <summary>
        /// Builds the method lookup for one registered interface.
        /// </summary>
        /// <param name="implementationType">
        /// The implementing type, when known. Supplying it makes the cache resolve each interface
        /// method to the method that actually runs.
        /// </param>
        /// <remarks>
        /// The distinction matters for authorization. Dispatch resolves a method through the
        /// interface, but virtual dispatch then executes the implementation — so storing the
        /// interface declaration meant <c>[Authorize]</c> on the implementing class was never seen,
        /// and every interface-routed call was evaluated as unrestricted unless the attribute
        /// happened to sit on the contract. Storing the implementation lets attributes on both
        /// sides be honoured.
        /// </remarks>
        public ClientInterfaceMethodsCache(Delegate factory, Type interfaceType, Type? implementationType) {

            Factory = factory;

            var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var methodInfo in methods) {
                var target = ResolveImplementation(interfaceType, implementationType, methodInfo) ?? methodInfo;
                Methods.AddOrUpdate(methodInfo.Name, target, (s, info) => target);
            }
        }

        private static MethodInfo? ResolveImplementation(Type interfaceType, Type? implementationType, MethodInfo interfaceMethod) {

            if (implementationType == null || implementationType.IsInterface || !interfaceType.IsAssignableFrom(implementationType)) {
                return null;
            }

            try {
                var mapping = implementationType.GetInterfaceMap(interfaceType);

                for (var i = 0; i < mapping.InterfaceMethods.Length; i++) {
                    if (mapping.InterfaceMethods[i] == interfaceMethod) {
                        return mapping.TargetMethods[i];
                    }
                }
            } catch (ArgumentException) {
                // Generic type definitions and a few exotic cases cannot be mapped; fall back to the
                // interface declaration, which is what happened for every registration before.
            }

            return null;
        }

        internal (Delegate Factory, MethodInfo MethodInfo) GetInvokeInformations(string methodName) {
            var method = Methods.TryGetValue(methodName, out var methodInfo) ? methodInfo : throw new Exception($"Method '{methodName}' not found!");
            return (Factory, method);
        }

    }
}
