using System;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The one implementation of <see cref="IClusterSubject{T}"/>: a synchronized Rx subject for
    /// the local side, and an outbox with a single relay loop for the other nodes.
    /// </summary>
    /// <remarks>
    /// The relay is one loop per subject so events leave this node in the order they were raised;
    /// the backplane preserves that order per publisher, and the receiving node hands envelopes
    /// on sequentially. Received events go straight into the local subject through
    /// <see cref="IClusterSubjectSink.Accept"/> and never touch the outbox, which is what makes
    /// "once locally, once remotely, never echoed" a property of the structure rather than of a
    /// flag on the wire.
    /// </remarks>
    internal sealed class ClusterSubject<T> : IClusterSubject<T>, IClusterSubjectSink, IDisposable {
        private readonly ISubject<T> _subject = Subject.Synchronize(new Subject<T>());
        private readonly ISignalARRRBackplane _backplane;
        private readonly ClusterSubjectRegistry _registry;
        private readonly JsonSerializerOptions _serializerOptions;
        private readonly ILogger _logger;
        private readonly Channel<Outgoing> _outbox = Channel.CreateUnbounded<Outgoing>(new UnboundedChannelOptions { SingleReader = true });
        private readonly Task _relay;

        public ClusterSubject(
            string name,
            ClusterSubjectOptions options,
            ISignalARRRBackplane backplane,
            ClusterSubjectRegistry registry,
            ILogger<ClusterSubject<T>> logger) {
            Name = name;
            _backplane = backplane;
            _registry = registry;
            _serializerOptions = options.SerializerOptions ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
            _logger = logger;

            _registry.Register(this);
            _relay = _backplane.IsEnabled ? Task.Run(RunRelayAsync) : Task.CompletedTask;
        }

        public string Name { get; }

        public Type EventType => typeof(T);

        public IDisposable Subscribe(IObserver<T> observer) => _subject.Subscribe(observer);

        public void OnNext(T value) {
            _subject.OnNext(value);

            if (_backplane.IsEnabled) {
                _outbox.Writer.TryWrite(new Outgoing(value, null));
            }
        }

        public async Task PublishAsync(T value, CancellationToken cancellationToken = default) {
            _subject.OnNext(value);

            if (!_backplane.IsEnabled) {
                return;
            }

            // Through the outbox, not past it: an awaited publish must not overtake the events
            // queued by OnNext before it, or the other nodes would see them out of order.
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_outbox.Writer.TryWrite(new Outgoing(value, completion))) {
                throw new ObjectDisposedException(nameof(ClusterSubject<T>));
            }

            await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        void IClusterSubjectSink.Accept(string payloadJson) {
            T? value;
            try {
                value = JsonSerializer.Deserialize<T>(payloadJson, _serializerOptions);
            } catch (JsonException ex) {
                // A peer on another build may send a shape this one cannot read. Drop it and say
                // so; the alternative — materializing whatever the wire names — is what the fixed
                // event type exists to rule out.
                _logger.LogWarning(ex, "Cluster subject '{Subject}' received an event it cannot deserialize as {EventType}; it is dropped.", Name, typeof(T));
                return;
            }

            if (value == null) {
                _logger.LogWarning("Cluster subject '{Subject}' received a null event; it is dropped.", Name);
                return;
            }

            try {
                _subject.OnNext(value);
            } catch (Exception ex) {
                // A subscriber that throws must not take the backplane's consumer down with it.
                _logger.LogError(ex, "A subscriber of cluster subject '{Subject}' threw while handling a remote event.", Name);
            }
        }

        private async Task RunRelayAsync() {
            await foreach (var outgoing in _outbox.Reader.ReadAllAsync().ConfigureAwait(false)) {
                try {
                    var payloadJson = JsonSerializer.Serialize(outgoing.Value, _serializerOptions);
                    await _backplane.PublishClusterEventAsync(Name, payloadJson).ConfigureAwait(false);
                    outgoing.Completion?.TrySetResult(true);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Cluster subject '{Subject}' could not relay an event to the other nodes; local subscribers have it, remote ones do not.", Name);
                    outgoing.Completion?.TrySetException(ex);
                }
            }
        }

        public void Dispose() {
            _outbox.Writer.TryComplete();
            _registry.Unregister(this);
            _subject.OnCompleted();
        }

        private readonly struct Outgoing {
            public T Value { get; }
            public TaskCompletionSource<bool>? Completion { get; }

            public Outgoing(T value, TaskCompletionSource<bool>? completion) {
                Value = value;
                Completion = completion;
            }
        }
    }
}
