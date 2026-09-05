using System.Collections.Generic;
using Cocoar.SignalARRR.Server;

namespace IntegrationTestServer {
    /// <summary>The event the multi-node tests push through a cluster subject.</summary>
    public sealed record ClusterTestEvent(string Value, string Payload);

    /// <summary>
    /// Subscribes to the test cluster subject for the lifetime of the process and keeps what it
    /// saw, so a test can ask a node "which events reached you, and in what order?".
    /// </summary>
    public sealed class ClusterSubjectProbe : IDisposable {
        private readonly List<ClusterTestEvent> _events = new List<ClusterTestEvent>();
        private readonly object _sync = new object();
        private readonly IDisposable _subscription;

        public ClusterSubjectProbe(IClusterSubject<ClusterTestEvent> subject) {
            _subscription = subject.Subscribe(e => {
                lock (_sync) {
                    _events.Add(e);
                }
            });
        }

        public IReadOnlyList<ClusterTestEvent> Snapshot() {
            lock (_sync) {
                return _events.ToArray();
            }
        }

        public void Dispose() => _subscription.Dispose();
    }
}
