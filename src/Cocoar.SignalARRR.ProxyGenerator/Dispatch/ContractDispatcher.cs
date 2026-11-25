using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.ProxyGenerator {
    /// <summary>
    /// Receives calls the server makes on a contract the client implements. The source generator
    /// emits one per <c>[SignalARRRContract]</c> interface: a <c>switch</c> on the method name that
    /// reads each argument with its static type and calls the implementation directly, so the
    /// receiving side needs neither reflection nor runtime code generation.
    /// </summary>
    public abstract class ContractDispatcher {

        /// <summary>The contract interface this dispatcher calls.</summary>
        public abstract Type ContractType { get; }

        /// <summary>The name the interface travels under: <c>[MessageName]</c>, else its full name.</summary>
        public abstract string WireName { get; }

        /// <summary>
        /// The types of every argument and result of the contract, as they cross the wire. A client
        /// whose JSON options cannot serialize one of them fails at start, naming all of them,
        /// instead of at the first call that needs one.
        /// </summary>
        public abstract IReadOnlyList<Type> WireTypes { get; }

        /// <summary>
        /// Why the contract cannot be received on a client, or <c>null</c>. Generic methods are the
        /// case: their type argument arrives as a name, which only reflection can turn into a call.
        /// Registering such a contract fails with this text.
        /// </summary>
        public virtual string? UnsupportedReason => null;

        /// <summary>Calls <paramref name="method"/> on <paramref name="target"/>, which implements <see cref="ContractType"/>.</summary>
        /// <returns>
        /// The method's result: <c>null</c> for <c>void</c> and <c>Task</c>, the value otherwise, and
        /// an <see cref="IStreamingResult"/> for <c>IAsyncEnumerable</c>, <c>ChannelReader</c> and
        /// <c>IObservable</c> results.
        /// </returns>
        public abstract Task<object?> InvokeUntypedAsync(object target, string method, ArgumentReader args);

        protected static Exception MethodNotFound(string method)
            => new DispatchException(DispatchException.MethodNotFound, $"Method '{method}' not found!");

        protected static Exception ArgumentCountMismatch(string method, int count, string accepted)
            => new DispatchException(DispatchException.InvalidArgumentCount,
                $"Method '{method}' cannot be called with {count} argument(s). Registered argument count(s): {accepted}.");
    }

    /// <inheritdoc />
    public abstract class ContractDispatcher<TContract> : ContractDispatcher where TContract : class {

        public sealed override Type ContractType => typeof(TContract);

        /// <summary>Calls <paramref name="method"/> on <paramref name="target"/>.</summary>
        public abstract Task<object?> InvokeAsync(TContract target, string method, ArgumentReader args);

        public sealed override Task<object?> InvokeUntypedAsync(object target, string method, ArgumentReader args)
            => InvokeAsync((TContract)target, method, args);
    }
}
