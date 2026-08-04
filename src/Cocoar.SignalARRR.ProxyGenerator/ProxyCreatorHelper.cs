using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.ProxyGenerator {
    public abstract class ProxyCreatorHelper {


        public abstract void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default);
        public abstract Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default);

        public abstract T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default);
        public abstract Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default);

        public abstract IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default);

        /// <summary>
        /// Bridges a stream into a <see cref="ChannelReader{T}"/> for callers that want one.
        /// </summary>
        /// <remarks>
        /// The pump used to run without any error handling: if the stream faulted — a dropped
        /// connection, an exception on the server — <c>TryComplete</c> was skipped, so the channel
        /// was never completed and every consumer awaited an item that could not arrive, for the
        /// lifetime of the process. The exception went unobserved on top, so nothing said why. This
        /// is the path behind every generated proxy method returning <see cref="ChannelReader{T}"/>.
        /// <para>
        /// Completing with the exception is what lets the consumer see it: the reader rethrows it
        /// out of the await instead of hanging.
        /// </para>
        /// <para>
        /// The token now also cancels the enumeration, not just the write. Passing it to
        /// <c>WriteAsync</c> alone left the producer running after the consumer had gone away.
        /// </para>
        /// </remarks>
        public ChannelReader<T> ToChannelReader<T>(IAsyncEnumerable<T> asyncEnumerable, CancellationToken token = default) {

            var output = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleWriter = true });
            var writer = output.Writer;
            _ = Task.Run(async () => {
                try {
                    await foreach (var x1 in asyncEnumerable.WithCancellation(token).ConfigureAwait(false)) {
                        if (!writer.TryWrite(x1)) {
                            await writer.WriteAsync(x1, token).ConfigureAwait(false);
                        }
                    }

                    writer.TryComplete();
                } catch (Exception ex) {
                    // Hands the failure to whoever is reading, rather than leaving them parked.
                    writer.TryComplete(ex);
                }
            });
            return output.Reader;
        }

        public IObservable<T> ToObservable<T>(IAsyncEnumerable<T> asyncEnumerable) {
            return Observable.Create<T>(async (observer, ct) => {
                try {
                    await foreach (var item in asyncEnumerable.WithCancellation(ct)) {
                        observer.OnNext(item);
                    }
                    observer.OnCompleted();
                } catch (OperationCanceledException) {
                    // Subscription was disposed
                } catch (Exception ex) {
                    observer.OnError(ex);
                }
            });
        }
    }
}
