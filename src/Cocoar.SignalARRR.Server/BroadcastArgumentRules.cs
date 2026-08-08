using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Microsoft.Extensions.Logging;

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
        /// Converts each <see cref="CancellationToken"/> argument into a
        /// <see cref="CancellationTokenReference"/> with an id of its own, wired to deliver the
        /// cancellation to the same recipients the call went to (N-4, variant C).
        /// </summary>
        /// <remarks>
        /// Mirrors what <see cref="MethodArgumentPreparer"/> does for single-client calls: each
        /// token keeps its own id, so two token parameters stay independently cancellable. Until
        /// the dispatcher and the backplane envelope could name a SignalR method, these arguments
        /// were rejected outright — delivery was impossible, and dropping the argument would have
        /// shifted every following one on clients that count positions.
        /// <para>
        /// The delivery callback is best-effort: it typically fires when recipients are already
        /// disconnecting, so failures are logged, never thrown. The token registration is
        /// deliberately not disposed, consistent with the cancellation contract (DI-6).
        /// </para>
        /// </remarks>
        public static object[] PrepareCancellationTokens(object[] arguments, Func<Guid, Task> deliverCancellation, ILogger? logger) {
            if (!arguments.Any(a => a is CancellationToken)) {
                return arguments;
            }

            var converted = new object[arguments.Length];
            for (var i = 0; i < arguments.Length; i++) {
                if (arguments[i] is CancellationToken token) {
                    var reference = new CancellationTokenReference();
                    // Not an async lambda: Register takes an Action, so one would compile to
                    // async void and take the process down when it throws.
                    token.Register(() => _ = DeliverSafeAsync(deliverCancellation, reference.Id, logger));
                    converted[i] = reference;
                } else {
                    converted[i] = arguments[i];
                }
            }

            return converted;
        }

        /// <summary>
        /// The wire message a broadcast cancellation travels as — the same shape the single-client
        /// path sends, so clients cannot tell the difference.
        /// </summary>
        public static ServerRequestMessage CancellationMessage(Guid tokenId) =>
            new ServerRequestMessage(MethodNames.CancelTokenFromServer) { CancellationGuid = tokenId };

        private static async Task DeliverSafeAsync(Func<Guid, Task> deliverCancellation, Guid tokenId, ILogger? logger) {
            try {
                await deliverCancellation(tokenId).ConfigureAwait(false);
            } catch (Exception ex) {
                logger?.LogDebug(ex, "Could not deliver broadcast cancellation of token {TokenId}.", tokenId);
            }
        }
    }
}
