using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    public class MethodArgumentPreperator {


        private readonly ClientContext _clientContext;
        private readonly ServerPushStreamManager _pushStreamManager;

        public MethodArgumentPreperator(ClientContext clientContext) {
            _clientContext = clientContext;
            _pushStreamManager = clientContext.ServiceProvider.GetRequiredService<ServerPushStreamManager>();
        }

        internal IEnumerable<object> PrepareArguments(IEnumerable<object> arguments) {
            foreach (var argument in arguments) {

                switch (argument) {
                    case null: {
                            yield return null;
                            continue;
                        }
                    case Stream stream: {
                        yield return PrepareStream(stream);
                            continue;
                        }
                    case CancellationToken cancellationToken: {
                        // TODO: Restore server-controlled client cancellation feature
                        // This allows the server to cancel long-running operations on the client
                        // Use case: Video conversion farm - cancel remote worker when user cancels in UI
                        // 
                        // Implementation needed:
                        // 1. Create CancellationTokenReference class in Common.RemoteReferenceTypes
                        // 2. Implement server-to-client cancellation message propagation
                        // 3. Client should create local CancellationTokenSource from the reference
                        // 
                        // Original implementation (commented out for now):
                        // yield return PrepareCancellationToken(cancellationToken);
                        
                        yield return null;
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

        // TODO: Restore this method when implementing server-controlled client cancellation
        // See TODO comments in PrepareArguments method above
        //private CancellationTokenReference PrepareCancellationToken(CancellationToken cancellationToken) {
        //    var tokenReference = new CancellationTokenReference();
        //    cancellationToken.Register(async () => await _clientContext.CancelToken(tokenReference));
        //    return tokenReference;
        //}
    }
}
