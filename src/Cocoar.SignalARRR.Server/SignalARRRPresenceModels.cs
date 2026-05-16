using System.Collections.Generic;

namespace Cocoar.SignalARRR.Server {
    public sealed class SignalARRRConnectionSnapshot {
        public string ConnectionId { get; init; } = string.Empty;
        public string NodeId { get; init; } = string.Empty;
        public string? UserId { get; init; }
        public IReadOnlyList<string> Groups { get; init; } = new List<string>();
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes { get; init; }
            = new Dictionary<string, IReadOnlyList<string>>();
    }

    public sealed class SignalARRRUserPresenceSnapshot {
        public string UserId { get; init; } = string.Empty;
        public IReadOnlyList<string> ConnectionIds { get; init; } = new List<string>();
        public IReadOnlyList<string> NodeIds { get; init; } = new List<string>();
    }
}
