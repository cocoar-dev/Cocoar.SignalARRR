using System;
using System.Collections.Concurrent;

namespace Cocoar.SignalARRR.ProxyGenerator {
    /// <summary>
    /// The generated dispatchers, keyed by contract interface. Filled by the module initializers the
    /// source generator emits, the same way <see cref="ProxyCreator"/> is filled with proxies.
    /// </summary>
    public static class DispatcherRegistry {

        private static readonly ConcurrentDictionary<Type, ContractDispatcher> Dispatchers = new ConcurrentDictionary<Type, ContractDispatcher>();

        /// <summary>
        /// Registers a dispatcher. Every assembly that sees a contract generates its own copy, and
        /// they are equivalent, so a later registration simply replaces an earlier one.
        /// </summary>
        public static void Register<TContract>(ContractDispatcher<TContract> dispatcher) where TContract : class
            => Dispatchers[typeof(TContract)] = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        /// <summary>The dispatcher for <typeparamref name="TContract"/>, or <c>null</c> when none was generated.</summary>
        public static ContractDispatcher<TContract>? Find<TContract>() where TContract : class
            => Dispatchers.TryGetValue(typeof(TContract), out var dispatcher) ? (ContractDispatcher<TContract>)dispatcher : null;
    }
}
