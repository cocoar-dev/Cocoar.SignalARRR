using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Client.FullFramework {
    /// <summary>
    /// Base class for proxy dispatch helpers. Handles invoke/send/stream for typed proxy methods.
    /// </summary>
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
                try {
                    await foreach (var item in asyncEnumerable) {
                        if (!writer.TryWrite(item)) {
                            await writer.WriteAsync(item, token);
                        }
                    }
                } finally {
                    writer.TryComplete();
                }
            });
            return output.Reader;
        }
    }
}
