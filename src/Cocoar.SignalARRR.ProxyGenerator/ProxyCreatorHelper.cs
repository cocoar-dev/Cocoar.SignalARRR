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

        public ChannelReader<T> ToChannelReader<T>(IAsyncEnumerable<T> asyncEnumerable, CancellationToken token = default) {

            var output = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleWriter = true });
            var writer = output.Writer;
            Task.Run(async () => {
                await foreach (var x1 in asyncEnumerable) {
                    if (!writer.TryWrite(x1)) {
                        await writer.WriteAsync(x1, token);
                    }
                }

                writer.TryComplete();
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
