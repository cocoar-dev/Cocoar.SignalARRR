using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Helper;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The Redis-compatible backplane: Pub/Sub for the transport, a hash per connection plus one
    /// set per hub, user, group and attribute key for the registry, and a TTL key per node for
    /// liveness. Correlation, heartbeat and sweep live in <see cref="SignalARRRBackplaneBase"/>.
    /// </summary>
    internal sealed class RedisSignalARRRBackplane : SignalARRRBackplaneBase {
        private readonly SignalARRRRedisBackplaneOptions _options;

        private ConnectionMultiplexer? _multiplexer;
        private ISubscriber? _subscriber;
        private IDatabase? _database;

        public RedisSignalARRRBackplane(
            SignalARRRRedisBackplaneOptions options,
            LocalSignalARRRBackplaneDispatcher localDispatcher,
            ILogger<RedisSignalARRRBackplane> logger)
            : base(options.NodeId, options.InvokeTimeout, options.HeartbeatInterval, options.NodeTimeout, localDispatcher, logger) {
            _options = options;
        }

        // --- Transport ---

        protected override async Task StartTransportAsync(CancellationToken cancellationToken) {
            if (_multiplexer != null) {
                return;
            }

            _multiplexer = await ConnectionMultiplexer.ConnectAsync(_options.ConnectionString).ConfigureAwait(false);
            _subscriber = _multiplexer.GetSubscriber();
            _database = _multiplexer.GetDatabase();

            var commandsSubscription = await _subscriber.SubscribeAsync(RedisChannel.Literal(GetCommandsChannel())).ConfigureAwait(false);
            commandsSubscription.OnMessage(channelMessage => {
                _ = HandleIncomingPayloadAsync((string)channelMessage.Message!);
            });

            var responsesSubscription = await _subscriber.SubscribeAsync(RedisChannel.Literal(GetResponsesChannel(NodeId))).ConfigureAwait(false);
            responsesSubscription.OnMessage(channelMessage => {
                _ = HandleIncomingPayloadAsync((string)channelMessage.Message!);
            });
        }

        protected override async Task StopTransportAsync(CancellationToken cancellationToken) {
            if (_subscriber != null) {
                await _subscriber.UnsubscribeAllAsync().ConfigureAwait(false);
            }

            _multiplexer?.Dispose();
            _multiplexer = null;
            _subscriber = null;
            _database = null;
        }

        protected override Task PublishCommandAsync(SignalARRRBackplaneEnvelope envelope) {
            return PublishAsync(GetCommandsChannel(), envelope);
        }

        protected override Task PublishResponseAsync(string targetNodeId, SignalARRRBackplaneEnvelope envelope) {
            return PublishAsync(GetResponsesChannel(targetNodeId), envelope);
        }

        private async Task PublishAsync(string channel, SignalARRRBackplaneEnvelope envelope) {
            if (_subscriber == null) {
                throw new InvalidOperationException("SignalARRR Redis backplane has not been started.");
            }

            var payload = JsonSerializer.Serialize(envelope, SerializerOptions);
            await _subscriber.PublishAsync(RedisChannel.Literal(channel), payload).ConfigureAwait(false);
        }

        public override async Task<TimeSpan?> PingAsync(CancellationToken cancellationToken = default) {
            if (_database == null) {
                return null;
            }

            try {
                return await _database.PingAsync().ConfigureAwait(false);
            } catch {
                return null;
            }
        }

        public override void Dispose() {
            base.Dispose();
            _multiplexer?.Dispose();
        }

        // --- Connection registry ---

        protected override async Task StoreRegistrationAsync(SignalARRRConnectionRegistration registration, CancellationToken cancellationToken) {
            if (_database == null) {
                throw new InvalidOperationException("SignalARRR Redis backplane has not been started.");
            }

            await SaveConnectionRegistrationAsync(registration).ConfigureAwait(false);
            await _database.SetAddAsync(GetNodeConnectionsKey(registration.NodeId), registration.ConnectionId).ConfigureAwait(false);
        }

        public override async Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken = default) {
            if (_database == null) {
                return;
            }

            var registration = await FindConnectionAsync(connectionId, cancellationToken).ConfigureAwait(false);
            if (registration == null) {
                await _database.KeyDeleteAsync(GetConnectionKey(connectionId)).ConfigureAwait(false);
                return;
            }

            await RemoveConnectionRegistrationAsync(registration).ConfigureAwait(false);
            await _database.SetRemoveAsync(GetNodeConnectionsKey(registration.NodeId), connectionId).ConfigureAwait(false);
        }

        protected override async Task<SignalARRRConnectionRegistration?> LoadRegistrationAsync(string connectionId, CancellationToken cancellationToken) {
            if (_database == null) {
                return null;
            }

            var payload = await _database.StringGetAsync(GetConnectionKey(connectionId)).ConfigureAwait(false);
            if (payload.IsNullOrEmpty) {
                return null;
            }

            return JsonSerializer.Deserialize<SignalARRRConnectionRegistration>((string)payload!, SerializerOptions);
        }

        public override async Task<IReadOnlyList<SignalARRRConnectionRegistration>> FindConnectionsAsync(
            Type hubType,
            string? groupName = null,
            string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null,
            CancellationToken cancellationToken = default) {
            if (_database == null) {
                return Array.Empty<SignalARRRConnectionRegistration>();
            }

            var hubTypeName = WireTypeName.From(hubType);
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

        public override async Task AddConnectionToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
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

        public override async Task RemoveConnectionFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
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

        // --- Node presence ---

        protected override async Task WriteHeartbeatAsync(CancellationToken cancellationToken) {
            if (_database == null) {
                return;
            }

            await _database.SetAddAsync(GetNodesKey(), NodeId).ConfigureAwait(false);
            await _database.StringSetAsync(GetNodeHeartbeatKey(NodeId), "1", _options.NodeTimeout).ConfigureAwait(false);
        }

        protected override async Task<bool> IsNodeAliveAsync(string nodeId, CancellationToken cancellationToken) {
            if (_database == null) {
                return false;
            }

            return await _database.KeyExistsAsync(GetNodeHeartbeatKey(nodeId)).ConfigureAwait(false);
        }

        protected override async Task<IReadOnlyList<string>> GetKnownNodeIdsAsync(CancellationToken cancellationToken) {
            if (_database == null) {
                return Array.Empty<string>();
            }

            var nodeIds = await _database.SetMembersAsync(GetNodesKey()).ConfigureAwait(false);
            return nodeIds.Select(v => (string?)v).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToArray();
        }

        protected override async Task CleanupNodeAsync(string nodeId, CancellationToken cancellationToken) {
            if (_database == null) {
                return;
            }

            var nodeConnectionsKey = GetNodeConnectionsKey(nodeId);
            var connectionIds = await _database.SetMembersAsync(nodeConnectionsKey).ConfigureAwait(false);
            if (connectionIds.Length > 0) {
                foreach (var connectionId in connectionIds.Select(v => (string?)v).Where(v => !string.IsNullOrWhiteSpace(v))) {
                    var payload = await _database.StringGetAsync(GetConnectionKey(connectionId!)).ConfigureAwait(false);
                    if (!payload.IsNullOrEmpty) {
                        var registration = JsonSerializer.Deserialize<SignalARRRConnectionRegistration>((string)payload!, SerializerOptions);
                        if (registration != null) {
                            await RemoveConnectionRegistrationAsync(registration).ConfigureAwait(false);
                            continue;
                        }
                    }

                    await _database.KeyDeleteAsync(GetConnectionKey(connectionId!)).ConfigureAwait(false);
                }
            }

            await _database.KeyDeleteAsync(nodeConnectionsKey).ConfigureAwait(false);
            await _database.KeyDeleteAsync(GetNodeHeartbeatKey(nodeId)).ConfigureAwait(false);
            await _database.SetRemoveAsync(GetNodesKey(), nodeId).ConfigureAwait(false);
        }

        // --- Keys ---

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

        // --- Registration storage ---

        private async Task SaveConnectionRegistrationAsync(SignalARRRConnectionRegistration registration) {
            if (_database == null) {
                return;
            }

            var existingPayload = await _database.StringGetAsync(GetConnectionKey(registration.ConnectionId)).ConfigureAwait(false);
            if (!existingPayload.IsNullOrEmpty) {
                var existingRegistration = JsonSerializer.Deserialize<SignalARRRConnectionRegistration>((string)existingPayload!, SerializerOptions);
                if (existingRegistration != null) {
                    await RemoveConnectionIndexesAsync(existingRegistration).ConfigureAwait(false);
                }
            }

            await _database.StringSetAsync(
                GetConnectionKey(registration.ConnectionId),
                JsonSerializer.Serialize(registration, SerializerOptions)).ConfigureAwait(false);
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
    }
}
