using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using Cocoar.Reflectensions.ExtensionMethods;

namespace Cocoar.SignalARRR.Server {
    public class ServerStreamManager {

        private readonly ConcurrentDictionary<Guid, Channel<object>> _pendingStreams = new();

        public Channel<object> CreateStream(Guid streamId) {
            var channel = Channel.CreateUnbounded<object>();
            _pendingStreams.TryAdd(streamId, channel);
            return channel;
        }

        public void WriteItem(Guid streamId, object item) {
            if (_pendingStreams.TryGetValue(streamId, out var channel)) {
                channel.Writer.TryWrite(item);
            }
        }

        public void CompleteStream(Guid streamId, string error = null) {
            if (_pendingStreams.TryRemove(streamId, out var channel)) {
                if (!string.IsNullOrEmpty(error)) {
                    channel.Writer.TryComplete(new Exception($"Client streaming error: {error}"));
                } else {
                    channel.Writer.TryComplete();
                }
            }
        }

        public async IAsyncEnumerable<TResult> ReadStream<TResult>(Guid streamId, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            if (!_pendingStreams.TryGetValue(streamId, out var channel)) {
                yield break;
            }

            try {
                await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken)) {
                    if (item is TResult typed) {
                        yield return typed;
                    } else {
                        yield return item.Reflect().To<TResult>();
                    }
                }
            } finally {
                _pendingStreams.TryRemove(streamId, out _);
            }
        }
    }
}
