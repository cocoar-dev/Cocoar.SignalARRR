using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Cocoar.SignalARRR.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cocoar.SignalARRR.Server {
    internal sealed class RedisSignalARRRBackplane : ISignalARRRBackplane, ISignalARRRConnectionRegistry, IHostedService, IDisposable {
        private readonly SignalARRRRedisBackplaneOptions _options;
        private readonly LocalSignalARRRBackplaneDispatcher _localDispatcher;
        private readonly ILogger<RedisSignalARRRBackplane> _logger;
        private readonly ConcurrentDictionary<Guid, PendingInvoke> _pendingInvocations = new ConcurrentDictionary<Guid, PendingInvoke>();
        private readonly ConcurrentDictionary<Guid, PendingQueryInvoke> _pendingQueryInvocations = new ConcurrentDictionary<Guid, PendingQueryInvoke>();
        private readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly SemaphoreSlim _cleanupSemaphore = new SemaphoreSlim(1, 1);

        private ConnectionMultiplexer? _multiplexer;
        private ISubscriber? _subscriber;
        private IDatabase? _database;
        private CancellationTokenSource? _heartbeatCts;
        private Task? _heartbeatTask;

        public RedisSignalARRRBackplane(
            SignalARRRRedisBackplaneOptions options,
            LocalSignalARRRBackplaneDispatcher localDispatcher,
            ILogger<RedisSignalARRRBackplane> logger) {
            _options = options;
            _localDispatcher = localDispatcher;
            _logger = logger;
        }

        public bool IsEnabled => true;

        public string NodeId => _options.NodeId;

        public async Task StartAsync(CancellationToken cancellationToken) {
            if (_multiplexer != null) {
                return;
            }

            _multiplexer = await ConnectionMultiplexer.ConnectAsync(_options.ConnectionString);
            _subscriber = _multiplexer.GetSubscriber();
            _database = _multiplexer.GetDatabase();

            var commandsSubscription = await _subscriber.SubscribeAsync(RedisChannel.Literal(GetCommandsChannel()));
            commandsSubscription.OnMessage(channelMessage => {
                _ = HandleEnvelopeAsync(channelMessage.Message);
            });

            var responsesSubscription = await _subscriber.SubscribeAsync(RedisChannel.Literal(GetResponsesChannel(NodeId)));
            responsesSubscription.OnMessage(channelMessage => {
                _ = HandleEnvelopeAsync(channelMessage.Message);
            });

            _heartbeatCts = new CancellationTokenSource();
            _heartbeatTask = RunHeartbeatLoopAsync(_heartbeatCts.Token);
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            if (_heartbeatCts != null) {
                await _heartbeatCts.CancelAsync();
                if (_heartbeatTask != null) {
                    await _heartbeatTask;
                }
                _heartbeatCts.Dispose();
                _heartbeatCts = null;
                _heartbeatTask = null;
            }

            if (_database != null) {
                await CleanupNodeAsync(NodeId);
            }

            if (_subscriber != null) {
                await _subscriber.UnsubscribeAllAsync();
            }

            _multiplexer?.Dispose();
            _multiplexer = null;
            _subscriber = null;
            _database = null;
        }

        public async Task PublishDispatchAsync(
            Type? hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            IReadOnlyList<string>? connectionIds = null,
            string? groupName = null,
            string? userId = null,
            CancellationToken cancellationToken = default) {
            string? targetNodeId = null;
            if (targetKind == SignalARRRBackplaneTargetKind.Connections) {
                var routing = await ResolveConnectionOrThrowAsync(connectionIds);
                targetNodeId = routing.NodeId;
                hubType ??= ResolveType(routing.HubType);
            }

            var envelope = new SignalARRRBackplaneEnvelope {
                OriginNodeId = NodeId,
                TargetNodeId = targetNodeId,
                Kind = SignalARRRBackplaneEnvelopeKind.Dispatch,
                HubType = hubType?.AssemblyQualifiedName ?? hubType?.FullName ?? string.Empty,
                TargetKind = targetKind,
                ConnectionIds = connectionIds == null ? Array.Empty<string>() : new List<string>(connectionIds).ToArray(),
                GroupName = groupName,
                UserId = userId,
                Message = message
            };

            await PublishAsync(GetCommandsChannel(), envelope);
        }

        public async Task<object?> InvokeConnectionAsync(
            Type? hubType,
            string connectionId,
            ServerRequestMessage message,
            Type resultType,
            CancellationToken cancellationToken = default) {
            var routing = await ResolveConnectionOrThrowAsync(new[] { connectionId });
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
                HubType = hubType?.AssemblyQualifiedName ?? hubType?.FullName ?? string.Empty,
                TargetKind = SignalARRRBackplaneTargetKind.Connections,
                ConnectionIds = new[] { connectionId },
                Message = message,
                RequestId = requestId,
                ResultType = resultType.AssemblyQualifiedName
            };

            try {
                await PublishAsync(GetCommandsChannel(), envelope);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.InvokeTimeout);
                return await pending.Completion.Task.WaitAsync(timeoutCts.Token);
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
            var pending = new PendingQueryInvoke(resultType, activeRemoteNodes.Count, singleResult, localResults);
            if (!_pendingQueryInvocations.TryAdd(requestId, pending)) {
                throw new InvalidOperationException("Could not register backplane query invoke request.");
            }

            var envelope = new SignalARRRBackplaneEnvelope {
                OriginNodeId = NodeId,
                Kind = SignalARRRBackplaneEnvelopeKind.InvokeQueryRequest,
                HubType = hubType.AssemblyQualifiedName ?? hubType.FullName ?? string.Empty,
                TargetKind = targetKind,
                GroupName = groupName,
                UserId = userId,
                Message = message,
                RequestId = requestId,
                ResultType = resultType.AssemblyQualifiedName,
                ErrorMessage = singleResult ? "single" : null
            };

            try {
                await PublishAsync(GetCommandsChannel(), envelope).ConfigureAwait(false);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(_options.InvokeTimeout);
                return await pending.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                throw new TimeoutException("Timed out waiting for cluster invoke results.");
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
            var routing = await ResolveConnectionOrThrowAsync(new[] { connectionId });
            hubType ??= ResolveType(routing.HubType);

            var envelope = new SignalARRRBackplaneEnvelope {
                OriginNodeId = NodeId,
                TargetNodeId = routing.NodeId,
                Kind = SignalARRRBackplaneEnvelopeKind.GroupCommand,
                HubType = hubType?.AssemblyQualifiedName ?? hubType?.FullName ?? string.Empty,
                ConnectionIds = new[] { connectionId },
                GroupName = groupName,
                GroupAction = action
            };

            await PublishAsync(GetCommandsChannel(), envelope);
        }

        public async Task RegisterConnectionAsync(ClientContext clientContext, CancellationToken cancellationToken = default) {
            if (_database == null) {
                throw new InvalidOperationException("SignalARRR Redis backplane has not been started.");
            }

            var registration = new SignalARRRConnectionRegistration {
                ConnectionId = clientContext.Id,
                NodeId = NodeId,
                HubType = clientContext.HARRRType.AssemblyQualifiedName ?? clientContext.HARRRType.FullName ?? string.Empty,
                UserId = clientContext.UserIdentifier,
                Groups = clientContext.Groups.ToArray(),
                Attributes = clientContext.Attributes
                    .Select(a => new SignalARRRConnectionAttribute {
                        Key = a.Key,
                        Values = a.Value.Where(v => v != null).Select(v => v!).ToArray()
                    })
                    .ToArray()
            };

            await SaveConnectionRegistrationAsync(registration);
            await _database.SetAddAsync(GetNodeConnectionsKey(NodeId), clientContext.Id);
        }

        public async Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken = default) {
            if (_database == null) {
                return;
            }

            var registration = await FindConnectionAsync(connectionId, cancellationToken);
            if (registration == null) {
                await _database.KeyDeleteAsync(GetConnectionKey(connectionId));
                return;
            }

            await RemoveConnectionRegistrationAsync(registration);
            await _database.SetRemoveAsync(GetNodeConnectionsKey(registration.NodeId), connectionId);
        }

        public async Task<SignalARRRConnectionRegistration?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) {
            if (_database == null) {
                return null;
            }

            var key = GetConnectionKey(connectionId);
            var payload = await _database.StringGetAsync(key);
            if (payload.IsNullOrEmpty) {
                return null;
            }

            var registration = JsonSerializer.Deserialize<SignalARRRConnectionRegistration>((string)payload!, _serializerOptions);
            if (registration == null) {
                return null;
            }

            if (!await IsNodeAliveAsync(registration.NodeId)) {
                await CleanupNodeIfDeadAsync(registration.NodeId, cancellationToken).ConfigureAwait(false);
                return null;
            }

            return registration;
        }

        public async Task<IReadOnlyList<SignalARRRConnectionRegistration>> FindConnectionsAsync(
            Type hubType,
            string? groupName = null,
            string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null,
            CancellationToken cancellationToken = default) {
            if (_database == null) {
                return Array.Empty<SignalARRRConnectionRegistration>();
            }

            var hubTypeName = hubType.AssemblyQualifiedName ?? hubType.FullName ?? string.Empty;
            var indexKeys = new List<RedisKey> { GetHubConnectionsKey(hubTypeName) };
            if (!string.IsNullOrWhiteSpace(groupName)) {
                indexKeys.Add(GetGroupConnectionsKey(hubTypeName, groupName));
            }

            if (!string.IsNullOrWhiteSpace(userId)) {
                indexKeys.Add(GetUserConnectionsKey(hubTypeName, userId));
            }

            if (attributeFilters != null) {
                foreach (var filter in attributeFilters) {
                    indexKeys.Add(GetAttributeConnectionsKey(hubTypeName, filter.Key));
                }
            }

            RedisValue[] connectionIds;
            if (indexKeys.Count == 1) {
                connectionIds = await _database.SetMembersAsync(indexKeys[0]).ConfigureAwait(false);
            } else {
                connectionIds = await _database.SetCombineAsync(SetOperation.Intersect, indexKeys.ToArray()).ConfigureAwait(false);
            }

            if (connectionIds.Length == 0) {
                return Array.Empty<SignalARRRConnectionRegistration>();
            }

            var registrations = new List<SignalARRRConnectionRegistration>(connectionIds.Length);
            foreach (var connectionId in connectionIds.Select(v => (string?)v).Where(v => !string.IsNullOrWhiteSpace(v))) {
                cancellationToken.ThrowIfCancellationRequested();

                var registration = await FindConnectionAsync(connectionId!, cancellationToken).ConfigureAwait(false);
                if (registration == null || !string.Equals(registration.HubType, hubTypeName, StringComparison.Ordinal)) {
                    continue;
                }

                if (!MatchesGroupFilter(registration, groupName) ||
                    !MatchesUserFilter(registration, userId) ||
                    !MatchesAttributeFilters(registration, attributeFilters)) {
                    continue;
                }

                registrations.Add(registration);
            }

            return registrations;
        }

        public async Task AddConnectionToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            if (_database == null) {
                return;
            }

            var registration = await FindConnectionAsync(connectionId, cancellationToken).ConfigureAwait(false);
            if (registration == null || registration.Groups.Contains(groupName, StringComparer.OrdinalIgnoreCase)) {
                return;
            }

            registration.Groups = registration.Groups.Concat(new[] { groupName }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            await SaveConnectionRegistrationAsync(registration).ConfigureAwait(false);
        }

        public async Task RemoveConnectionFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            if (_database == null) {
                return;
            }

            var registration = await FindConnectionAsync(connectionId, cancellationToken).ConfigureAwait(false);
            if (registration == null || !registration.Groups.Contains(groupName, StringComparer.OrdinalIgnoreCase)) {
                return;
            }

            registration.Groups = registration.Groups.Where(g => !string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase)).ToArray();
            await SaveConnectionRegistrationAsync(registration).ConfigureAwait(false);
        }

        public void Dispose() {
            _cleanupSemaphore.Dispose();
            _heartbeatCts?.Dispose();
            _multiplexer?.Dispose();
        }

        private async Task PublishAsync(string channel, SignalARRRBackplaneEnvelope envelope) {
            if (_subscriber == null) {
                throw new InvalidOperationException("SignalARRR Redis backplane has not been started.");
            }

            var payload = JsonSerializer.Serialize(envelope, _serializerOptions);
            await _subscriber.PublishAsync(RedisChannel.Literal(channel), payload);
        }

        private async Task HandleEnvelopeAsync(RedisValue payload) {
            try {
                var envelope = JsonSerializer.Deserialize<SignalARRRBackplaneEnvelope>((string)payload!, _serializerOptions);
                if (envelope == null) {
                    return;
                }

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
                        await HandleDispatchAsync(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeRequest:
                        await HandleInvokeRequestAsync(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeResponse:
                        HandleInvokeResponse(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeQueryRequest:
                        await HandleInvokeQueryRequestAsync(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeQueryResult:
                        HandleInvokeQueryResult(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.InvokeQueryCompleted:
                        HandleInvokeQueryCompleted(envelope);
                        break;
                    case SignalARRRBackplaneEnvelopeKind.GroupCommand:
                        await HandleGroupCommandAsync(envelope);
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
                envelope.UserId);
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
                envelope.GroupAction.Value);

            if (envelope.GroupAction == SignalARRRBackplaneGroupAction.Add) {
                await AddConnectionToGroupAsync(envelope.ConnectionIds[0], envelope.GroupName);
            } else {
                await RemoveConnectionFromGroupAsync(envelope.ConnectionIds[0], envelope.GroupName);
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
                    resultType);

                if (!handled) {
                    return;
                }

                await PublishAsync(GetResponsesChannel(envelope.OriginNodeId), new SignalARRRBackplaneEnvelope {
                    OriginNodeId = NodeId,
                    TargetNodeId = envelope.OriginNodeId,
                    Kind = SignalARRRBackplaneEnvelopeKind.InvokeResponse,
                    HubType = envelope.HubType,
                    RequestId = envelope.RequestId,
                    ResultJson = result == null ? null : JsonSerializer.Serialize(result, resultType, _serializerOptions)
                });
            } catch (Exception ex) {
                await PublishAsync(GetResponsesChannel(envelope.OriginNodeId), new SignalARRRBackplaneEnvelope {
                    OriginNodeId = NodeId,
                    TargetNodeId = envelope.OriginNodeId,
                    Kind = SignalARRRBackplaneEnvelopeKind.InvokeResponse,
                    HubType = envelope.HubType,
                    RequestId = envelope.RequestId,
                    ErrorMessage = ex.Message
                });
            }
        }

        private void HandleInvokeResponse(SignalARRRBackplaneEnvelope envelope) {
            if (envelope.RequestId == null || !string.Equals(envelope.TargetNodeId, NodeId, StringComparison.Ordinal)) {
                return;
            }

            if (!_pendingInvocations.TryGetValue(envelope.RequestId.Value, out var pending)) {
                return;
            }

            if (!string.IsNullOrWhiteSpace(envelope.ErrorMessage)) {
                pending.Completion.TrySetException(new InvalidOperationException(envelope.ErrorMessage));
                return;
            }

            object? result = null;
            if (envelope.ResultJson != null) {
                result = JsonSerializer.Deserialize(envelope.ResultJson, pending.ResultType, _serializerOptions);
            }

            pending.Completion.TrySetResult(result);
        }

        private async Task HandleInvokeQueryRequestAsync(SignalARRRBackplaneEnvelope envelope) {
            var hubType = ResolveType(envelope.HubType);
            var resultType = ResolveType(envelope.ResultType);

            if (hubType == null || resultType == null || envelope.Message == null || envelope.RequestId == null) {
                return;
            }

            try {
                var singleResult = string.Equals(envelope.ErrorMessage, "single", StringComparison.Ordinal);
                var results = singleResult
                    ? await GetLocalSingleInvokeResultAsync(hubType, envelope.TargetKind, envelope.Message, resultType, envelope.GroupName, envelope.UserId, CancellationToken.None).ConfigureAwait(false)
                    : await _localDispatcher.InvokeAsync(hubType, envelope.TargetKind, envelope.Message, resultType, envelope.ConnectionIds, envelope.GroupName, envelope.UserId, CancellationToken.None).ConfigureAwait(false);

                foreach (var result in results) {
                    await PublishAsync(GetResponsesChannel(envelope.OriginNodeId), new SignalARRRBackplaneEnvelope {
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
                await PublishAsync(GetResponsesChannel(envelope.OriginNodeId), new SignalARRRBackplaneEnvelope {
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
                pending.MarkCompleted();
            }
        }

        private string GetCommandsChannel() => $"{_options.ChannelPrefix}:commands";

        private string GetResponsesChannel(string nodeId) => $"{_options.ChannelPrefix}:responses:{nodeId}";

        private string GetNodesKey() => $"{_options.ChannelPrefix}:nodes";

        private string GetNodeHeartbeatKey(string nodeId) => $"{_options.ChannelPrefix}:nodes:{nodeId}:heartbeat";

        private string GetNodeConnectionsKey(string nodeId) => $"{_options.ChannelPrefix}:nodes:{nodeId}:connections";

        private string GetConnectionKey(string connectionId) => $"{_options.ChannelPrefix}:connections:{connectionId}";

        private string GetHubConnectionsKey(string hubType) => $"{_options.ChannelPrefix}:hub:{hubType}:connections";

        private string GetUserConnectionsKey(string hubType, string userId) => $"{_options.ChannelPrefix}:hub:{hubType}:users:{userId}:connections";

        private string GetGroupConnectionsKey(string hubType, string groupName) => $"{_options.ChannelPrefix}:hub:{hubType}:groups:{groupName}:connections";

        private string GetAttributeConnectionsKey(string hubType, string attributeKey) => $"{_options.ChannelPrefix}:hub:{hubType}:attributes:{NormalizeAttributeKey(attributeKey)}:connections";

        private static Type? ResolveType(string? typeName) {
            return string.IsNullOrWhiteSpace(typeName) ? null : Type.GetType(typeName, throwOnError: false);
        }

        private async Task<SignalARRRConnectionRegistration> ResolveConnectionOrThrowAsync(IReadOnlyList<string>? connectionIds) {
            if (connectionIds == null || connectionIds.Count == 0) {
                throw new InvalidOperationException("No connection ids supplied for targeted backplane dispatch.");
            }

            var registration = await FindConnectionAsync(connectionIds[0]);
            if (registration == null) {
                throw new InvalidOperationException($"Client not found: {connectionIds[0]}");
            }

            return registration;
        }

        private async Task<bool> IsNodeAliveAsync(string nodeId) {
            if (_database == null) {
                return false;
            }

            return await _database.KeyExistsAsync(GetNodeHeartbeatKey(nodeId));
        }

        private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken) {
            if (_database == null) {
                return;
            }

            using var timer = new PeriodicTimer(_options.HeartbeatInterval);
            await RefreshHeartbeatAsync().ConfigureAwait(false);
            await SweepStaleNodesAsync(cancellationToken).ConfigureAwait(false);

            try {
                while (await timer.WaitForNextTickAsync(cancellationToken)) {
                    await RefreshHeartbeatAsync().ConfigureAwait(false);
                    await SweepStaleNodesAsync(cancellationToken).ConfigureAwait(false);
                }
            } catch (OperationCanceledException) {
            }
        }

        private async Task RefreshHeartbeatAsync() {
            if (_database == null) {
                return;
            }

            await _database.SetAddAsync(GetNodesKey(), NodeId);
            await _database.StringSetAsync(GetNodeHeartbeatKey(NodeId), "1", _options.NodeTimeout);
        }

        private async Task CleanupNodeAsync(string nodeId) {
            if (_database == null) {
                return;
            }

            var nodeConnectionsKey = GetNodeConnectionsKey(nodeId);
            var connectionIds = await _database.SetMembersAsync(nodeConnectionsKey);
            if (connectionIds.Length > 0) {
                foreach (var connectionId in connectionIds.Select(v => (string?)v).Where(v => !string.IsNullOrWhiteSpace(v))) {
                    var payload = await _database.StringGetAsync(GetConnectionKey(connectionId!)).ConfigureAwait(false);
                    if (!payload.IsNullOrEmpty) {
                        var registration = JsonSerializer.Deserialize<SignalARRRConnectionRegistration>((string)payload!, _serializerOptions);
                        if (registration != null) {
                            await RemoveConnectionRegistrationAsync(registration).ConfigureAwait(false);
                            continue;
                        }
                    }

                    await _database.KeyDeleteAsync(GetConnectionKey(connectionId!)).ConfigureAwait(false);
                }
            }

            await _database.KeyDeleteAsync(nodeConnectionsKey);
            await _database.KeyDeleteAsync(GetNodeHeartbeatKey(nodeId));
            await _database.SetRemoveAsync(GetNodesKey(), nodeId);
        }

        private async Task CleanupNodeIfDeadAsync(string nodeId, CancellationToken cancellationToken = default) {
            if (_database == null || string.Equals(nodeId, NodeId, StringComparison.Ordinal)) {
                return;
            }

            if (await IsNodeAliveAsync(nodeId).ConfigureAwait(false)) {
                return;
            }

            await _cleanupSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                if (_database == null || await IsNodeAliveAsync(nodeId).ConfigureAwait(false)) {
                    return;
                }

                await CleanupNodeAsync(nodeId).ConfigureAwait(false);
            } finally {
                _cleanupSemaphore.Release();
            }
        }

        private async Task SweepStaleNodesAsync(CancellationToken cancellationToken) {
            if (_database == null) {
                return;
            }

            var nodeIds = await _database.SetMembersAsync(GetNodesKey()).ConfigureAwait(false);
            foreach (var nodeIdValue in nodeIds) {
                cancellationToken.ThrowIfCancellationRequested();

                var nodeId = (string?)nodeIdValue;
                if (string.IsNullOrWhiteSpace(nodeId) || string.Equals(nodeId, NodeId, StringComparison.Ordinal)) {
                    continue;
                }

                await CleanupNodeIfDeadAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<List<string>> GetActiveRemoteNodeIdsAsync(CancellationToken cancellationToken) {
            cancellationToken.ThrowIfCancellationRequested();

            if (_database == null) {
                return new List<string>();
            }

            var nodeIds = await _database.SetMembersAsync(GetNodesKey()).ConfigureAwait(false);
            var activeNodes = new List<string>(nodeIds.Length);
            foreach (var nodeIdValue in nodeIds) {
                var nodeId = (string?)nodeIdValue;
                if (string.IsNullOrWhiteSpace(nodeId) || string.Equals(nodeId, NodeId, StringComparison.Ordinal)) {
                    continue;
                }

                if (await IsNodeAliveAsync(nodeId).ConfigureAwait(false)) {
                    activeNodes.Add(nodeId);
                } else {
                    await CleanupNodeIfDeadAsync(nodeId, cancellationToken).ConfigureAwait(false);
                }
            }

            return activeNodes;
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

        private async Task SaveConnectionRegistrationAsync(SignalARRRConnectionRegistration registration) {
            if (_database == null) {
                return;
            }

            var existingPayload = await _database.StringGetAsync(GetConnectionKey(registration.ConnectionId)).ConfigureAwait(false);
            if (!existingPayload.IsNullOrEmpty) {
                var existingRegistration = JsonSerializer.Deserialize<SignalARRRConnectionRegistration>((string)existingPayload!, _serializerOptions);
                if (existingRegistration != null) {
                    await RemoveConnectionIndexesAsync(existingRegistration).ConfigureAwait(false);
                }
            }

            await _database.StringSetAsync(
                GetConnectionKey(registration.ConnectionId),
                JsonSerializer.Serialize(registration, _serializerOptions)).ConfigureAwait(false);
            await AddConnectionIndexesAsync(registration).ConfigureAwait(false);
        }

        private async Task RemoveConnectionRegistrationAsync(SignalARRRConnectionRegistration registration) {
            if (_database == null) {
                return;
            }

            await RemoveConnectionIndexesAsync(registration).ConfigureAwait(false);
            await _database.KeyDeleteAsync(GetConnectionKey(registration.ConnectionId)).ConfigureAwait(false);
        }

        private async Task AddConnectionIndexesAsync(SignalARRRConnectionRegistration registration) {
            if (_database == null) {
                return;
            }

            await _database.SetAddAsync(GetHubConnectionsKey(registration.HubType), registration.ConnectionId).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(registration.UserId)) {
                await _database.SetAddAsync(GetUserConnectionsKey(registration.HubType, registration.UserId), registration.ConnectionId).ConfigureAwait(false);
            }

            foreach (var group in registration.Groups.Distinct(StringComparer.OrdinalIgnoreCase)) {
                await _database.SetAddAsync(GetGroupConnectionsKey(registration.HubType, group), registration.ConnectionId).ConfigureAwait(false);
            }

            foreach (var attribute in registration.Attributes.GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First())) {
                await _database.SetAddAsync(GetAttributeConnectionsKey(registration.HubType, attribute.Key), registration.ConnectionId).ConfigureAwait(false);
            }
        }

        private async Task RemoveConnectionIndexesAsync(SignalARRRConnectionRegistration registration) {
            if (_database == null) {
                return;
            }

            await _database.SetRemoveAsync(GetHubConnectionsKey(registration.HubType), registration.ConnectionId).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(registration.UserId)) {
                await _database.SetRemoveAsync(GetUserConnectionsKey(registration.HubType, registration.UserId), registration.ConnectionId).ConfigureAwait(false);
            }

            foreach (var group in registration.Groups.Distinct(StringComparer.OrdinalIgnoreCase)) {
                await _database.SetRemoveAsync(GetGroupConnectionsKey(registration.HubType, group), registration.ConnectionId).ConfigureAwait(false);
            }

            foreach (var attribute in registration.Attributes.GroupBy(a => a.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First())) {
                await _database.SetRemoveAsync(GetAttributeConnectionsKey(registration.HubType, attribute.Key), registration.ConnectionId).ConfigureAwait(false);
            }
        }

        private static bool MatchesGroupFilter(SignalARRRConnectionRegistration registration, string? groupName) {
            return string.IsNullOrWhiteSpace(groupName) ||
                registration.Groups.Any(g => string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesUserFilter(SignalARRRConnectionRegistration registration, string? userId) {
            return string.IsNullOrWhiteSpace(userId) ||
                string.Equals(registration.UserId, userId, StringComparison.Ordinal);
        }

        private static bool MatchesAttributeFilters(
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

        private static string NormalizeAttributeKey(string key) => key.ToUpperInvariant();

        private sealed class PendingInvoke {
            public Type ResultType { get; }
            public TaskCompletionSource<object?> Completion { get; } = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingInvoke(Type resultType) {
                ResultType = resultType;
            }
        }

        private sealed class PendingQueryInvoke {
            private readonly bool _singleResult;
            private int _remainingNodes;
            private int _completed;
            private readonly List<SignalARRRBackplaneInvokeResult> _results;
            private readonly object _syncRoot = new object();

            public Type ResultType { get; }
            public TaskCompletionSource<IReadOnlyList<SignalARRRBackplaneInvokeResult>> Completion { get; }
                = new TaskCompletionSource<IReadOnlyList<SignalARRRBackplaneInvokeResult>>(TaskCreationOptions.RunContinuationsAsynchronously);

            public PendingQueryInvoke(Type resultType, int remainingNodes, bool singleResult, IReadOnlyList<SignalARRRBackplaneInvokeResult> initialResults) {
                ResultType = resultType;
                _remainingNodes = remainingNodes;
                _singleResult = singleResult;
                _results = new List<SignalARRRBackplaneInvokeResult>(initialResults);

                if (_singleResult && _results.Count > 0) {
                    Completion.TrySetResult(new[] { _results[0] });
                } else if (_remainingNodes == 0) {
                    Completion.TrySetResult(_results);
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

            public void MarkCompleted() {
                if (Interlocked.Decrement(ref _remainingNodes) > 0) {
                    return;
                }

                if (Interlocked.Exchange(ref _completed, 1) == 1) {
                    return;
                }

                lock (_syncRoot) {
                    Completion.TrySetResult(_singleResult && _results.Count > 0
                        ? new[] { _results[0] }
                        : _results);
                }
            }
        }
    }
}
