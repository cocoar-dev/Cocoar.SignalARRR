using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using Cocoar.SignalARRR.Common.Exceptions;

namespace Cocoar.SignalARRR.Server {

    internal abstract class StreamingResult : IAsyncEnumerable<object> {


        public abstract IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Adapts a server method's stream for SignalR, re-checking authorization as it goes.
    /// </summary>
    /// <remarks>
    /// SignalR hands its stream token to <see cref="GetAsyncEnumerator"/> and cancels it when the
    /// client aborts the stream or disappears. That token used to be dropped: the enumerator was
    /// obtained without it, and a <see cref="ChannelReader{T}"/> source was turned into an
    /// enumerable in the constructor — before any token existed at all. So the server loop stayed
    /// parked in <c>MoveNextAsync</c> and the producer behind it kept running, with nobody left to
    /// receive anything. Iterators annotated with <c>[EnumeratorCancellation]</c> were silently not
    /// cancellable.
    /// </remarks>
    internal class StreamingResult<T> : StreamingResult {
        private readonly ExtensionMethods.SignalARRRAuthorizationPlan _authorization;

        private readonly IAsyncEnumerable<T>? _enumerable;
        private readonly ChannelReader<T>? _channelReader;

        public ClientContext ClientContext { get; }

        private StreamingResult(ClientContext clientContext, ExtensionMethods.SignalARRRAuthorizationPlan authorization) {
            _authorization = authorization;
            ClientContext = clientContext;
        }

        public StreamingResult(IAsyncEnumerable<T> enumerable, ClientContext clientContext, ExtensionMethods.SignalARRRAuthorizationPlan authorization) : this(clientContext, authorization) {
            _enumerable = enumerable;
        }

        /// <summary>
        /// Kept as a reader rather than converted here: <c>ReadAllAsync</c> takes the token, and at
        /// construction time there is none to give it.
        /// </summary>
        public StreamingResult(ChannelReader<T> channelReader, ClientContext clientContext, ExtensionMethods.SignalARRRAuthorizationPlan authorization) : this(clientContext, authorization) {
            _channelReader = channelReader;
        }


        public override async IAsyncEnumerator<object> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
            var source = _enumerable ?? _channelReader!.ReadAllAsync(cancellationToken);
            var enumerator = source.GetAsyncEnumerator(cancellationToken);
            try {
                while (await enumerator.MoveNextAsync()) {
                    var authResult = await ClientContext.TryAuthenticate(_authorization);
                    if (!authResult.Succeeded) {
                        throw new UnauthorizedException();
                    }
                    yield return enumerator.Current!;
                }
            } finally { await enumerator.DisposeAsync(); }

        }
    }
}
