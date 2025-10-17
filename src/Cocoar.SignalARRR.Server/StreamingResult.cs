using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using Cocoar.SignalARRR.Common.Exceptions;

namespace Cocoar.SignalARRR.Server {

    internal abstract class StreamingResult : IAsyncEnumerable<object> {


        public abstract IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default);
    }

    internal class StreamingResult<T> : StreamingResult {
        private readonly MethodInfo _methodInfo;

        private IAsyncEnumerable<T> _enumerable;

        public ClientContext ClientContext { get; }

        private StreamingResult(ClientContext clientContext, MethodInfo methodInfo) {
            _methodInfo = methodInfo;
            ClientContext = clientContext;
        }

        public StreamingResult(IAsyncEnumerable<T> enumerable, ClientContext clientContext, MethodInfo methodInfo) : this(clientContext, methodInfo) {
            _enumerable = enumerable;
        }

        public StreamingResult(ChannelReader<T> channelReader, ClientContext clientContext, MethodInfo methodInfo) : this(clientContext, methodInfo) {
            _enumerable = channelReader.ReadAllAsync();
        }


        public override async IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
            var enumerator = _enumerable.GetAsyncEnumerator();
            try {
                while (await enumerator.MoveNextAsync()) {
                    var authResult = await ClientContext.TryAuthenticate(_methodInfo);
                    if (!authResult.Succeeded) {
                        throw new UnauthorizedException();
                    }
                    yield return enumerator.Current;
                }
            } finally { await enumerator.DisposeAsync(); }
                                  
        }
    }
}
