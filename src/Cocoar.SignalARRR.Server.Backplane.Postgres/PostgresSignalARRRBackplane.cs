using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Helper;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The PostgreSQL backplane: <c>LISTEN</c>/<c>NOTIFY</c> for the transport, one row per
    /// connection for the registry, and a <c>last_seen</c> timestamp per node for liveness.
    /// Correlation, heartbeat and sweep live in <see cref="SignalARRRBackplaneBase"/>.
    /// </summary>
    /// <remarks>
    /// Two channels: <c>{schema}_commands</c>, which every node listens on, and
    /// <c>{schema}_responses</c>, shared as well — a response names its target node in the
    /// envelope and the others drop it. Per-node channels are not an option, because channel
    /// names are identifiers capped at 63 bytes and node ids are not.
    /// <para>
    /// A notification payload must stay under 8000 bytes. With catch-up on (the default) every
    /// envelope is written to the <c>messages</c> table and the notification carries only its
    /// row id plus origin and target (<see cref="PostgresMessageReference"/>); the receiver
    /// fetches the row. With catch-up off, an envelope that fits is sent inline and only larger
    /// ones take the table. The <c>INSERT</c> and the <c>NOTIFY</c> share one transaction, and
    /// Postgres delivers notifications after commit, so the row is always visible by the time it
    /// is looked up. Receivers never delete rows; a retention purge on every heartbeat does.
    /// </para>
    /// <para>
    /// The listener is one dedicated, unpooled connection per node. If it drops, the node
    /// reconnects with backoff. Without catch-up it is deaf in between — the same
    /// transient-delivery contract the Redis backplane has during a Pub/Sub reconnect. With
    /// catch-up, the node remembers the id of the last message it saw and, once resubscribed,
    /// reads everything past that cursor addressed to it before resuming live delivery; an
    /// outage longer than the retention is reported as a gap rather than passed over silently.
    /// Notifications are consumed in arrival order by a single reader, which resolves
    /// table-backed payloads before handing each envelope on, so ordering matches the Redis
    /// backplane, and replayed rows are handed on in id order ahead of what arrived live.
    /// </para>
    /// <para>
    /// Liveness is judged on the database clock only (<c>now()</c> on write and on compare), so
    /// nodes need not agree on the time.
    /// </para>
    /// </remarks>
    internal sealed class PostgresSignalARRRBackplane : SignalARRRBackplaneBase {
        /// <summary>
        /// Envelopes up to this many UTF-8 bytes travel inside the notification when catch-up is
        /// off; larger ones go through the <c>messages</c> table. Postgres rejects payloads of
        /// 8000 bytes or more, and the margin keeps a byte-counting mistake from becoming a failed
        /// publish.
        /// </summary>
        internal const int MaxInlinePayloadBytes = 7500;

        /// <summary>
        /// The <c>application_name</c> of the listener session, so it can be told apart in
        /// <c>pg_stat_activity</c>: <c>signalarrr-backplane-listener:{nodeId}</c>, truncated by
        /// Postgres to 63 bytes.
        /// </summary>
        internal static string ListenerApplicationName(string nodeId) => $"signalarrr-backplane-listener:{nodeId}";

        private static readonly TimeSpan ListenerReconnectMinDelay = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan ListenerReconnectMaxDelay = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ListenerStartupTimeout = TimeSpan.FromSeconds(30);

        /// <summary>Ids replayed by a catch-up are remembered so their live notifications, if any arrive, are dropped; this bounds the set.</summary>
        private const int MaxRememberedReplayedIds = 10_000;

        private readonly SignalARRRPostgresBackplaneOptions _options;
        private readonly string _listenerConnectionString;
        private readonly string _commandsChannel;
        private readonly string _responsesChannel;
        private readonly Sql _sql;
        private readonly HashSet<long> _replayedIds = new HashSet<long>();
        private readonly object _replayedIdsLock = new object();

        private NpgsqlDataSource? _dataSource;
        private Channel<IncomingMessage>? _incoming;
        private CancellationTokenSource? _listenerCts;
        private Task? _listenerTask;
        private Task? _consumerTask;
        private TaskCompletionSource<bool>? _listenerReady;
        private Exception? _lastListenerError;
        private DateTime? _listenerLostAtUtc;
        private volatile bool _listening;

        /// <summary>The highest message id this node has seen, live or replayed; where catch-up resumes from.</summary>
        private long _cursor;

        public PostgresSignalARRRBackplane(
            SignalARRRPostgresBackplaneOptions options,
            LocalSignalARRRBackplaneDispatcher localDispatcher,
            ClusterSubjectRegistry clusterSubjects,
            ILogger<PostgresSignalARRRBackplane> logger)
            : base(options.NodeId, options.InvokeTimeout, options.HeartbeatInterval, options.NodeTimeout, localDispatcher, clusterSubjects, logger) {
            _options = options;
            _commandsChannel = $"{options.Schema}_commands";
            _responsesChannel = $"{options.Schema}_responses";
            _sql = new Sql(options.Schema, _commandsChannel, _responsesChannel);

            // The listener holds its connection for the lifetime of the node, so it must not
            // come from the pool, and the periodic keepalive is what notices a silent drop while
            // the connection sits in WaitAsync with nothing else ever written to it.
            var listenerBuilder = new NpgsqlConnectionStringBuilder(options.ConnectionString) {
                Pooling = false,
                KeepAlive = 30,
                ApplicationName = ListenerApplicationName(options.NodeId)
            };
            _listenerConnectionString = listenerBuilder.ConnectionString;
        }

        // --- Transport ---

        protected override async Task StartTransportAsync(CancellationToken cancellationToken) {
            if (_dataSource != null) {
                return;
            }

            _dataSource = NpgsqlDataSource.Create(_options.ConnectionString);

            if (_options.AutoCreateSchema) {
                await EnsureSchemaAsync(cancellationToken).ConfigureAwait(false);
            } else {
                await EnsureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_options.CatchUp) {
                // A fresh node has nothing to catch up on: it serves no connections yet, so what
                // was published before it subscribed cannot concern it. Start at the current end.
                _cursor = await ReadLatestMessageIdAsync(cancellationToken).ConfigureAwait(false);
            }

            _incoming = Channel.CreateUnbounded<IncomingMessage>(new UnboundedChannelOptions { SingleReader = true });
            _listenerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _listenerCts = new CancellationTokenSource();
            _listenerTask = RunListenerLoopAsync(_incoming.Writer, _listenerCts.Token);
            _consumerTask = RunConsumerAsync(_incoming.Reader, _listenerCts.Token);

            // Not cluster-aware until the subscription is up: a broadcast published in between
            // would be lost, and the Redis backplane awaits its subscription too. Bounded, because
            // an endpoint that accepts queries but not LISTEN — a transaction-pooling PgBouncer,
            // a read replica — would otherwise hang startup rather than fail it.
            var ready = await Task.WhenAny(_listenerReady.Task, Task.Delay(ListenerStartupTimeout, cancellationToken)).ConfigureAwait(false);
            if (ready != _listenerReady.Task) {
                cancellationToken.ThrowIfCancellationRequested();
                await StopTransportAsync(CancellationToken.None).ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"SignalARRR Postgres backplane could not subscribe to notifications within {ListenerStartupTimeout}. " +
                    "LISTEN needs a direct connection to the primary; a transaction-pooling PgBouncer or a read replica cannot serve it.",
                    _lastListenerError);
            }
        }

        protected override async Task StopTransportAsync(CancellationToken cancellationToken) {
            if (_listenerCts != null) {
                _listenerCts.Cancel();
                _incoming?.Writer.TryComplete();

                if (_listenerTask != null) {
                    try { await _listenerTask.ConfigureAwait(false); } catch { }
                }
                if (_consumerTask != null) {
                    try { await _consumerTask.ConfigureAwait(false); } catch { }
                }

                _listenerCts.Dispose();
                _listenerCts = null;
                _listenerTask = null;
                _consumerTask = null;
                _incoming = null;
            }

            if (_dataSource != null) {
                await _dataSource.DisposeAsync().ConfigureAwait(false);
                _dataSource = null;
            }
        }

        protected override Task PublishCommandAsync(SignalARRRBackplaneEnvelope envelope) {
            return PublishAsync(_commandsChannel, envelope);
        }

        protected override Task PublishResponseAsync(string targetNodeId, SignalARRRBackplaneEnvelope envelope) {
            return PublishAsync(_responsesChannel, envelope);
        }

        private async Task PublishAsync(string channel, SignalARRRBackplaneEnvelope envelope) {
            var dataSource = _dataSource ?? throw new InvalidOperationException("SignalARRR Postgres backplane has not been started.");
            var payload = JsonSerializer.Serialize(envelope, SerializerOptions);

            if (!_options.CatchUp && Encoding.UTF8.GetByteCount(payload) <= MaxInlinePayloadBytes) {
                await using var command = dataSource.CreateCommand(_sql.NotifyInline);
                command.Parameters.Add(Text(channel));
                command.Parameters.Add(Text(payload));
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                return;
            }

            await using var referenceCommand = dataSource.CreateCommand(_sql.NotifyReference);
            referenceCommand.Parameters.Add(Text(channel));
            referenceCommand.Parameters.Add(Text(payload));
            referenceCommand.Parameters.Add(Text(envelope.OriginNodeId));
            referenceCommand.Parameters.Add(Text(envelope.TargetNodeId));
            await referenceCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public override async Task<TimeSpan?> PingAsync(CancellationToken cancellationToken = default) {
            // A node whose listener is down can query the database but cannot hear the cluster,
            // which for a backplane is unreachable: the health check must not report it fine.
            var dataSource = _dataSource;
            if (dataSource == null || !_listening) {
                return null;
            }

            try {
                var stopwatch = Stopwatch.StartNew();
                await using var command = dataSource.CreateCommand(_sql.Ping);
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return stopwatch.Elapsed;
            } catch {
                return null;
            }
        }

        public override void Dispose() {
            base.Dispose();
            _listenerCts?.Dispose();
            _dataSource?.Dispose();
        }

        // --- Listener ---

        private async Task RunListenerLoopAsync(ChannelWriter<IncomingMessage> incoming, CancellationToken cancellationToken) {
            var delay = ListenerReconnectMinDelay;
            var subscribedBefore = false;

            while (!cancellationToken.IsCancellationRequested) {
                try {
                    await using var connection = new NpgsqlConnection(_listenerConnectionString);
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                    void OnNotification(object sender, NpgsqlNotificationEventArgs e) => incoming.TryWrite(IncomingMessage.Live(e.Payload));
                    connection.Notification += OnNotification;

                    try {
                        await using (var listen = new NpgsqlCommand(_sql.Listen, connection)) {
                            await listen.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                        if (subscribedBefore) {
                            SignalARRRServerTelemetry.BackplaneListenerReconnects.Add(1);

                            // Subscribed first, then read the backlog: whatever commits from here on
                            // arrives as a live notification, so nothing falls between the two. Rows
                            // that show up in both are recognized by id and handed on once.
                            if (_options.CatchUp) {
                                await CatchUpAsync(incoming, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        subscribedBefore = true;
                        _listenerLostAtUtc = null;
                        _listening = true;
                        _listenerReady?.TrySetResult(true);
                        delay = ListenerReconnectMinDelay;

                        while (!cancellationToken.IsCancellationRequested) {
                            await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
                        }
                    } finally {
                        _listening = false;
                        connection.Notification -= OnNotification;
                    }
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    return;
                } catch (Exception ex) {
                    _lastListenerError = ex;
                    _listenerLostAtUtc ??= DateTime.UtcNow;
                    Logger.LogError(ex,
                        _options.CatchUp
                            ? "SignalARRR Postgres backplane node {NodeId} lost its LISTEN connection; cluster messages will be replayed once it reconnects. Reconnecting in {Delay}."
                            : "SignalARRR Postgres backplane node {NodeId} lost its LISTEN connection; cluster messages until it reconnects are lost. Reconnecting in {Delay}.",
                        NodeId, delay);

                    try {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    } catch (OperationCanceledException) {
                        return;
                    }

                    delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, ListenerReconnectMaxDelay.Ticks));
                }
            }
        }

        /// <summary>
        /// Reads every message past this node's cursor that is addressed to it and queues the rows
        /// ahead of the live notifications, in id order.
        /// </summary>
        /// <remarks>
        /// Runs on a pooled connection, not the listener: a command on the listener connection
        /// would also drain the notifications already queued there, interleaving live messages
        /// with the backlog. The cursor is an id, and identity values are not assigned in commit
        /// order, so a publish that was mid-transaction at the exact moment the subscription
        /// dropped and committed after the reconnect could carry an id below the cursor. Its live
        /// notification then still arrives, because the subscription is back by the time it
        /// commits — and its id is not in the replayed set, so it is handed on normally.
        /// </remarks>
        private async Task CatchUpAsync(ChannelWriter<IncomingMessage> incoming, CancellationToken cancellationToken) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return;
            }

            var outage = _listenerLostAtUtc.HasValue ? DateTime.UtcNow - _listenerLostAtUtc.Value : TimeSpan.Zero;
            var replayed = 0;

            await using (var command = dataSource.CreateCommand(_sql.CatchUp)) {
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = _cursor });
                command.Parameters.Add(Text(NodeId));

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                    incoming.TryWrite(IncomingMessage.Replayed(reader.GetInt64(0), reader.GetString(1)));
                    replayed++;
                }
            }

            SignalARRRServerTelemetry.BackplaneMessagesReplayed.Add(replayed);

            if (outage > _options.MessageRetention) {
                // The purge has run in the meantime; whatever was published before the oldest
                // surviving row is gone. Said out loud, because a silent gap is the failure mode
                // catch-up exists to remove.
                SignalARRRServerTelemetry.BackplaneCatchUpGaps.Add(1);
                Logger.LogWarning(
                    "SignalARRR Postgres backplane node {NodeId} was unsubscribed for {Outage}, longer than the message retention of {Retention}; {Replayed} message(s) were replayed, older ones are lost.",
                    NodeId, outage, _options.MessageRetention, replayed);
            } else {
                Logger.LogInformation(
                    "SignalARRR Postgres backplane node {NodeId} resubscribed after {Outage} and replayed {Replayed} message(s).",
                    NodeId, outage, replayed);
            }
        }

        /// <summary>
        /// Hands envelopes on in the order they were queued: a replayed backlog first, then live
        /// notifications as they arrive. Each handler is started and not awaited, exactly like the
        /// Redis subscription callback, so a slow remote invoke does not hold up the messages
        /// behind it — but the hand-off itself is sequential, which is what keeps two consecutive
        /// pushes to one client in order.
        /// </summary>
        private async Task RunConsumerAsync(ChannelReader<IncomingMessage> incoming, CancellationToken cancellationToken) {
            try {
                await foreach (var message in incoming.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                    string? envelopeJson;
                    try {
                        envelopeJson = message.ReplayedJson != null
                            ? AcceptReplayed(message)
                            : await ResolveLiveAsync(message.NotificationPayload!, cancellationToken).ConfigureAwait(false);
                    } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                        return;
                    } catch (Exception ex) {
                        Logger.LogError(ex, "SignalARRR Postgres backplane could not load a table-backed message; it is dropped.");
                        continue;
                    }

                    if (envelopeJson != null) {
                        _ = HandleIncomingPayloadAsync(envelopeJson);
                    }
                }
            } catch (OperationCanceledException) {
                // Shutdown.
            }
        }

        private string AcceptReplayed(IncomingMessage message) {
            lock (_replayedIdsLock) {
                if (_replayedIds.Count >= MaxRememberedReplayedIds) {
                    _replayedIds.Clear();
                }

                _replayedIds.Add(message.Id);
            }

            AdvanceCursor(message.Id);
            return message.ReplayedJson!;
        }

        private async Task<string?> ResolveLiveAsync(string payload, CancellationToken cancellationToken) {
            if (!PostgresMessageReference.IsReference(payload)) {
                return payload.Length == 0 ? null : payload;
            }

            var reference = PostgresMessageReference.TryParse(payload);
            if (reference == null) {
                Logger.LogWarning("SignalARRR Postgres backplane received a notification it cannot parse; it is dropped.");
                return null;
            }

            AdvanceCursor(reference.Id);

            // Already handed on by a catch-up that overlapped this notification's commit.
            lock (_replayedIdsLock) {
                if (_replayedIds.Remove(reference.Id)) {
                    return null;
                }
            }

            // The same filter the envelope handler applies, evaluated before the fetch so a node
            // that would drop the envelope anyway does not read it first. A node never sends a
            // response to itself — it answers only requests from other nodes — so its own
            // messages are always safe to skip here.
            if (string.Equals(reference.OriginNodeId, NodeId, StringComparison.Ordinal)) {
                return null;
            }

            if (reference.TargetNodeId != null && !string.Equals(reference.TargetNodeId, NodeId, StringComparison.Ordinal)) {
                return null;
            }

            var dataSource = _dataSource;
            if (dataSource == null) {
                return null;
            }

            await using var command = dataSource.CreateCommand(_sql.LoadMessage);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = reference.Id });
            var envelopeJson = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            if (envelopeJson == null) {
                Logger.LogWarning(
                    "SignalARRR Postgres backplane message {MessageId} from node {OriginNodeId} was already purged; it is dropped.",
                    reference.Id, reference.OriginNodeId);
            }

            return envelopeJson;
        }

        private void AdvanceCursor(long id) {
            if (id > _cursor) {
                _cursor = id;
            }
        }

        private async Task<long> ReadLatestMessageIdAsync(CancellationToken cancellationToken) {
            await using var command = _dataSource!.CreateCommand(_sql.LatestMessageId);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is long id ? id : 0;
        }

        // --- Schema ---

        private async Task EnsureSchemaAsync(CancellationToken cancellationToken) {
            var dataSource = _dataSource!;
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // Two nodes starting at once would both run the DDL; IF NOT EXISTS does not make
            // that race-free (the catalog insert can still collide). The lock serializes them
            // and is released with the transaction.
            await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext($1))", connection, transaction)) {
                lockCommand.Parameters.Add(Text($"signalarrr-backplane:{_options.Schema}"));
                await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var ddl = new NpgsqlCommand(SignalARRRPostgresBackplaneSchema.GetCreateScript(_options.Schema), connection, transaction)) {
                await ddl.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task EnsureSchemaExistsAsync(CancellationToken cancellationToken) {
            await using var command = _dataSource!.CreateCommand("SELECT to_regclass($1)");
            command.Parameters.Add(Text($"{SignalARRRPostgresBackplaneSchema.QuoteIdentifier(_options.Schema)}.connections"));
            var found = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (found == null || found is DBNull) {
                throw new InvalidOperationException(
                    $"SignalARRR Postgres backplane tables are missing in schema '{_options.Schema}' and AutoCreateSchema is off. " +
                    "Apply SignalARRRPostgresBackplaneSchema.GetCreateScript() through your migrations first.");
            }
        }

        // --- Connection registry ---

        protected override async Task StoreRegistrationAsync(SignalARRRConnectionRegistration registration, CancellationToken cancellationToken) {
            var dataSource = _dataSource ?? throw new InvalidOperationException("SignalARRR Postgres backplane has not been started.");

            await using var command = dataSource.CreateCommand(_sql.UpsertConnection);
            command.Parameters.Add(Text(registration.ConnectionId));
            command.Parameters.Add(Text(registration.NodeId));
            command.Parameters.Add(Text(registration.HubType));
            command.Parameters.Add(Text(registration.UserId));
            command.Parameters.Add(TextArray(registration.Groups));
            command.Parameters.Add(TextArray(registration.Attributes.Select(a => NormalizeAttributeKey(a.Key)).Distinct(StringComparer.Ordinal).ToArray()));
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(registration.Attributes, SerializerOptions) });
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public override async Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken = default) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return;
            }

            await using var command = dataSource.CreateCommand(_sql.DeleteConnection);
            command.Parameters.Add(Text(connectionId));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override async Task<SignalARRRConnectionRegistration?> LoadRegistrationAsync(string connectionId, CancellationToken cancellationToken) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return null;
            }

            await using var command = dataSource.CreateCommand(_sql.LoadConnection);
            command.Parameters.Add(Text(connectionId));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                return null;
            }

            return ReadRegistration(reader);
        }

        public override async Task<IReadOnlyList<SignalARRRConnectionRegistration>> FindConnectionsAsync(
            Type hubType,
            string? groupName = null,
            string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null,
            CancellationToken cancellationToken = default) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return Array.Empty<SignalARRRConnectionRegistration>();
            }

            var attributeKeys = attributeFilters == null || attributeFilters.Count == 0
                ? null
                : attributeFilters.Select(f => NormalizeAttributeKey(f.Key)).Distinct(StringComparer.Ordinal).ToArray();

            // Dead nodes are excluded by the join rather than swept here; the heartbeat loop
            // removes their rows within one interval. The SQL narrows by hub, user, group and
            // attribute key; the value patterns of attribute filters are matched in memory, the
            // same way the Redis backplane does after its set intersection.
            await using var command = dataSource.CreateCommand(_sql.FindConnections);
            command.Parameters.Add(Text(WireTypeName.From(hubType)));
            command.Parameters.Add(Text(string.IsNullOrWhiteSpace(userId) ? null : userId));
            command.Parameters.Add(Text(string.IsNullOrWhiteSpace(groupName) ? null : groupName));
            command.Parameters.Add(TextArray(attributeKeys));
            command.Parameters.Add(Interval(NodeTimeout));

            var registrations = new List<SignalARRRConnectionRegistration>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                var registration = ReadRegistration(reader);
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
            var dataSource = _dataSource;
            if (dataSource == null) {
                return;
            }

            await using var command = dataSource.CreateCommand(_sql.AddConnectionToGroup);
            command.Parameters.Add(Text(connectionId));
            command.Parameters.Add(Text(groupName));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        public override async Task RemoveConnectionFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return;
            }

            await using var command = dataSource.CreateCommand(_sql.RemoveConnectionFromGroup);
            command.Parameters.Add(Text(connectionId));
            command.Parameters.Add(Text(groupName));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private SignalARRRConnectionRegistration ReadRegistration(NpgsqlDataReader reader) {
            var attributesJson = reader.GetString(5);
            return new SignalARRRConnectionRegistration {
                ConnectionId = reader.GetString(0),
                NodeId = reader.GetString(1),
                HubType = reader.GetString(2),
                UserId = reader.IsDBNull(3) ? null : reader.GetString(3),
                Groups = reader.GetFieldValue<string[]>(4),
                Attributes = JsonSerializer.Deserialize<SignalARRRConnectionAttribute[]>(attributesJson, SerializerOptions) ?? Array.Empty<SignalARRRConnectionAttribute>()
            };
        }

        // --- Node presence ---

        protected override async Task WriteHeartbeatAsync(CancellationToken cancellationToken) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return;
            }

            await using var command = dataSource.CreateCommand(_sql.UpsertHeartbeat);
            command.Parameters.Add(Text(NodeId));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override async Task<bool> IsNodeAliveAsync(string nodeId, CancellationToken cancellationToken) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return false;
            }

            await using var command = dataSource.CreateCommand(_sql.IsNodeAlive);
            command.Parameters.Add(Text(nodeId));
            command.Parameters.Add(Interval(NodeTimeout));
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is bool alive && alive;
        }

        protected override async Task<IReadOnlyList<string>> GetKnownNodeIdsAsync(CancellationToken cancellationToken) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return Array.Empty<string>();
            }

            var nodeIds = new List<string>();
            await using var command = dataSource.CreateCommand(_sql.SelectNodeIds);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) {
                nodeIds.Add(reader.GetString(0));
            }

            return nodeIds;
        }

        protected override async Task CleanupNodeAsync(string nodeId, CancellationToken cancellationToken) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return;
            }

            await using var batch = dataSource.CreateBatch();
            var deleteConnections = new NpgsqlBatchCommand(_sql.DeleteNodeConnections);
            deleteConnections.Parameters.Add(Text(nodeId));
            batch.BatchCommands.Add(deleteConnections);

            var deleteNode = new NpgsqlBatchCommand(_sql.DeleteNode);
            deleteNode.Parameters.Add(Text(nodeId));
            batch.BatchCommands.Add(deleteNode);

            await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override async Task RunMaintenanceAsync(CancellationToken cancellationToken) {
            var dataSource = _dataSource;
            if (dataSource == null) {
                return;
            }

            await using var command = dataSource.CreateCommand(_sql.PurgeMessages);
            command.Parameters.Add(Interval(_options.MessageRetention));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // --- Parameters ---

        private static NpgsqlParameter Text(string? value) {
            return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)value ?? DBNull.Value };
        }

        private static NpgsqlParameter TextArray(string[]? value) {
            return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = (object?)value ?? DBNull.Value };
        }

        private static NpgsqlParameter Interval(TimeSpan value) {
            return new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Interval, Value = value };
        }

        /// <summary>What the consumer takes off the queue: a live notification payload, or a row replayed by catch-up.</summary>
        private readonly struct IncomingMessage {
            public string? NotificationPayload { get; }
            public long Id { get; }
            public string? ReplayedJson { get; }

            private IncomingMessage(string? notificationPayload, long id, string? replayedJson) {
                NotificationPayload = notificationPayload;
                Id = id;
                ReplayedJson = replayedJson;
            }

            public static IncomingMessage Live(string payload) => new IncomingMessage(payload, 0, null);

            public static IncomingMessage Replayed(long id, string json) => new IncomingMessage(null, id, json);
        }

        /// <summary>Every statement, with the schema baked in once.</summary>
        private sealed class Sql {
            public string Listen { get; }
            public string NotifyInline { get; }
            public string NotifyReference { get; }
            public string LoadMessage { get; }
            public string LatestMessageId { get; }
            public string CatchUp { get; }
            public string PurgeMessages { get; }
            public string Ping { get; }
            public string UpsertConnection { get; }
            public string DeleteConnection { get; }
            public string LoadConnection { get; }
            public string FindConnections { get; }
            public string AddConnectionToGroup { get; }
            public string RemoveConnectionFromGroup { get; }
            public string UpsertHeartbeat { get; }
            public string IsNodeAlive { get; }
            public string SelectNodeIds { get; }
            public string DeleteNodeConnections { get; }
            public string DeleteNode { get; }

            public Sql(string schema, string commandsChannel, string responsesChannel) {
                var s = SignalARRRPostgresBackplaneSchema.QuoteIdentifier(schema);

                Listen = $"LISTEN {SignalARRRPostgresBackplaneSchema.QuoteIdentifier(commandsChannel)}; LISTEN {SignalARRRPostgresBackplaneSchema.QuoteIdentifier(responsesChannel)};";

                NotifyInline = "SELECT pg_notify($1, $2)";

                // INSERT and NOTIFY in one statement, hence one transaction: the notification is
                // delivered after commit, when the row is guaranteed visible to the fetch.
                NotifyReference =
                    $"WITH m AS (INSERT INTO {s}.messages (payload, origin_node_id, target_node_id) VALUES ($2, $3, $4) RETURNING id) " +
                    "SELECT pg_notify($1, json_build_array(m.id, $3::text, $4::text)::text) FROM m";

                LoadMessage = $"SELECT payload FROM {s}.messages WHERE id = $1";
                LatestMessageId = $"SELECT COALESCE(max(id), 0) FROM {s}.messages";
                CatchUp =
                    $"SELECT id, payload FROM {s}.messages " +
                    "WHERE id > $1 AND origin_node_id <> $2 AND (target_node_id IS NULL OR target_node_id = $2) " +
                    "ORDER BY id";
                PurgeMessages = $"DELETE FROM {s}.messages WHERE created_at < now() - $1::interval";
                Ping = "SELECT 1";

                UpsertConnection =
                    $"INSERT INTO {s}.connections (connection_id, node_id, hub_type, user_id, groups, attribute_keys, attributes, registered_at) " +
                    "VALUES ($1, $2, $3, $4, $5, $6, $7, now()) " +
                    "ON CONFLICT (connection_id) DO UPDATE SET node_id = EXCLUDED.node_id, hub_type = EXCLUDED.hub_type, user_id = EXCLUDED.user_id, " +
                    "groups = EXCLUDED.groups, attribute_keys = EXCLUDED.attribute_keys, attributes = EXCLUDED.attributes, registered_at = now()";

                DeleteConnection = $"DELETE FROM {s}.connections WHERE connection_id = $1";

                LoadConnection =
                    $"SELECT connection_id, node_id, hub_type, user_id, groups, attributes FROM {s}.connections WHERE connection_id = $1";

                FindConnections =
                    "SELECT c.connection_id, c.node_id, c.hub_type, c.user_id, c.groups, c.attributes " +
                    $"FROM {s}.connections c " +
                    $"JOIN {s}.nodes n ON n.node_id = c.node_id AND n.last_seen > now() - $5::interval " +
                    "WHERE c.hub_type = $1 " +
                    "AND ($2::text IS NULL OR c.user_id = $2) " +
                    "AND ($3::text IS NULL OR $3 = ANY (c.groups)) " +
                    "AND ($4::text[] IS NULL OR c.attribute_keys @> $4)";

                AddConnectionToGroup =
                    $"UPDATE {s}.connections SET groups = array_append(groups, $2) WHERE connection_id = $1 AND NOT ($2 = ANY (groups))";

                RemoveConnectionFromGroup =
                    $"UPDATE {s}.connections SET groups = array_remove(groups, $2) WHERE connection_id = $1";

                UpsertHeartbeat =
                    $"INSERT INTO {s}.nodes (node_id, last_seen) VALUES ($1, now()) ON CONFLICT (node_id) DO UPDATE SET last_seen = now()";

                IsNodeAlive =
                    $"SELECT EXISTS (SELECT 1 FROM {s}.nodes WHERE node_id = $1 AND last_seen > now() - $2::interval)";

                SelectNodeIds = $"SELECT node_id FROM {s}.nodes";
                DeleteNodeConnections = $"DELETE FROM {s}.connections WHERE node_id = $1";
                DeleteNode = $"DELETE FROM {s}.nodes WHERE node_id = $1";
            }
        }
    }
}
