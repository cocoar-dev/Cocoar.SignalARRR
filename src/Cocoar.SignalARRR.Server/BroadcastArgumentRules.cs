using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// What a call may carry when it goes to more than one client at a time.
    /// </summary>
    /// <remarks>
    /// Shared so every broadcast path answers the same way. There is more than one: a query built
    /// with <c>WithHub&lt;T&gt;()</c> captures the call and hands it to the dispatcher, while a plain
    /// sequence of contexts goes through <see cref="BroadcastProxyCreatorHelper"/>. Which one runs is
    /// an internal detail, so they must not differ in what they accept.
    /// </remarks>
    internal static class BroadcastArgumentRules {

        /// <summary>
        /// Rejects cancellation tokens, because a broadcast cannot deliver the cancellation.
        /// </summary>
        /// <remarks>
        /// Cancelling means telling the recipients, and telling them means sending
        /// <c>CancelTokenFromServer</c> to the same set the call went to. The dispatcher cannot say
        /// that: it sends everything as <c>InvokeServerMessage</c>, and the backplane envelope has no
        /// field for a different one. Delivering a broadcast cancellation therefore needs a protocol
        /// change, which is a decision of its own rather than a detail of this one.
        /// <para>
        /// Rejected rather than quietly dropped. Dropping the argument shifts every following one,
        /// because a client with no parameter types to consult counts positions — and that is exactly
        /// the silent misbinding this whole change is about. A loud, explainable failure is the
        /// honest interim.
        /// </para>
        /// </remarks>
        public static void RejectCancellationTokens(string methodName, IEnumerable<object> arguments) {
            if (!arguments.Any(a => a is CancellationToken)) {
                return;
            }

            throw new NotSupportedException(
                $"Method '{methodName}' has a CancellationToken argument. Cancellation is not supported for " +
                "broadcast/multi-client operations: the cancellation notification cannot be routed to the " +
                "recipients. Call the clients individually via GetTypedMethods<T>(connectionId) if you need to " +
                "cancel them.");
        }
    }
}
