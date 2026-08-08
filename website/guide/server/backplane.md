# Backplane & Clustering

SignalARRR runs in pure in-memory single-node mode by default. If you never configure a backplane, all client tracking, groups, and filters stay local to the current process.

For multi-node deployments, enable the built-in Redis-compatible backplane.

## Setup

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

Backplane support is fully opt-in. Existing single-node applications keep the old in-memory behavior unless `AddSignalARRRRedisBackplane(...)` is configured.

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

Each node publishes a heartbeat. When a node stops heartbeating, other nodes actively sweep and remove its registrations.

You can tune cleanup behavior with:

```csharp
builder.Services.AddSignalARRRRedisBackplane(options => options
    .WithConnectionString("localhost:6379")
    .WithHeartbeatInterval(TimeSpan.FromSeconds(5))
    .WithNodeTimeout(TimeSpan.FromSeconds(20)));
```

Lower values remove stale registrations faster after crashes, but also increase sensitivity to short pauses.

## When to use it

Use the backplane when you need:

- multiple app instances behind a load balancer,
- user targeting across nodes,
- shared group membership,
- cluster-wide presence,
- or distributed `InvokeAllAsync` / `InvokeOneAsync`.

Stay with in-memory mode when you only run a single app instance and want the simplest setup.
