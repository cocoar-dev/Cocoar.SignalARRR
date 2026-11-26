using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.ProxyGenerator {
    /// <summary>
    /// A streaming result of a client method, handed back by a generated dispatcher. It knows its
    /// element type statically, so the client can send the items without finding out what they are
    /// through reflection.
    /// </summary>
    public interface IStreamingResult {

        /// <summary>Sends every item through <paramref name="sendItem"/>, in order, until the stream ends.</summary>
        Task ForEachAsync(Func<object?, Task> sendItem, CancellationToken cancellationToken);
    }

    /// <summary>Factories for <see cref="IStreamingResult"/>, called by generated dispatchers.</summary>
    public static class StreamingResult {

        public static IStreamingResult From<T>(IAsyncEnumerable<T> source) => new AsyncEnumerableResult<T>(source);

        public static IStreamingResult From<T>(ChannelReader<T> source) => new AsyncEnumerableResult<T>(ReadAll(source));

        public static IStreamingResult From<T>(IObservable<T> source) => new AsyncEnumerableResult<T>(ReadAll(source));

        private static async IAsyncEnumerable<T> ReadAll<T>(ChannelReader<T> reader, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false)) {
                while (reader.TryRead(out var item)) {
                    yield return item;
                }
            }
        }

        private static async IAsyncEnumerable<T> ReadAll<T>(IObservable<T> observable, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });
            using (observable.Subscribe(new ChannelObserver<T>(channel.Writer))) {
                await foreach (var item in ReadAll(channel.Reader, cancellationToken).ConfigureAwait(false)) {
                    yield return item;
                }
            }
        }

        private sealed class ChannelObserver<T> : IObserver<T> {
            private readonly ChannelWriter<T> _writer;
            public ChannelObserver(ChannelWriter<T> writer) => _writer = writer;
            public void OnNext(T value) => _writer.TryWrite(value);
            public void OnError(Exception error) => _writer.TryComplete(error);
            public void OnCompleted() => _writer.TryComplete();
        }

        private sealed class AsyncEnumerableResult<T> : IStreamingResult {
            private readonly IAsyncEnumerable<T> _source;

            public AsyncEnumerableResult(IAsyncEnumerable<T> source) => _source = source;

            public async Task ForEachAsync(Func<object?, Task> sendItem, CancellationToken cancellationToken) {
                await foreach (var item in _source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    await sendItem(item).ConfigureAwait(false);
                }
            }
        }
    }
}
