using System;
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

        /// <summary>
        /// Bridges a stream into a <see cref="ChannelReader{T}"/> for callers that want one.
        /// </summary>
        /// <remarks>
        /// This copy always completed the channel, so unlike the .NET one it never hung — but it
        /// completed it <em>successfully</em> whatever happened, so a stream that faulted looked to
        /// the consumer exactly like one that had ended normally. Silent truncation instead of a
        /// silent hang. Completing with the exception makes the reader rethrow it.
        /// <para>
        /// The token now also cancels the enumeration, not just the write.
        /// </para>
        /// </remarks>
        public ChannelReader<T> ToChannelReader<T>(IAsyncEnumerable<T> asyncEnumerable, CancellationToken token = default) {
            var output = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleWriter = true });
            var writer = output.Writer;
            _ = Task.Run(async () => {
                try {
                    await foreach (var item in asyncEnumerable.WithCancellation(token).ConfigureAwait(false)) {
                        if (!writer.TryWrite(item)) {
                            await writer.WriteAsync(item, token).ConfigureAwait(false);
                        }
                    }

                    writer.TryComplete();
                } catch (Exception ex) {
                    writer.TryComplete(ex);
                }
            });
            return output.Reader;
        }
    }
}
