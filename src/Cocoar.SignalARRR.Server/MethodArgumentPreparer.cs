using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.SignalARRR.Server {
    internal class MethodArgumentPreparer {


        private readonly ClientContext _clientContext;
        private readonly ServerPushStreamManager _pushStreamManager;
        private readonly ILogger _logger;

        public MethodArgumentPreparer(ClientContext clientContext) {
            _clientContext = clientContext;
            _pushStreamManager = clientContext.ServiceProvider.GetRequiredService<ServerPushStreamManager>();
            _logger = clientContext.ServiceProvider.GetService<ILogger<MethodArgumentPreparer>>() ?? (ILogger)NullLogger.Instance;
        }

        /// <summary>
        /// Converts arguments into their wire form for one outgoing call.
        /// </summary>
        /// <remarks>
        /// Each <see cref="CancellationToken"/> argument gets a reference with an id of its own, and
        /// a cancellation callback bound to that very token. Two token parameters therefore stay
        /// independently cancellable — which is the only reason to declare two of them.
        /// <para>
        /// This is deliberately <em>not</em> collapsed onto the message's <c>CancellationGuid</c>.
        /// That id means something different: the call as a whole, which is what a caller passing a
        /// token to a method that has no token parameter needs. Merging them would have made every
        /// token parameter of a call cancel together.
        /// </para>
        /// </remarks>
        /// <param name="registrations">
        /// Collects the cancellation callbacks registered here, so the caller can unhook them when
        /// the call is over rather than leaving them on the caller's token (DI-6).
        /// </param>
        internal IEnumerable<object> PrepareArguments(IEnumerable<object> arguments, CancellationRegistrations registrations) {
            foreach (var argument in arguments) {

                switch (argument) {
                    case null: {
                            yield return null!;
                            continue;
                        }
                    case Stream stream: {
                            yield return PrepareStream(stream);
                            continue;
                        }
                    case CancellationToken cancellationToken: {
                            yield return PrepareCancellationToken(cancellationToken, registrations);
                            continue;
                        }
                    default:
                        yield return argument;
                        break;
                }
            }
        }

        private StreamReference PrepareStream(Stream stream) {
            var identifier = _pushStreamManager.StoreStreamForDownload(stream, _clientContext.ConnectedTo);
            return new StreamReference() { Uri = identifier };
        }

        private CancellationTokenReference PrepareCancellationToken(CancellationToken cancellationToken, CancellationRegistrations registrations) {
            var tokenReference = new CancellationTokenReference();

            // CancellationToken.Register takes an Action, so an `async` lambda here compiles to
            // async void: an exception escaping it is raised on the thread pool with nobody to
            // observe it, which terminates the process. That is not a corner case -- this callback
            // fires precisely when the caller cancels, which is typically because the client is
            // already gone, so CancelToken faulting is the expected path, not the exceptional one.
            registrations.Add(cancellationToken.Register(() => _ = CancelTokenSafeAsync(tokenReference.Id)));

            return tokenReference;
        }

        private async Task CancelTokenSafeAsync(Guid tokenId) {
            try {
                await _clientContext.CancelToken(tokenId).ConfigureAwait(false);
            } catch (Exception ex) {
                // Best effort: notifying a client that is no longer reachable is not an error, and
                // there is no caller left to propagate to. Never let this escape (see above).
                _logger.LogDebug(ex, "Could not notify client {ConnectionId} about cancellation of token {TokenId}.",
                    _clientContext.Id, tokenId);
            }
        }
    }
}
