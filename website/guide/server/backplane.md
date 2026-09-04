# Backplane & Clustering

SignalARRR runs in pure in-memory single-node mode by default. If you never configure a backplane, all client tracking, groups, and filters stay local to the current process.

For multi-node deployments, add one of the two backplane packages: Redis-compatible, or PostgreSQL.

## Choosing a backplane

Both packages provide the same cluster behaviour — every operation listed under
[Supported distributed operations](#supported-distributed-operations) works identically on either.
They differ in what they ask of your infrastructure and in how much traffic they carry.

| | `Backplane.Redis` | `Backplane.Postgres` |
|---|---|---|
| Needs | A Redis, Valkey or Garnet instance | The PostgreSQL primary your application already uses |
| Transport | Pub/Sub, in memory | `LISTEN`/`NOTIFY`, plus a table for envelopes above 8 kB |
| Registry | Hash and sets per key | One row per connection, one per node |
| Throughput ceiling | Hundreds of thousands of cross-node messages per second | Low thousands per second; `NOTIFY` serializes at commit and large envelopes cost a row write |
| Latency | Sub-millisecond | Single-digit milliseconds |
| Housekeeping | Key TTLs | Sweeps run by the nodes themselves |
| Best for | High realtime volume, or Redis already in the stack | Deployments whose only stateful dependency is Postgres |

Pick Redis when cross-node volume is a design parameter. Pick Postgres when you would otherwise be
adding a second stateful service — its ceiling is two orders of magnitude above what an
application-level backplane sees in practice, and one fewer service to run, back up, monitor and
secure is a real saving. Switching later is a package reference and one registration call; nothing
else in the application changes.

## Redis-compatible backplane

The backplane ships separately, so that single-node applications — the majority — do not carry
`StackExchange.Redis` and its transitive closure for a feature they never switch on:

```xml
<PackageReference Include="Cocoar.SignalARRR.Server.Backplane.Redis" Version="5.*" />
```

::: warning Moved out of the server package in 5.0
In 4.x this came with `Cocoar.SignalARRR.Server`. If you use `AddSignalARRRRedisBackplane`, add the
package reference above — nothing else changes, the method and its options are unchanged and stay in
the same namespace.
:::

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddSignalARRR(b =>
    b.AddServerMethodsFrom(typeof(ChatHub).Assembly));

builder.Services.AddSignalARRRRedisBackplane(options => options
    .WithConnectionString("localhost:6379,abortConnect=false")
    .WithChannelPrefix("chat-prod")
    .WithNodeId($"{Environment.MachineName}-api-1"));
```

The provider is **Redis-compatible**, not Redis-specific. It works with:

- Redis
- Valkey
- Garnet

`WithChannelPrefix` isolates one application's keys and channels from another's on a shared
instance.

## PostgreSQL backplane

```xml
<PackageReference Include="Cocoar.SignalARRR.Server.Backplane.Postgres" Version="5.*" />
```

```csharp
builder.Services.AddSignalARRRPostgresBackplane(options => options
    .WithConnectionString(builder.Configuration.GetConnectionString("Default")!)
    .WithSchema("signalarrr")
    .WithNodeId($"{Environment.MachineName}-api-1"));
```

That is the whole setup. On startup the backplane creates its schema and tables if they are
missing, registers the node, and subscribes to its notification channels.

### How it works

Every node holds one dedicated connection in `LISTEN` on two channels, `{schema}_commands` and
`{schema}_responses`. A broadcast, a targeted call or a cluster query is a `NOTIFY` on the commands
channel; the reply to a call travels back on the responses channel. Connection registrations and
node heartbeats are rows in the `connections` and `nodes` tables, and liveness is judged on the
database clock alone, so the nodes need not agree on the time.

A notification payload must stay under 8 kB. An envelope that fits is sent inline. A larger one —
a push carrying a sizeable object graph, a cluster query result with many items — is written to the
unlogged `messages` table in the same transaction as the `NOTIFY`, which then carries only the row
id; the receiving node reads the row and drops it from consideration once a retention sweep removes
it two minutes later. That extra write and read is the price of the larger payload, and it is why
the throughput ceiling above is lower than Redis's. The boundary is invisible to your code: both
paths deliver the same envelope, in the same order.

### Schema and permissions

The backplane needs `CREATE` on the database the first time it starts, to create the schema and its
tables. Afterwards, `SELECT`, `INSERT`, `UPDATE` and `DELETE` on those tables and the ability to
`LISTEN` and `NOTIFY` are enough.

If your database role does not have `CREATE`, or you apply every schema change through migrations,
switch automatic creation off and run the script yourself:

```csharp
builder.Services.AddSignalARRRPostgresBackplane(options => options
    .WithConnectionString(connectionString)
    .WithAutoCreateSchema(false));

// The DDL, for your migration tooling. Idempotent: every statement is IF NOT EXISTS.
var ddl = SignalARRRPostgresBackplaneSchema.GetCreateScript("signalarrr");
```

With automatic creation off, startup fails with a clear message if the tables are missing.

The schema is the unit of isolation: it names the tables *and* the notification channels, so two
applications sharing one database must use two schemas. Schema names are lowercase letters, digits
and underscores, at most 50 characters — the channel names are derived from it and Postgres caps
identifiers at 63 bytes.

### What to know before running it

- **Primary only.** `NOTIFY` is not replicated, so the connection string must point at the
  primary, not a read replica.
- **Direct connection for the listener.** `LISTEN` needs a session that stays open. A
  transaction-pooling PgBouncer cannot provide one; point the backplane at Postgres directly, or use
  session pooling for it. Startup fails within 30 seconds if the subscription cannot be
  established, naming this as the likely cause.
- **Connections per node.** One long-lived listener connection, plus ordinary pooled connections
  for publishing and registry lookups — count them when sizing `max_connections`.
- **Transient delivery, as with Redis.** If the listener connection drops, the node reconnects
  with backoff and misses whatever was published in between. The registry lives in tables and loses
  nothing. The health check reports the node unhealthy while its listener is down.
- **Sweeps instead of TTLs.** Node timeouts, orphaned registrations and message retention are
  cleaned up by the nodes on every heartbeat; there is nothing for you to schedule.

::: info Custom backplanes are not a supported extension point
The contracts behind this — `ISignalARRRBackplane` and `ISignalARRRConnectionRegistry` — are
internal, so a backplane for NATS or RabbitMQ cannot be plugged in from outside the library today.
They are registered with `TryAddSingleton`, which makes the order of `AddSignalARRR` and the
backplane registration irrelevant; read that as order-independence, not as an invitation.

Opening them up would mean freezing the inter-node envelope as public API, so it is a deliberate
decision rather than a side effect. If you need another transport, say so — internal can be opened
later without breaking anyone, the reverse cannot. The Redis and Postgres packages share one
internal base for everything that is not transport or storage, so a third first-party transport is
a smaller job than it looks.
:::

## Supported distributed operations

With the backplane enabled, the following become cluster-aware:

| Operation | Cluster behavior |
|---|---|
| `GetTypedMethods<T>(connectionId)` | Routes send/invoke to the node that owns the connection |
| `WithHub<T>().SendAsync(...)` | Broadcasts across all nodes |
| `WithGroup(...)` | Resolves remote group members too |
| `WithUser(...)` | Targets all connections for the user across nodes |
| `WithAttribute(...)` | Resolves matching connections across nodes |
| `InvokeAllAsync(...)` | Collects results from matching clients across the cluster |
| `InvokeOneAsync(...)` | Returns the first successful result across the cluster |
| `AddToGroupAsync(...)` / `RemoveFromGroupAsync(...)` | Works for local and remote connections |

## Presence APIs

`ClientManager` exposes cluster-aware presence snapshots:

```csharp
var allConnections = await clients.GetConnectionsAsync<ChatHub>();
var aliceConnections = await clients.GetConnectionsByUserAsync<ChatHub>("alice");
var documentEditors = await clients.GetConnectionsInGroupAsync<ChatHub>("doc-123");
var admins = await clients.GetConnectionsByAttributeAsync<ChatHub>("role", "admin");
var onlineUsers = await clients.GetOnlineUsersAsync<ChatHub>();
var isAliceOnline = await clients.IsUserOnlineAsync<ChatHub>("alice");
```

Without a backplane, these APIs fall back to local in-memory state only.

## Cluster semantics

### Single-node compatibility

Backplane support is fully opt-in. Existing single-node applications keep the old in-memory behavior unless a backplane is configured.

### Transient delivery

The backplane distributes **live** traffic. It is not a durable queue, event store, or replay log.

### Eventual convergence

Connection metadata, groups, user mappings, and attributes propagate quickly, but not atomically. Right after:

- a new connection,
- a disconnect,
- a remote group join/leave,
- or an attribute/user change visible on reconnect

there can be a short convergence window before every node sees the same routing state.

### Node failure cleanup

Each node publishes a heartbeat. When a node stops heartbeating, other nodes actively sweep and remove its registrations. A node that starts under a node id the store still knows — a crashed predecessor that never said goodbye — clears those stale registrations first, so a stable node id across restarts is safe.

You can tune cleanup behavior on either backplane:

```csharp
builder.Services.AddSignalARRRRedisBackplane(options => options
    .WithConnectionString("localhost:6379")
    .WithHeartbeatInterval(TimeSpan.FromSeconds(5))
    .WithNodeTimeout(TimeSpan.FromSeconds(20)));

builder.Services.AddSignalARRRPostgresBackplane(options => options
    .WithConnectionString(connectionString)
    .WithHeartbeatInterval(TimeSpan.FromSeconds(5))
    .WithNodeTimeout(TimeSpan.FromSeconds(20)));
```

Lower values remove stale registrations faster after crashes, but also increase sensitivity to short pauses. The node timeout must be longer than the heartbeat interval; the Postgres backplane refuses a configuration where it is not.

## When to use it

Use a backplane when you need:

- multiple app instances behind a load balancer,
- user targeting across nodes,
- shared group membership,
- cluster-wide presence,
- or distributed `InvokeAllAsync` / `InvokeOneAsync`.

Stay with in-memory mode when you only run a single app instance and want the simplest setup.
