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
        /// <para>
        /// Inherited contract members are included too. <c>GetMethods</c> behaves differently on
        /// interfaces than on classes: it returns only what the interface declares, never what it
        /// inherits. The source generator walks <c>AllInterfaces</c>, so the proxy for
        /// <c>IDerived : IBase</c> does implement <c>IBase</c>'s methods and tags them
        /// <c>Ns.IDerived|BaseMethod</c> — a name nothing registered, so every call to an inherited
        /// member ended in "Method 'BaseMethod' not found!", with no hint that inheritance was why.
        /// </para>
        /// <para>
        /// Two base interfaces declaring the same name still collide silently: the lookup is keyed
        /// on the name alone, which is the overload collision (F-6) and is addressed separately.
        /// </para>
        /// </remarks>
        public ClientInterfaceMethodsCache(Delegate factory, Type interfaceType, Type? implementationType) {

            Factory = factory;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var methodInfo in interfaceType.GetMethods(flags)) {
                var target = Resolve(interfaceType, implementationType, methodInfo);
                Methods.AddOrUpdate(methodInfo.Name, target, (s, info) => target);
            }

            // Inherited members are added only where the registered interface does not already
            // declare that name, so registering IDerived cannot change what one of its own members
            // means. Deliberately TryAdd rather than AddOrUpdate.
            foreach (var baseInterface in interfaceType.GetInterfaces()) {
                foreach (var methodInfo in baseInterface.GetMethods(flags)) {
                    Methods.TryAdd(methodInfo.Name, Resolve(baseInterface, implementationType, methodInfo));
                }
            }
        }

        private static MethodInfo Resolve(Type declaringInterface, Type? implementationType, MethodInfo methodInfo)
            => ResolveImplementation(declaringInterface, implementationType, methodInfo) ?? methodInfo;

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
