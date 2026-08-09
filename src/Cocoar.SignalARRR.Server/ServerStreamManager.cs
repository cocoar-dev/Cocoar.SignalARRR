using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.SignalARRR.Common.Serialization;

namespace Cocoar.SignalARRR.Server {
    public class ServerStreamManager : IDisposable {

        /// <summary>
        /// Default number of items buffered per client-to-server stream before the producer is made
        /// to wait.
        /// </summary>
        public const int DefaultBufferSize = 1024;

        private readonly ConcurrentDictionary<Guid, PendingStream> _pendingStreams = new();
        private readonly TimeSpan _idleTimeout;
        private readonly Timer _reaperTimer;

        /// <summary>
        /// Streams currently tracked. A stream that grows old here without ever being read is a
        /// leak — previously invisible, because this count did not exist (O-8).
        /// </summary>
        public int ActiveStreamCount => _pendingStreams.Count;

        public ServerStreamManager() : this(TimeSpan.FromMinutes(10)) {
        }

        public ServerStreamManager(TimeSpan idleTimeout) {
            _idleTimeout = idleTimeout;
            // The callback must not throw: an unhandled exception on a timer thread takes the
            // process down.
            _reaperTimer = new Timer(_ => {
                try {
                    SweepIdleStreams();
                } catch {
                    // Never let this escape onto the timer thread.
                }
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Removes streams that were created but never read. Everything with a reader attached has
        /// a lifecycle of its own (the read loop's <c>finally</c>, or
        /// <see cref="CompleteStreamsFor"/> on disconnect) — the leak case is exactly a stream
        /// nobody ever started consuming.
        /// </summary>
        internal int SweepIdleStreams() {
            var reaped = 0;

            foreach (var entry in _pendingStreams) {
                if (entry.Value.ReaderAttached || Stopwatch.GetElapsedTime(entry.Value.CreatedAt) < _idleTimeout) {
                    continue;
                }

                if (_pendingStreams.TryRemove(entry.Key, out var pending)) {
                    pending.Channel.Writer.TryComplete(new IOException(
                        $"The stream was never consumed and was reaped after {_idleTimeout}."));
                    SignalARRRServerTelemetry.ActiveStreams.Add(-1);
                    SignalARRRServerTelemetry.StreamsReaped.Add(1);
                    reaped++;
                }
            }

            return reaped;
        }

        public void Dispose() {
            _reaperTimer.Dispose();
        }

        /// <summary>
        /// Creates a stream owned by <paramref name="ownerConnectionId"/>.
        /// </summary>
        /// <remarks>
        /// The owner is recorded so that items and completion can only come from the connection the
        /// stream was created for. Without it the stream id was the only credential: any connected
        /// client that learned another's id — from a log, a shared proxy, or a replay — could inject
        /// forged items into that stream or abort it with an error.
        /// <para>
        /// The channel is bounded. It used to be unbounded and written with TryWrite, which never
        /// fails, so a client could push items faster than the server consumed them and grow the heap
        /// without limit.
        /// </para>
        /// </remarks>
        public Channel<object> CreateStream(Guid streamId, string ownerConnectionId, int bufferSize = DefaultBufferSize) {
            var channel = Channel.CreateBounded<object>(new BoundedChannelOptions(bufferSize) {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true
            });

            if (_pendingStreams.TryAdd(streamId, new PendingStream(channel, ownerConnectionId))) {
                SignalARRRServerTelemetry.ActiveStreams.Add(1);
            }

            return channel;
        }

        /// <summary>
        /// Writes an item into a stream, if <paramref name="fromConnectionId"/> owns it.
        /// </summary>
        /// <returns><c>true</c> if the item was accepted.</returns>
        /// <remarks>
        /// Awaiting the write is what applies backpressure: on a full buffer the calling hub
        /// invocation waits, which throttles that one connection rather than letting it allocate
        /// without bound.
        /// </remarks>
        public async Task<bool> WriteItemAsync(Guid streamId, object item, string fromConnectionId, CancellationToken cancellationToken = default) {
            if (!TryGetOwned(streamId, fromConnectionId, out var pending)) {
                return false;
            }

            try {
                await pending!.Channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                return true;
            } catch (ChannelClosedException) {
                // The consumer completed or faulted the stream while this item was in flight.
                return false;
            }
        }

        /// <summary>
        /// Completes a stream, if <paramref name="fromConnectionId"/> owns it.
        /// </summary>
        /// <returns><c>true</c> if the completion was accepted.</returns>
        /// <remarks>
        /// Completing the writer is all this does — the entry stays until the reader is finished
        /// with it. Removing it here was a race: <see cref="ReadStream{TResult}"/> is an async
        /// iterator, so its lookup does not run until the first MoveNextAsync. A short, fast stream
        /// delivers all its items and its completion before the server begins enumerating, the lookup
        /// then found nothing, and the caller got an empty stream with no error at all.
        /// </remarks>
        public bool CompleteStream(Guid streamId, string fromConnectionId, string? error = null) {
            if (!TryGetOwned(streamId, fromConnectionId, out var pending)) {
                return false;
            }

            if (!string.IsNullOrEmpty(error)) {
                pending!.Channel.Writer.TryComplete(new Exception($"Client streaming error: {error}"));
            } else {
                pending!.Channel.Writer.TryComplete();
            }

            return true;
        }

        /// <summary>
        /// Completes every stream owned by a connection. Called when that connection goes away.
        /// </summary>
        internal void CompleteStreamsFor(string connectionId, string reason) {
            foreach (var entry in _pendingStreams) {
                if (!string.Equals(entry.Value.OwnerConnectionId, connectionId, StringComparison.Ordinal)) {
                    continue;
                }

                if (_pendingStreams.TryRemove(entry.Key, out var pending)) {
                    pending.Channel.Writer.TryComplete(new IOException(reason));
                    SignalARRRServerTelemetry.ActiveStreams.Add(-1);
                }
            }
        }

        public async IAsyncEnumerable<TResult> ReadStream<TResult>(Guid streamId, [EnumeratorCancellation] CancellationToken cancellationToken = default, IProtocolSerializer? serializer = null) {
            if (!_pendingStreams.TryGetValue(streamId, out var pending)) {
                yield break;
            }

            // From here on the read loop's finally owns the entry's removal; the idle reaper must
            // not touch a stream that is actually being consumed.
            pending.ReaderAttached = true;

            try {
                await foreach (var item in pending.Channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                    if (item is TResult typed) {
                        yield return typed;
                    } else if (serializer != null) {
                        yield return (TResult)serializer.ConvertTo(item, typeof(TResult))!;
                    } else {
                        yield return item.Reflect().To<TResult>()!;
                    }
                }
            } finally {
                if (_pendingStreams.TryRemove(streamId, out _)) {
                    SignalARRRServerTelemetry.ActiveStreams.Add(-1);
                }
            }
        }

        private bool TryGetOwned(Guid streamId, string fromConnectionId, out PendingStream? pending) {
            if (_pendingStreams.TryGetValue(streamId, out var found)
                && string.Equals(found.OwnerConnectionId, fromConnectionId, StringComparison.Ordinal)) {
                pending = found;
                return true;
            }

            pending = null;
            return false;
        }

        private sealed class PendingStream {
            public PendingStream(Channel<object> channel, string ownerConnectionId) {
                Channel = channel;
                OwnerConnectionId = ownerConnectionId;
            }

            public Channel<object> Channel { get; }
            public string OwnerConnectionId { get; }

            /// <summary>
            /// A monotonic timestamp, not a wall-clock time.
            /// </summary>
            /// <remarks>
            /// Age used to be <c>DateTime.UtcNow</c> minus <c>DateTime.UtcNow</c>, which is neither
            /// monotonic nor guaranteed to advance between two adjacent statements. A clock
            /// correction backwards kept leaked streams alive and one forwards reaped live ones —
            /// and with a zero idle timeout, "old enough" came down to whether the clock happened
            /// to tick in between, which is how this reached CI as an intermittent failure.
            /// </remarks>
            public long CreatedAt { get; } = Stopwatch.GetTimestamp();
            public bool ReaderAttached { get; set; }
        }
    }
}
