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
    public class MethodArgumentPreperator {


        private readonly ClientContext _clientContext;
        private readonly ServerPushStreamManager _pushStreamManager;
        private readonly ILogger _logger;

        public MethodArgumentPreperator(ClientContext clientContext) {
            _clientContext = clientContext;
            _pushStreamManager = clientContext.ServiceProvider.GetRequiredService<ServerPushStreamManager>();
            _logger = clientContext.ServiceProvider.GetService<ILogger<MethodArgumentPreperator>>() ?? (ILogger)NullLogger.Instance;
        }

        internal IEnumerable<object> PrepareArguments(IEnumerable<object> arguments) {
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
                            yield return PrepareCancellationToken(cancellationToken);
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

        private CancellationTokenReference PrepareCancellationToken(CancellationToken cancellationToken) {
            var tokenReference = new CancellationTokenReference();

            // CancellationToken.Register takes an Action, so an `async` lambda here compiles to
            // async void: an exception escaping it is raised on the thread pool with nobody to
            // observe it, which terminates the process. That is not a corner case -- this callback
            // fires precisely when the caller cancels, which is typically because the client is
            // already gone, so CancelToken faulting is the expected path, not the exceptional one.
            cancellationToken.Register(() => _ = CancelTokenSafeAsync(tokenReference.Id));

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
