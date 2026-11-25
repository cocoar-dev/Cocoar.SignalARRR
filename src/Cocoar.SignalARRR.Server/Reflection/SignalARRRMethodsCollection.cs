using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Common.Interfaces;

namespace Cocoar.SignalARRR.Common {
    public class SignalARRRMethodsCollection : ISignalARRRMethodsCollection {

        private readonly WireSlotPolicy _slotPolicy;

        // Name → argument count → registration. Methods are indexed under every argument count
        // they accept (a method with trailing defaults accepts a range), so dispatch is a single
        // lookup and ambiguity cannot exist past registration.
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, (Delegate? Factory, MethodInfo MethodInfo)>> _collection =
            new ConcurrentDictionary<string, ConcurrentDictionary<int, (Delegate? Factory, MethodInfo MethodInfo)>>(StringComparer.Ordinal);

        public SignalARRRMethodsCollection() : this(WireSlotPolicy.AllParameters) {
        }

        public SignalARRRMethodsCollection(WireSlotPolicy slotPolicy) {
            _slotPolicy = slotPolicy ?? throw new ArgumentNullException(nameof(slotPolicy));
        }

        public void AddMethod(string name, MethodInfo methodInfo) {

            object? Factory(IServiceProvider sp) {
                if (methodInfo.DeclaringType == null) {
                    return null;
                }

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

        /// <summary>
        /// Registers a method under every argument count it accepts. Two different methods that
        /// would be reachable under the same name and argument count are indistinguishable on the
        /// wire, so that is a hard error here — at startup — instead of the last registration
        /// silently winning and possibly carrying different <c>[Authorize]</c> data than the one
        /// the caller believes is checked.
        /// </summary>
        public void AddMethod<T>(string name, MethodInfo methodInfo, Func<IServiceProvider, T>? factory = null) {
            var byCount = _collection.GetOrAdd(name,
                _ => new ConcurrentDictionary<int, (Delegate? Factory, MethodInfo MethodInfo)>());

            foreach (var count in _slotPolicy.GetAcceptedArgumentCounts(methodInfo)) {
                var added = byCount.AddOrUpdate(count,
                    _ => (factory, methodInfo),
                    (_, existing) => existing.MethodInfo == methodInfo ? (factory, methodInfo) : existing);

                if (added.MethodInfo != methodInfo) {
                    throw new InvalidOperationException(
                        $"Cannot register '{name}' for {count} argument(s): {Describe(added.MethodInfo)} and " +
                        $"{Describe(methodInfo)} would both be reachable under that name and argument count. " +
                        "The wire carries no parameter types, so overloads must differ in argument count — " +
                        "rename one of the methods or give it a distinct [MessageName].");
                }
            }
        }

        public (Delegate Factory, MethodInfo MethodInfo) GetMethodInformations(string name, int argumentCount) {
            if (_collection.TryGetValue(name, out var byCount)) {
                if (byCount.TryGetValue(argumentCount, out var registration)) {
                    return (registration.Factory!, registration.MethodInfo);
                }

                throw new MethodResolutionException(HARRRErrorCodes.InvalidArgumentCount,
                    $"Method '{name}' cannot be called with {argumentCount} argument(s). " +
                    $"Registered argument count(s): {string.Join(", ", byCount.Keys.OrderBy(c => c))}.");
            }

            throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound, $"Method '{name}' not found!");
        }

        internal static string Describe(MethodInfo method) =>
            $"{method.DeclaringType?.FullName}.{method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})";
    }
}
