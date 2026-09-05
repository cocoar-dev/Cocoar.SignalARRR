using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Common.Helper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Everything a backplane does that does not depend on how bytes travel between nodes:
    /// building and routing envelopes, correlating invoke requests with their responses,
    /// collecting cluster query results by node identity, the heartbeat loop with self-eviction
    /// recovery, and sweeping nodes that stopped heartbeating.
    /// </summary>
    /// <remarks>
    /// A concrete backplane supplies two things. A <b>transport</b>: a fan-out channel every node
    /// listens on (<see cref="PublishCommandAsync"/>) and a way to answer one specific node
    /// (<see cref="PublishResponseAsync"/>), feeding whatever arrives into
    /// <see cref="HandleIncomingPayloadAsync"/>. And a <b>store</b>: the connection registrations
    /// and the node heartbeats. Delivery is transient by contract — a message published while a
    /// node's subscription is down is gone — and the registry is the only state that has to
    /// survive.
    /// <para>
    /// This lived inside the Redis backplane until the Postgres backplane needed the same
    /// correlation, heartbeat and sweep logic verbatim. Nothing here is public: the envelope is
    /// the inter-node wire format, and the contracts are not an extension point (AF-3).
    /// </para>
    /// </remarks>
    internal abstract class SignalARRRBackplaneBase : ISignalARRRBackplane, ISignalARRRConnectionRegistry, ISignalARRRBackplaneHealth, IHostedService, IDisposable {
        private readonly LocalSignalARRRBackplaneDispatcher _localDispatcher;
        private readonly ClusterSubjectRegistry _clusterSubjects;
        private readonly ILogger _logger;
        private readonly TimeSpan _invokeTimeout;
        private readonly TimeSpan _heartbeatInterval;
        private readonly ConcurrentDictionary<Guid, PendingInvoke> _pendingInvocations = new ConcurrentDictionary<Guid, PendingInvoke>();
        private readonly ConcurrentDictionary<Guid, PendingQueryInvoke> _pendingQueryInvocations = new ConcurrentDictionary<Guid, PendingQueryInvoke>();
        private readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly SemaphoreSlim _cleanupSemaphore = new SemaphoreSlim(1, 1);

        private CancellationTokenSource? _heartbeatCts;
        private Task? _heartbeatTask;
        private bool _started;
        private DateTime? _lastSuccessfulHeartbeatUtc;

        /// <summary>
        /// Set once the first heartbeat has been written, so that a missing heartbeat afterwards
        /// means this node was evicted rather than that it has not started yet.
        /// </summary>
        private bool _heartbeatEstablished;

        protected SignalARRRBackplaneBase(
            string nodeId,
            TimeSpan invokeTimeout,
            TimeSpan heartbeatInterval,
            TimeSpan nodeTimeout,
            LocalSignalARRRBackplaneDispatcher localDispatcher,
            ClusterSubjectRegistry clusterSubjects,
            ILogger logger) {
            NodeId = nodeId;
            NodeTimeout = nodeTimeout;
            _invokeTimeout = invokeTimeout;
            _heartbeatInterval = heartbeatInterval;
            _localDispatcher = localDispatcher;
            _clusterSubjects = clusterSubjects;
            _logger = logger;
        }

        public bool IsEnabled => true;

        public string NodeId { get; }

        /// <summary>How long a node may go without a heartbeat before the cluster declares it dead.</summary>
        protected TimeSpan NodeTimeout { get; }

        protected ILogger Logger => _logger;

        /// <summary>The options every envelope and registration is serialized with.</summary>
        protected JsonSerializerOptions SerializerOptions => _serializerOptions;

        // --- What a concrete backplane provides: transport ---

        /// <summary>Connects and subscribes; called once from <see cref="StartAsync"/> before the heartbeat loop starts.</summary>
        protected abstract Task StartTransportAsync(CancellationToken cancellationToken);

        /// <summary>Unsubscribes and disconnects; called last from <see cref="StopAsync"/>.</summary>
        protected abstract Task StopTransportAsync(CancellationToken cancellationToken);

        /// <summary>Delivers <paramref name="envelope"/> to every node, this one included; the handler filters.</summary>
        protected abstract Task PublishCommandAsync(SignalARRRBackplaneEnvelope envelope);

        /// <summary>Delivers <paramref name="envelope"/> to <paramref name="targetNodeId"/> only.</summary>
        protected abstract Task PublishResponseAsync(string targetNodeId, SignalARRRBackplaneEnvelope envelope);

        /// <summary>Round-trip to the backing store; null when unreachable.</summary>
        public abstract Task<TimeSpan?> PingAsync(CancellationToken cancellationToken = default);

        // --- What a concrete backplane provides: connection registry ---

        /// <summary>Writes a registration, replacing any previous one for the same connection id.</summary>
        protected abstract Task StoreRegistrationAsync(SignalARRRConnectionRegistration registration, CancellationToken cancellationToken);

        /// <summary>Reads a registration as stored, without judging whether its node is alive.</summary>
        protected abstract Task<SignalARRRConnectionRegistration?> LoadRegistrationAsync(string connectionId, CancellationToken cancellationToken);

        public abstract Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken = default);

        public abstract Task<IReadOnlyList<SignalARRRConnectionRegistration>> FindConnectionsAsync(
            Type hubType,
            string? groupName = null,
            string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null,
            CancellationToken cancellationToken = default);

        public abstract Task AddConnectionToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default);

        public abstract Task RemoveConnectionFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default);

        // --- What a concrete backplane provides: node presence ---

        /// <summary>Records that this node is alive right now, creating its entry if necessary.</summary>
        protected abstract Task WriteHeartbeatAsync(CancellationToken cancellationToken);

        /// <summary>Whether <paramref name="nodeId"/> has heartbeated within <see cref="NodeTimeout"/>.</summary>
        protected abstract Task<bool> IsNodeAliveAsync(string nodeId, CancellationToken cancellationToken);

        /// <summary>Every node the store knows of, alive or not.</summary>
        protected abstract Task<IReadOnlyList<string>> GetKnownNodeIdsAsync(CancellationToken cancellationToken);

        /// <summary>Removes <paramref name="nodeId"/> and every registration it owns.</summary>
        protected abstract Task CleanupNodeAsync(string nodeId, CancellationToken cancellationToken);

        /// <summary>
        /// Runs once per heartbeat iteration after the sweep, for housekeeping the store needs
        /// that a TTL would otherwise do for free. Failures are logged and retried next iteration.
        /// </summary>
        protected virtual Task RunMaintenanceAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        // --- Lifecycle ---

        public async Task StartAsync(CancellationToken cancellationToken) {
            if (_started) {
                return;
            }

            await StartTransportAsync(cancellationToken).ConfigureAwait(false);
            _started = true;

            // A process that has just started serves no connections, so anything the store still
            // holds under this node id belongs to a previous incarnation that did not get to say
            // goodbye. Left in place, the new process would keep the node's heartbeat alive and
            // those stale registrations would never be swept — they would be routed to, forever,
            // and never answer. Best effort: the heartbeat loop copes with a store that is
            // unreachable right now, and so must this.
            try {
                await CleanupNodeAsync(NodeId, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger.LogWarning(ex,
                    "Could not clear registrations left behind under node id {NodeId}; continuing.", NodeId);
            }

            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = RunHeartbeatLoopAsync(_heartbeatCts.Token);
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            if (_heartbeatCts != null) {
                await _heartbeatCts.CancelAsync().ConfigureAwait(false);
                if (_heartbeatTask != null) {
                    await _heartbeatTask.ConfigureAwait(false);
                }
                _heartbeatCts.Dispose();
                _heartbeatCts = null;
                _heartbeatTask = null;
            }

            if (_started) {
                try {
                    await CleanupNodeAsync(NodeId, cancellationToken).ConfigureAwait(false);
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    // The other nodes sweep it once the heartbeat lapses; shutdown must not throw.
                    _logger.LogWarning(ex, "Could not deregister node {NodeId} on shutdown.", NodeId);
                }
            }

            await StopTransportAsync(cancellationToken).ConfigureAwait(false);
            _started = false;
        }

        public virtual void Dispose() {
            _cleanupSemaphore.Dispose();
            _heartbeatCts?.Dispose();
        }

        // --- ISignalARRRBackplane ---

        public async Task PublishDispatchAsync(
            Type? hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            IReadOnlyList<string>? connectionIds = null,
            string? groupName = null,
            string? userId = null,
            string? signalRMethodName = null,
            CancellationToken cancellationToken = default) {
            string? targetNodeId = null;
            if (targetKind == SignalARRRBackplaneTargetKind.Connections) {
                var routing = await ResolveConnectionOrThrowAsync(connectionIds, cancellationToken).ConfigureAwait(false);
                targetNodeId = routing.NodeId;
                hubType ??= ResolveType(routing.HubType);
            }

            var envelope = new SignalARRRBackplaneEnvelope {
                OriginNodeId = NodeId,
                TargetNodeId = targetNodeId,
                Kind = SignalARRRBackplaneEnvelopeKind.Dispatch,
                HubType = hubType == null ? string.Empty : WireTypeName.From(hubType),
                TargetKind = targetKind,
                ConnectionIds = connectionIds == null ? Array.Empty<string>() : new List<string>(connectionIds).ToArray(),
                GroupName = groupName,
                UserId = userId,
                Message = message,
                SignalRMethod = signalRMethodName
            };

            await PublishCommandAsync(envelope).ConfigureAwait(false);
        }

        public async Task<object?> InvokeConnectionAsync(
            Type? hubType,
            string connectionId,
            ServerRequestMessage message,
            Type resultType,
            CancellationToken cancellationToken = default) {
            var routing = await ResolveConnectionOrThrowAsync(new[] { connectionId }, cancellationToken).ConfigureAwait(false);
            hubType ??= ResolveType(routing.HubType);

            var requestId = Guid.NewGuid();
            var pending = new PendingInvoke(resultType);
            if (!_pendingInvocations.TryAdd(requestId, pending)) {
                throw new InvalidOperationException("Could not register backplane invoke request.");
            }

            var envelope = new SignalARRRBackplaneEnvelope {
                OriginNodeId = NodeId,
                TargetNodeId = routing.NodeId,
                Kind = SignalARRRBackplaneEnvelopeKind.InvokeRequest,
                HubType = hubType == null ? string.Empty : WireTypeName.From(hubType),
                TargetKind = SignalARRRBackplaneTargetKind.Connections,
                ConnectionIds = new[] { connectionId },
                Message = message,
                RequestId = requestId,
                ResultType = WireTypeName.From(resultType)
            };

            try {
                await PublishCommandAsync(envelope).ConfigureAwait(false);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_invokeTimeout);
                return await pending.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException($"Timed out waiting for backplane response for connection '{connectionId}'.");
            } finally {
                _pendingInvocations.TryRemove(requestId, out _);
            }
        }

        public async Task<IReadOnlyList<SignalARRRBackplaneInvokeResult>> InvokeQueryAsync(
            Type hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            Type resultType,
            string? groupName = null,
            string? userId = null,
            bool singleResult = false,
            CancellationToken cancellationToken = default) {
            var localResults = singleResult
                ? await GetLocalSingleInvokeResultAsync(hubType, targetKind, message, resultType, groupName, userId, cancellationToken).ConfigureAwait(false)
                : await _localDispatcher.InvokeAsync(hubType, targetKind, message, resultType, groupName: groupName, userId: userId, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (singleResult && localResults.Count > 0) {
                return localResults;
            }

            var activeRemoteNodes = await GetActiveRemoteNodeIdsAsync(cancellationToken).ConfigureAwait(false);
            if (activeRemoteNodes.Count == 0) {
                return localResults;
            }

            var requestId = Guid.NewGuid();
            var pending = new PendingQueryInvoke(resultType, activeRemoteNodes, singleResult, localResults);
            if (!_pendingQueryInvocations.TryAdd(requestId, pending)) {
                throw new InvalidOperationException("Could not register backplane query invoke request.");
            }

            var envelope = new SignalARRRBackplaneEnvelope {
                OriginNodeId = NodeId,
                Kind = SignalARRRBackplaneEnvelopeKind.InvokeQueryRequest,
                HubType = WireTypeName.From(hubType),
                TargetKind = targetKind,
                GroupName = groupName,
                UserId = userId,
                Message = message,
                RequestId = requestId,
                ResultType = WireTypeName.From(resultType),
                ErrorMessage = singleResult ? "single" : null
            };

            try {
                await PublishCommandAsync(envelope).ConfigureAwait(false);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_invokeTimeout);
                return await pending.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                // Return what did arrive rather than discarding it. There is no per-node deadline and
                // no way to force an answer, so a single node that is restarting, wedged, or unable
                // to resolve the types used to make the whole query throw after the full timeout --
                // taking the local results and every other node's answers down with it. A partial
                // answer is the honest outcome; the log names who did not reply.
                _logger.LogWarning(
                    "Cluster invoke query {RequestId} timed out after {Timeout}; returning partial results. Node(s) that did not respond: {OutstandingNodes}.",
                    requestId, _invokeTimeout, string.Join(", ", pending.OutstandingNodes));

                pending.CompleteWithPartialResults();
                return await pending.Completion.Task.ConfigureAwait(false);
            } finally {
                _pendingQueryInvocations.TryRemove(requestId, out _);
            }
        }

        public async Task PublishGroupCommandAsync(
            Type? hubType,
            string connectionId,
            string groupName,
            SignalARRRBackplaneGroupAction action,
            CancellationToken cancellationToken = default) {
            var routing = await ResolveConnectionOrThrowAsync(new[] { connectionId }, cancellationToken).ConfigureAwait(false);
            hubType ??= ResolveType(routing.HubType);

            var envelope = new SignalARRRBackplaneEnvelope {
                OriginNodeId = NodeId,
                TargetNodeId = routing.NodeId,
                Kind = SignalARRRBackplaneEnvelopeKind.GroupCommand,
                HubType = hubType == null ? string.Empty : WireTypeName.From(hubType),
                ConnectionIds = new[] { connectionId },
                GroupName = groupName,
                GroupAction = action
            };

            await PublishCommandAsync(envelope).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> GetActiveNodesAsync(CancellationToken cancellationToken = default) {
            var nodes = await GetActiveRemoteNodeIdsAsync(cancellationToken).ConfigureAwait(false);
            nodes.Insert(0, NodeId);
            return nodes;
        }

        public Task PublishClusterEventAsync(string subject, string payloadJson, CancellationToken cancellationToken = default) {
            var envelope = new SignalARRRBackplaneEnvelope {
                OriginNodeId = NodeId,
                Kind = SignalARRRBackplaneEnvelopeKind.ClusterEvent,
                ClusterSubject = subject,
                PayloadJson = payloadJson
            };

            return PublishCommandAsync(envelope);
        }

        // --- ISignalARRRConnectionRegistry ---

        public Task RegisterConnectionAsync(ClientContext clientContext, CancellationToken cancellationToken = default) {
            var registration = new SignalARRRConnectionRegistration {
                ConnectionId = clientContext.Id,
                NodeId = NodeId,
                HubType = WireTypeName.From(clientContext.HARRRType),
                UserId = clientContext.UserIdentifier,
                Groups = clientContext.Groups.ToArray(),
                Attributes = clientContext.Attributes
                    .Select(a => new SignalARRRConnectionAttribute {
                        Key = a.Key,
                        Values = a.Value.Where(v => v != null).Select(v => v!).ToArray()
                    })
                    .ToArray()
            };

            return StoreRegistrationAsync(registration, cancellationToken);
        }

        /// <summary>
        /// A registration whose node is alive. A registration owned by a dead node is reported as
        /// absent and triggers that node's cleanup, so a stale entry is never routed to twice.
        /// </summary>
        public virtual async Task<SignalARRRConnectionRegistration?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) {
            var registration = await LoadRegistrationAsync(connectionId, cancellationToken).ConfigureAwait(false);
            if (registration == null) {
                return null;
            }

            if (!await IsNodeAliveAsync(registration.NodeId, cancellationToken).ConfigureAwait(false)) {
                await CleanupNodeIfDeadAsync(registration.NodeId, cancellationToken).ConfigureAwait(false);
                return null;
            }

            return registration;
        }

        // --- ISignalARRRBackplaneHealth (O-8) ---

        public TimeSpan HeartbeatInterval => _heartbeatInterval;

        public DateTime? LastSuccessfulHeartbeatUtc => _lastSuccessfulHeartbeatUtc;

        public bool HeartbeatLoopFaulted => _heartbeatTask?.IsFaulted == true;

        // --- Incoming envelopes ---

        /// <summary>Deserializes a received payload and handles it; never throws.</summary>
        protected async Task HandleIncomingPayloadAsync(string payload) {
            SignalARRRBackplaneEnvelope? envelope;
            try {
                envelope = JsonSerializer.Deserialize<SignalARRRBackplaneEnvelope>(payload, _serializerOptions);
            } catch (Exception ex) {
                _logger.LogError(ex, "Unhandled SignalARRR backplane message error.");
                return;
            }

            if (envelope != null) {
                await HandleEnvelopeAsync(envelope).ConfigureAwait(false);
            }
        }

        /// <summary>Routes a received envelope to its handler; never throws.</summary>
        protected async Task HandleEnvelopeAsync(SignalARRRBackplaneEnvelope envelope) {
            try {
                if (!string.IsNullOrWhiteSpace(envelope.TargetNodeId) &&
                    !string.Equals(envelope.TargetNodeId, NodeId, StringComparison.Ordinal)) {
                    return;
                }

                if (envelope.Kind != SignalARRRBackplaneEnvelopeKind.InvokeResponse &&
                    string.Equals(envelope.OriginNodeId, NodeId, StringComparison.Ordinal)) {
                    return;
                }

                switch (envelope.Kind) {
                    case SignalARRRBackplaneEnvelopeKind.Dispatch:
                        await HandleDispatchAsync(envelope).ConfigureAwait(false);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeRequest:
                        await HandleInvokeRequestAsync(envelope).ConfigureAwait(false);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeResponse:
                        HandleInvokeResponse(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeQueryRequest:
                        await HandleInvokeQueryRequestAsync(envelope).ConfigureAwait(false);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeQueryResult:
                        HandleInvokeQueryResult(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeQueryCompleted:
                        HandleInvokeQueryCompleted(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.GroupCommand:
                        await HandleGroupCommandAsync(envelope).ConfigureAwait(false);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.ClusterEvent:
                        if (!string.IsNullOrEmpty(envelope.ClusterSubject) && envelope.PayloadJson != null) {
                            _clusterSubjects.Dispatch(envelope.ClusterSubject!, envelope.PayloadJson);
                        }
                        break;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Unhandled SignalARRR backplane message error.");
            }
        }

        private async Task HandleDispatchAsync(SignalARRRBackplaneEnvelope envelope) {
            var hubType = ResolveType(envelope.HubType);
            if (envelope.Message == null) {
                return;
            }

            await _localDispatcher.DispatchAsync(
                hubType,
                envelope.TargetKind,
                envelope.Message,
                envelope.ConnectionIds,
                envelope.GroupName,
                envelope.UserId,
                envelope.SignalRMethod).ConfigureAwait(false);
        }

        private async Task HandleGroupCommandAsync(SignalARRRBackplaneEnvelope envelope) {
            var hubType = ResolveType(envelope.HubType);
            if (envelope.GroupAction == null || envelope.ConnectionIds.Length == 0 || string.IsNullOrWhiteSpace(envelope.GroupName)) {
                return;
            }

            await _localDispatcher.ApplyGroupCommandAsync(
                hubType,
                envelope.ConnectionIds[0],
                envelope.GroupName,
                envelope.GroupAction.Value).ConfigureAwait(false);

            if (envelope.GroupAction == SignalARRRBackplaneGroupAction.Add) {
                await AddConnectionToGroupAsync(envelope.ConnectionIds[0], envelope.GroupName).ConfigureAwait(false);
            } else {
                await RemoveConnectionFromGroupAsync(envelope.ConnectionIds[0], envelope.GroupName).ConfigureAwait(false);
            }
        }

        private async Task HandleInvokeRequestAsync(SignalARRRBackplaneEnvelope envelope) {
            var hubType = ResolveType(envelope.HubType);
            var resultType = ResolveType(envelope.ResultType);

            if (resultType == null || envelope.Message == null || envelope.RequestId == null || envelope.ConnectionIds.Length == 0) {
                return;
            }

            try {
                var (handled, result) = await _localDispatcher.InvokeConnectionAsync(
                    hubType,
                    envelope.ConnectionIds[0],
                    envelope.Message,
                    resultType).ConfigureAwait(false);

                if (!handled) {
                    return;
                }

                await PublishResponseAsync(envelope.OriginNodeId, new SignalARRRBackplaneEnvelope {
                    OriginNodeId = NodeId,
                    TargetNodeId = envelope.OriginNodeId,
                    Kind = SignalARRRBackplaneEnvelopeKind.InvokeResponse,
                    HubType = envelope.HubType,
                    RequestId = envelope.RequestId,
                    ResultJson = result == null ? null : JsonSerializer.Serialize(result, resultType, _serializerOptions)
                }).ConfigureAwait(false);
            } catch (Exception ex) {
                // The full structured error, not ex.Message: flattening here was why single-node
                // and multi-node produced different exception types for the same failure.
                await PublishResponseAsync(envelope.OriginNodeId, new SignalARRRBackplaneEnvelope {
                    OriginNodeId = NodeId,
                    TargetNodeId = envelope.OriginNodeId,
                    Kind = SignalARRRBackplaneEnvelopeKind.InvokeResponse,
                    HubType = envelope.HubType,
                    RequestId = envelope.RequestId,
                    ErrorMessage = ex.Message,
                    ErrorJson = HARRRException.Wrap(ex).Message
                }).ConfigureAwait(false);
            }
        }

        private void HandleInvokeResponse(SignalARRRBackplaneEnvelope envelope) {
            if (envelope.RequestId == null || !string.Equals(envelope.TargetNodeId, NodeId, StringComparison.Ordinal)) {
                return;
            }

            if (!_pendingInvocations.TryGetValue(envelope.RequestId.Value, out var pending)) {
                return;
            }

            if (!string.IsNullOrWhiteSpace(envelope.ErrorMessage) || !string.IsNullOrWhiteSpace(envelope.ErrorJson)) {
                // Rehydrated as the same type the single-node path throws — previously this was an
                // InvalidOperationException, so the same failure produced different exception
                // types depending on whether a backplane was in play.
                var error = !string.IsNullOrWhiteSpace(envelope.ErrorJson)
                    ? HARRRError.Parse(envelope.ErrorJson!)
                    : HARRRError.Parse(envelope.ErrorMessage!);
                pending.Completion.TrySetException(new HARRRRemoteException(error));
                return;
            }

            object? result = null;
            if (envelope.ResultJson != null) {
                result = JsonSerializer.Deserialize(envelope.ResultJson, pending.ResultType, _serializerOptions);
            }

            pending.Completion.TrySetResult(result);
        }

        private async Task HandleInvokeQueryRequestAsync(SignalARRRBackplaneEnvelope envelope) {
            if (envelope.RequestId == null) {
                return;
            }

            try {
                var hubType = ResolveType(envelope.HubType);
                var resultType = ResolveType(envelope.ResultType);

                // Inside the try on purpose. This used to return before it, so the finally that
                // publishes InvokeQueryCompleted never ran and the asking node waited out its full
                // timeout for an answer that could never come. A node unable to resolve the types --
                // during a rolling deployment, or when a second application shares the same store
                // and prefix -- silently stalled every cluster query.
                if (hubType == null || resultType == null || envelope.Message == null) {
                    _logger.LogDebug(
                        "Ignoring cluster query {RequestId} from node {OriginNodeId}: hub type '{HubType}' or result type '{ResultType}' is unknown here.",
                        envelope.RequestId, envelope.OriginNodeId, envelope.HubType, envelope.ResultType);
                    return;
                }

                var singleResult = string.Equals(envelope.ErrorMessage, "single", StringComparison.Ordinal);
                var results = singleResult
                    ? await GetLocalSingleInvokeResultAsync(hubType, envelope.TargetKind, envelope.Message, resultType, envelope.GroupName, envelope.UserId, CancellationToken.None).ConfigureAwait(false)
                    : await _localDispatcher.InvokeAsync(hubType, envelope.TargetKind, envelope.Message, resultType, envelope.ConnectionIds, envelope.GroupName, envelope.UserId, CancellationToken.None).ConfigureAwait(false);

                foreach (var result in results) {
                    await PublishResponseAsync(envelope.OriginNodeId, new SignalARRRBackplaneEnvelope {
                        OriginNodeId = NodeId,
                        TargetNodeId = envelope.OriginNodeId,
                        Kind = SignalARRRBackplaneEnvelopeKind.InvokeQueryResult,
                        HubType = envelope.HubType,
                        RequestId = envelope.RequestId,
                        ConnectionIds = new[] { result.ConnectionId },
                        ResultJson = result.Value == null ? null : JsonSerializer.Serialize(result.Value, resultType, _serializerOptions)
                    }).ConfigureAwait(false);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Unhandled SignalARRR backplane invoke query error.");
            } finally {
                await PublishResponseAsync(envelope.OriginNodeId, new SignalARRRBackplaneEnvelope {
                    OriginNodeId = NodeId,
                    TargetNodeId = envelope.OriginNodeId,
                    Kind = SignalARRRBackplaneEnvelopeKind.InvokeQueryCompleted,
                    HubType = envelope.HubType,
                    RequestId = envelope.RequestId
                }).ConfigureAwait(false);
            }
        }

        private void HandleInvokeQueryResult(SignalARRRBackplaneEnvelope envelope) {
            if (envelope.RequestId == null || !string.Equals(envelope.TargetNodeId, NodeId, StringComparison.Ordinal)) {
                return;
            }

            if (!_pendingQueryInvocations.TryGetValue(envelope.RequestId.Value, out var pending) || envelope.ConnectionIds.Length == 0) {
                return;
            }

            object? result = null;
            if (envelope.ResultJson != null) {
                result = JsonSerializer.Deserialize(envelope.ResultJson, pending.ResultType, _serializerOptions);
            }

            pending.TryAddResult(new SignalARRRBackplaneInvokeResult {
                ConnectionId = envelope.ConnectionIds[0],
                Value = result
            });
        }

        private void HandleInvokeQueryCompleted(SignalARRRBackplaneEnvelope envelope) {
            if (envelope.RequestId == null || !string.Equals(envelope.TargetNodeId, NodeId, StringComparison.Ordinal)) {
                return;
            }

            if (_pendingQueryInvocations.TryGetValue(envelope.RequestId.Value, out var pending)) {
                // Attributed to the node that sent it, so an answer from a node this query never
                // waited on cannot complete it on someone else's behalf.
                pending.MarkCompleted(envelope.OriginNodeId);
            }
        }

        // --- Heartbeat and sweep ---

        /// <summary>
        /// Keeps this node's heartbeat alive and sweeps nodes that stopped heartbeating.
        /// </summary>
        /// <remarks>
        /// Every iteration is guarded. Previously only <see cref="OperationCanceledException"/> was
        /// caught and the priming calls sat outside the try entirely, so a single transient
        /// connection exception — routine during a failover or a one-second network blip — ended
        /// the loop permanently. Nothing observed the faulted task, so nothing logged it and
        /// nothing restarted it.
        /// <para>
        /// The consequence was not local: once the heartbeat lapsed, every other node treated this
        /// one as dead and deleted all of its connection registrations, while it went on serving
        /// those very connections. It became invisible cluster-wide, permanently, and only a restart
        /// recovered it.
        /// </para>
        /// </remarks>
        private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken) {
            using var timer = new PeriodicTimer(_heartbeatInterval);

            await RunHeartbeatIterationAsync(cancellationToken).ConfigureAwait(false);

            try {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) {
                    await RunHeartbeatIterationAsync(cancellationToken).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
                // Shutdown.
            }
        }

        private async Task RunHeartbeatIterationAsync(CancellationToken cancellationToken) {
            try {
                await RefreshHeartbeatAsync(cancellationToken).ConfigureAwait(false);
                await SweepStaleNodesAsync(cancellationToken).ConfigureAwait(false);
                await RunMaintenanceAsync(cancellationToken).ConfigureAwait(false);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                SignalARRRServerTelemetry.BackplaneHeartbeatFailures.Add(1);

                // Keep ticking: the next iteration re-registers whatever this one failed to write,
                // which is what makes a transient store outage survivable instead of terminal.
                _logger.LogError(ex,
                    "SignalARRR backplane heartbeat iteration failed for node {NodeId}; retrying in {Interval}.",
                    NodeId, _heartbeatInterval);
            }
        }

        /// <summary>
        /// Refreshes this node's heartbeat, and re-registers its connections if the node had been
        /// declared dead in the meantime.
        /// </summary>
        /// <remarks>
        /// Registrations were written once at connect time and never re-asserted, while liveness was
        /// judged solely by another node's view of the heartbeat — and the cleanup that follows is
        /// destructive and irreversible. A stop-the-world GC, thread pool starvation or a partial
        /// network partition longer than <see cref="NodeTimeout"/> was therefore enough for another
        /// node to wipe every registration belonging to this one.
        /// <para>
        /// The node then came back, refreshed its heartbeat and looked healthy again — but all of its
        /// connections were gone from the hub, group, user and attribute indexes, permanently.
        /// <see cref="CleanupNodeIfDeadAsync"/> skips the local node, so it could not even repair
        /// itself. Detecting the eviction and re-registering makes the outcome recoverable instead.
        /// </para>
        /// </remarks>
        private async Task RefreshHeartbeatAsync(CancellationToken cancellationToken) {
            var wasConsideredDead = _heartbeatEstablished
                && !await IsNodeAliveAsync(NodeId, cancellationToken).ConfigureAwait(false);

            await WriteHeartbeatAsync(cancellationToken).ConfigureAwait(false);
            _heartbeatEstablished = true;
            _lastSuccessfulHeartbeatUtc = DateTime.UtcNow;

            if (wasConsideredDead) {
                SignalARRRServerTelemetry.BackplaneSelfEvictions.Add(1);
                await ReregisterLocalConnectionsAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ReregisterLocalConnectionsAsync(CancellationToken cancellationToken) {
            var connections = _localDispatcher.GetLocalConnections().ToList();

            _logger.LogWarning(
                "SignalARRR backplane node {NodeId} lost its heartbeat and was treated as dead; re-registering {ConnectionCount} live connection(s).",
                NodeId, connections.Count);

            foreach (var client in connections) {
                cancellationToken.ThrowIfCancellationRequested();

                try {
                    await RegisterConnectionAsync(client, cancellationToken).ConfigureAwait(false);
                } catch (Exception ex) {
                    _logger.LogError(ex,
                        "Could not re-register connection {ConnectionId} for node {NodeId}.",
                        client.Id, NodeId);
                }
            }
        }

        /// <summary>Removes <paramref name="nodeId"/> if it is not this node and no longer alive.</summary>
        protected async Task CleanupNodeIfDeadAsync(string nodeId, CancellationToken cancellationToken = default) {
            if (string.Equals(nodeId, NodeId, StringComparison.Ordinal)) {
                return;
            }

            if (await IsNodeAliveAsync(nodeId, cancellationToken).ConfigureAwait(false)) {
                return;
            }

            await _cleanupSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (await IsNodeAliveAsync(nodeId, cancellationToken).ConfigureAwait(false)) {
                    return;
                }

                SignalARRRServerTelemetry.BackplaneNodesSwept.Add(1);
                await CleanupNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            } finally {
                _cleanupSemaphore.Release();
            }
        }

        private async Task SweepStaleNodesAsync(CancellationToken cancellationToken) {
            var nodeIds = await GetKnownNodeIdsAsync(cancellationToken).ConfigureAwait(false);
            foreach (var nodeId in nodeIds) {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(nodeId) || string.Equals(nodeId, NodeId, StringComparison.Ordinal)) {
                    continue;
                }

                await CleanupNodeIfDeadAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<List<string>> GetActiveRemoteNodeIdsAsync(CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            var nodeIds = await GetKnownNodeIdsAsync(cancellationToken).ConfigureAwait(false);
            var activeNodes = new List<string>(nodeIds.Count);
            foreach (var nodeId in nodeIds) {
                if (string.IsNullOrWhiteSpace(nodeId) || string.Equals(nodeId, NodeId, StringComparison.Ordinal)) {
                    continue;
                }

                if (await IsNodeAliveAsync(nodeId, cancellationToken).ConfigureAwait(false)) {
                    activeNodes.Add(nodeId);
                } else {
                    await CleanupNodeIfDeadAsync(nodeId, cancellationToken).ConfigureAwait(false);
                }
            }

            return activeNodes;
        }

        // --- Helpers for implementations ---

        protected static Type? ResolveType(string? typeName) {
            return WireTypeName.Resolve(typeName);
        }

        private async Task<SignalARRRConnectionRegistration> ResolveConnectionOrThrowAsync(IReadOnlyList<string>? connectionIds, CancellationToken cancellationToken) {
            if (connectionIds == null || connectionIds.Count == 0) {
                throw new InvalidOperationException("No connection ids supplied for targeted backplane dispatch.");
            }

            var registration = await FindConnectionAsync(connectionIds[0], cancellationToken).ConfigureAwait(false);
            if (registration == null) {
                throw new InvalidOperationException($"Client not found: {connectionIds[0]}");
            }

            return registration;
        }

        private async Task<IReadOnlyList<SignalARRRBackplaneInvokeResult>> GetLocalSingleInvokeResultAsync(
            Type hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            Type resultType,
            string? groupName,
            string? userId,
            CancellationToken cancellationToken) {
            var result = await _localDispatcher.InvokeOneAsync(
                hubType,
                targetKind,
                message,
                resultType,
                groupName: groupName,
                userId: userId,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return result == null
                ? Array.Empty<SignalARRRBackplaneInvokeResult>()
                : new[] { result };
        }

        protected static bool MatchesGroupFilter(SignalARRRConnectionRegistration registration, string? groupName) {
            return string.IsNullOrWhiteSpace(groupName) ||
                registration.Groups.Any(g => string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase));
        }

        protected static bool MatchesUserFilter(SignalARRRConnectionRegistration registration, string? userId) {
            return string.IsNullOrWhiteSpace(userId) ||
                string.Equals(registration.UserId, userId, StringComparison.Ordinal);
        }

        protected static bool MatchesAttributeFilters(
            SignalARRRConnectionRegistration registration,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters) {
            if (attributeFilters == null || attributeFilters.Count == 0) {
                return true;
            }

            foreach (var filter in attributeFilters) {
                var attribute = registration.Attributes.FirstOrDefault(a =>
                    string.Equals(a.Key, filter.Key, StringComparison.OrdinalIgnoreCase));
                if (attribute == null) {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(filter.Value) &&
                    !attribute.Values.Any(v => v.Match(filter.Value))) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Attribute keys are matched case-insensitively; indexes store them normalized.</summary>
        protected static string NormalizeAttributeKey(string key) => key.ToUpperInvariant();

        private sealed class PendingInvoke {
            public Type ResultType { get; }
            public TaskCompletionSource<object?> Completion { get; } = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingInvoke(Type resultType) {
                ResultType = resultType;
            }
        }

        /// <summary>
        /// Tracks the nodes a cluster query is still waiting on.
        /// </summary>
        /// <remarks>
        /// By identity, not by count. The node count was sampled before the request was published, so
        /// any node that subscribed in that window — routine during scale-out or a rolling restart —
        /// also received the request and also answered. The extra decrement drove the counter to zero
        /// early: with two nodes counted and three answering, the query completed after the two
        /// fastest and silently omitted the third node's clients. A successful-looking result with
        /// missing entries is worse than a timeout, because nothing indicates it happened.
        /// </remarks>
        private sealed class PendingQueryInvoke {
            private readonly bool _singleResult;
            private readonly HashSet<string> _outstandingNodes;
            private readonly List<SignalARRRBackplaneInvokeResult> _results;
            private readonly object _syncRoot = new object();

            public Type ResultType { get; }
            public TaskCompletionSource<IReadOnlyList<SignalARRRBackplaneInvokeResult>> Completion { get; }
                = new TaskCompletionSource<IReadOnlyList<SignalARRRBackplaneInvokeResult>>(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingQueryInvoke(Type resultType, IEnumerable<string> expectedNodes, bool singleResult, IReadOnlyList<SignalARRRBackplaneInvokeResult> initialResults) {
                ResultType = resultType;
                _outstandingNodes = new HashSet<string>(expectedNodes, StringComparer.Ordinal);
                _singleResult = singleResult;
                _results = new List<SignalARRRBackplaneInvokeResult>(initialResults);

                if (_singleResult && _results.Count > 0) {
                    Completion.TrySetResult(new[] { _results[0] });
                } else if (_outstandingNodes.Count == 0) {
                    Completion.TrySetResult(Snapshot());
                }
            }

            /// <summary>The nodes that have not answered yet.</summary>
            public IReadOnlyCollection<string> OutstandingNodes {
                get {
                    lock (_syncRoot) {
                        return _outstandingNodes.ToArray();
                    }
                }
            }

            public void TryAddResult(SignalARRRBackplaneInvokeResult result) {
                lock (_syncRoot) {
                    _results.Add(result);
                    if (_singleResult) {
                        Completion.TrySetResult(new[] { result });
                    }
                }
            }

            /// <summary>
            /// Records that <paramref name="nodeId"/> has finished. Unknown nodes are ignored, so a
            /// late subscriber cannot complete the query on another node's behalf.
            /// </summary>
            public void MarkCompleted(string? nodeId) {
                lock (_syncRoot) {
                    if (nodeId == null || !_outstandingNodes.Remove(nodeId) || _outstandingNodes.Count > 0) {
                        return;
                    }

                    Completion.TrySetResult(_singleResult && _results.Count > 0
                        ? new[] { _results[0] }
                        : Snapshot());
                }
            }

            /// <summary>
            /// Completes with what has arrived so far, for the case where some node never answers.
            /// </summary>
            public void CompleteWithPartialResults() {
                lock (_syncRoot) {
                    Completion.TrySetResult(_singleResult && _results.Count > 0
                        ? new[] { _results[0] }
                        : Snapshot());
                }
            }

            /// <summary>
            /// Copies the results before handing them out.
            /// </summary>
            /// <remarks>
            /// The live list used to be returned directly while <see cref="TryAddResult"/> kept
            /// mutating it, so a straggler arriving as the caller enumerated threw
            /// "Collection was modified".
            /// </remarks>
            private IReadOnlyList<SignalARRRBackplaneInvokeResult> Snapshot() => _results.ToArray();
        }
    }
}
