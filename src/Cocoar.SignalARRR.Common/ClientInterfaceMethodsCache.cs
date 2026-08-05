using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Cocoar.SignalARRR.Common {
    public class ClientInterfaceMethodsCache {

        // Method name → argument count → target. Built once in the constructor and read-only
        // afterwards, so a plain dictionary is safe.
        private readonly Dictionary<string, Dictionary<int, MethodInfo>> _methods =
            new Dictionary<string, Dictionary<int, MethodInfo>>(StringComparer.Ordinal);

        internal Delegate Factory { get; }

        internal Type InterfaceType { get; }

        public ClientInterfaceMethodsCache(Delegate factory, Type interfaceType)
            : this(factory, interfaceType, implementationType: null) {
        }

        public ClientInterfaceMethodsCache(Delegate factory, Type interfaceType, Type? implementationType)
            : this(factory, interfaceType, implementationType, WireSlotPolicy.AllParameters) {
        }

        /// <summary>
        /// Builds the method lookup for one registered interface.
        /// </summary>
        /// <param name="implementationType">
        /// The implementing type, when known. Supplying it makes the cache resolve each interface
        /// method to the method that actually runs.
        /// </param>
        /// <param name="slotPolicy">
        /// The receiving side's slot rules; methods are indexed under every argument count they
        /// accept, so overloads resolve by the argument count of the incoming message.
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
        /// A name the registered interface declares itself hides every inherited member of that
        /// name entirely, so registering <c>IDerived</c> cannot change what one of its own members
        /// means. Beyond that, two methods reachable under the same name and argument count are
        /// indistinguishable on the wire — that is a hard error here (at registration) instead of
        /// an unspecified one of them silently winning with possibly different <c>[Authorize]</c>
        /// data (F-6).
        /// </para>
        /// </remarks>
        public ClientInterfaceMethodsCache(Delegate factory, Type interfaceType, Type? implementationType, WireSlotPolicy slotPolicy) {

            Factory = factory;
            InterfaceType = interfaceType;

            if (slotPolicy == null) {
                throw new ArgumentNullException(nameof(slotPolicy));
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            var declaredMethods = interfaceType.GetMethods(flags);
            foreach (var methodInfo in declaredMethods) {
                Add(interfaceType, implementationType, methodInfo, slotPolicy);
            }

            var declaredNames = new HashSet<string>(declaredMethods.Select(m => m.Name), StringComparer.Ordinal);

            foreach (var baseInterface in interfaceType.GetInterfaces()) {
                foreach (var methodInfo in baseInterface.GetMethods(flags)) {
                    if (declaredNames.Contains(methodInfo.Name)) {
                        continue;
                    }

                    Add(baseInterface, implementationType, methodInfo, slotPolicy);
                }
            }
        }

        private void Add(Type declaringInterface, Type? implementationType, MethodInfo methodInfo, WireSlotPolicy slotPolicy) {
            // The argument counts are computed from the resolved target, not the interface
            // declaration: the binder fills omitted arguments from the *executed* method's default
            // values, so the index must accept exactly what the binder can actually bind.
            var target = Resolve(declaringInterface, implementationType, methodInfo);

            if (!_methods.TryGetValue(methodInfo.Name, out var byCount)) {
                byCount = new Dictionary<int, MethodInfo>();
                _methods[methodInfo.Name] = byCount;
            }

            foreach (var count in slotPolicy.GetAcceptedArgumentCounts(target)) {
                if (byCount.TryGetValue(count, out var existing)) {
                    throw new InvalidOperationException(
                        $"Cannot register interface '{InterfaceType.FullName}': " +
                        $"{SignalARRRMethodsCollection.Describe(existing)} and {SignalARRRMethodsCollection.Describe(target)} " +
                        $"would both be reachable as '{methodInfo.Name}' with {count} argument(s). " +
                        "The wire carries no parameter types, so methods sharing a name must differ in argument count — " +
                        "rename one of them, or declare the method on the registered interface itself to hide the inherited ones.");
                }

                byCount[count] = target;
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

        internal (Delegate Factory, MethodInfo MethodInfo) GetInvokeInformations(string methodName, int argumentCount) {
            if (_methods.TryGetValue(methodName, out var byCount)) {
                if (byCount.TryGetValue(argumentCount, out var methodInfo)) {
                    return (Factory, methodInfo);
                }

                throw new Exception(
                    $"Method '{methodName}' cannot be called with {argumentCount} argument(s). " +
                    $"Registered argument count(s): {string.Join(", ", byCount.Keys.OrderBy(c => c))}.");
            }

            throw new Exception($"Method '{methodName}' not found!");
        }

    }
}
