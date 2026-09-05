using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    /// <summary>What the backplane hands a received cluster event to: the subject registered under its name.</summary>
    internal interface IClusterSubjectSink {
        string Name { get; }
        Type EventType { get; }
        void Accept(string payloadJson);
    }

    /// <summary>
    /// The subjects of this node by name, so an envelope arriving from another node finds its
    /// counterpart. Names are cluster-wide identifiers: two subjects cannot share one.
    /// </summary>
    internal sealed class ClusterSubjectRegistry {
        private readonly ConcurrentDictionary<string, IClusterSubjectSink> _sinks = new ConcurrentDictionary<string, IClusterSubjectSink>(StringComparer.Ordinal);
        private readonly ILogger<ClusterSubjectRegistry> _logger;

        public ClusterSubjectRegistry(ILogger<ClusterSubjectRegistry> logger) {
            _logger = logger;
        }

        public void Register(IClusterSubjectSink sink) {
            if (!_sinks.TryAdd(sink.Name, sink)) {
                var existing = _sinks[sink.Name];
                throw new InvalidOperationException(
                    $"A cluster subject named '{sink.Name}' is already registered for {existing.EventType}. " +
                    "Subject names are cluster-wide identifiers; use one name per event type.");
            }
        }

        public void Unregister(IClusterSubjectSink sink) {
            if (_sinks.TryGetValue(sink.Name, out var current) && ReferenceEquals(current, sink)) {
                _sinks.TryRemove(sink.Name, out _);
            }
        }

        /// <summary>Delivers an event from another node; an unknown name is dropped, not an error — a rolling update can leave nodes with different subjects for a while.</summary>
        public void Dispatch(string name, string payloadJson) {
            if (_sinks.TryGetValue(name, out var sink)) {
                sink.Accept(payloadJson);
                return;
            }

            _logger.LogDebug(
                "A cluster event for subject '{Subject}' arrived, but no such subject is registered on this node; it is dropped. Registered: {Registered}.",
                name, string.Join(", ", _sinks.Keys.OrderBy(k => k, StringComparer.Ordinal)));
        }
    }
}
