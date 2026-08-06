using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;

namespace Cocoar.SignalARRR.Server {
    internal enum SignalARRRBackplaneEnvelopeKind {
        Dispatch,
        InvokeRequest,
        InvokeResponse,
        InvokeQueryRequest,
        InvokeQueryResult,
        InvokeQueryCompleted,
        GroupCommand
    }

    internal enum SignalARRRBackplaneTargetKind {
        Connections,
        All,
        Group,
        User
    }

    internal enum SignalARRRBackplaneGroupAction {
        Add,
        Remove
    }

    internal sealed class SignalARRRBackplaneEnvelope {
        public string OriginNodeId { get; set; } = string.Empty;
        public string? TargetNodeId { get; set; }
        public SignalARRRBackplaneEnvelopeKind Kind { get; set; }
        public string HubType { get; set; } = string.Empty;
        public SignalARRRBackplaneTargetKind TargetKind { get; set; } = SignalARRRBackplaneTargetKind.Connections;
        public string[] ConnectionIds { get; set; } = Array.Empty<string>();
        public string? GroupName { get; set; }
        public string? UserId { get; set; }
        public ServerRequestMessage? Message { get; set; }
        public Guid? RequestId { get; set; }
        public string? ResultType { get; set; }
        public string? ResultJson { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Serialized <see cref="Cocoar.SignalARRR.Common.HARRRError"/> of a failed remote invoke.
        /// Additive: older nodes send only <see cref="ErrorMessage"/>, which then rehydrates
        /// without a structured envelope. (<see cref="ErrorMessage"/> doubles as the
        /// single-result flag on request envelopes and stays untouched for that.)
        /// </summary>
        public string? ErrorJson { get; set; }
        public SignalARRRBackplaneGroupAction? GroupAction { get; set; }
    }

    internal sealed class SignalARRRConnectionRegistration {
        public string ConnectionId { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string HubType { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string[] Groups { get; set; } = Array.Empty<string>();
        public SignalARRRConnectionAttribute[] Attributes { get; set; } = Array.Empty<SignalARRRConnectionAttribute>();
    }

    internal sealed class SignalARRRConnectionAttribute {
        public string Key { get; set; } = string.Empty;
        public string[] Values { get; set; } = Array.Empty<string>();
    }

    internal sealed class SignalARRRConnectionAttributeFilter {
        public string Key { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

    internal sealed class SignalARRRBackplaneInvokeResult {
        public string ConnectionId { get; set; } = string.Empty;
        public object? Value { get; set; }
    }

    internal interface ISignalARRRConnectionRegistry {
        bool IsEnabled { get; }

        Task RegisterConnectionAsync(ClientContext clientContext, CancellationToken cancellationToken = default);

        Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken = default);

        Task<SignalARRRConnectionRegistration?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SignalARRRConnectionRegistration>> FindConnectionsAsync(
            Type hubType,
            string? groupName = null,
            string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null,
            CancellationToken cancellationToken = default);

        Task AddConnectionToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default);

        Task RemoveConnectionFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default);
    }

    internal interface ISignalARRRBackplane {
        bool IsEnabled { get; }
        string NodeId { get; }

        Task PublishDispatchAsync(
            Type? hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            IReadOnlyList<string>? connectionIds = null,
            string? groupName = null,
            string? userId = null,
            CancellationToken cancellationToken = default);

        Task<object?> InvokeConnectionAsync(
            Type? hubType,
            string connectionId,
            ServerRequestMessage message,
            Type resultType,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SignalARRRBackplaneInvokeResult>> InvokeQueryAsync(
            Type hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            Type resultType,
            string? groupName = null,
            string? userId = null,
            bool singleResult = false,
            CancellationToken cancellationToken = default);

        Task PublishGroupCommandAsync(
            Type? hubType,
            string connectionId,
            string groupName,
            SignalARRRBackplaneGroupAction action,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The nodes currently alive in the cluster, this one included. Empty when the backplane
        /// is disabled. Previously there was no way to ask (O-8).
        /// </summary>
        Task<IReadOnlyList<string>> GetActiveNodesAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Health surface of a backplane implementation, consumed by the SignalARRR health check.
    /// </summary>
    internal interface ISignalARRRBackplaneHealth {
        TimeSpan HeartbeatInterval { get; }

        /// <summary>When this node last wrote its heartbeat successfully; null before the first.</summary>
        DateTime? LastSuccessfulHeartbeatUtc { get; }

        /// <summary>The heartbeat loop died — nothing keeps this node registered anymore.</summary>
        bool HeartbeatLoopFaulted { get; }

        /// <summary>Round-trip to the backing store; null when unreachable.</summary>
        Task<TimeSpan?> PingAsync(CancellationToken cancellationToken = default);
    }

    internal sealed class DisabledSignalARRRBackplane : ISignalARRRBackplane {
        public bool IsEnabled => false;

        public string NodeId { get; } = Environment.MachineName;

        public Task PublishDispatchAsync(
            Type? hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            IReadOnlyList<string>? connectionIds = null,
            string? groupName = null,
            string? userId = null,
            CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task<object?> InvokeConnectionAsync(
            Type? hubType,
            string connectionId,
            ServerRequestMessage message,
            Type resultType,
            CancellationToken cancellationToken = default) {
            throw new InvalidOperationException("SignalARRR backplane is not configured.");
        }

        public Task<IReadOnlyList<SignalARRRBackplaneInvokeResult>> InvokeQueryAsync(
            Type hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            Type resultType,
            string? groupName = null,
            string? userId = null,
            bool singleResult = false,
            CancellationToken cancellationToken = default) {
            throw new InvalidOperationException("SignalARRR backplane is not configured.");
        }

        public Task PublishGroupCommandAsync(
            Type? hubType,
            string connectionId,
            string groupName,
            SignalARRRBackplaneGroupAction action,
            CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> GetActiveNodesAsync(CancellationToken cancellationToken = default) {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }

    internal sealed class DisabledSignalARRRConnectionRegistry : ISignalARRRConnectionRegistry {
        public bool IsEnabled => false;

        public Task RegisterConnectionAsync(ClientContext clientContext, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task<SignalARRRConnectionRegistration?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) {
            return Task.FromResult<SignalARRRConnectionRegistration?>(null);
        }

        public Task<IReadOnlyList<SignalARRRConnectionRegistration>> FindConnectionsAsync(
            Type hubType,
            string? groupName = null,
            string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null,
            CancellationToken cancellationToken = default) {
            return Task.FromResult<IReadOnlyList<SignalARRRConnectionRegistration>>(Array.Empty<SignalARRRConnectionRegistration>());
        }

        public Task AddConnectionToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }

        public Task RemoveConnectionFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }
    }
}
