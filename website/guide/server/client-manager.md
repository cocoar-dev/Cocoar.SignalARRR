---
description: "ClientManager: query clients with IClientQuery, single-client calls, SendAsync / InvokeAllAsync / InvokeOneAsync, presence snapshots, groups, use from controllers and background services, ClientContext properties and custom attributes"
---

# Client Manager

`ClientManager` tracks all connected clients and enables server-to-client RPC from anywhere — controllers, background services, or other hubs. Inject it from DI as a singleton.

## Inject ClientManager

```csharp
public class NotificationService
{
    private readonly ClientManager _clients;

    public NotificationService(ClientManager clients) => _clients = clients;

    public void NotifyUser(string connectionId)
    {
        var methods = _clients.GetTypedMethods<IChatClient>(connectionId);
        methods.ReceiveMessage("System", "You have a new notification");
    }
}
```

## Query clients

Start with `WithHub<T>()` to select the hub, then chain filters:

```csharp
// Always start with the hub
_clients.WithHub<AlertHub>()

// Then chain filters
_clients.WithHub<AlertHub>().WithGroup("dashboard")
_clients.WithHub<AlertHub>().WithAttribute("role", "oncall")
_clients.WithHub<AlertHub>().WithGroup("dashboard").WithAttribute("region", "eu")
```

`WithHub<T>()` returns an **`IClientQuery`**. That is a *description* of a target set, not a list of
clients: with a [backplane](/guide/server/backplane) enabled, the filters and the send/invoke methods
below are resolved across every node in the cluster.

| Method | On | Description |
|--------|---|-------------|
| `WithHub<THub>()` | `ClientManager` | Select hub — always start here |
| `.WithGroup(groupName)` | `IClientQuery` | Filter by SignalR group — cluster-wide |
| `.WithAttribute(key)` | `IClientQuery` | Filter by attribute existence — cluster-wide |
| `.WithAttribute(key, value)` | `IClientQuery` | Filter by attribute key-value match — cluster-wide |
| `.WithLocalFilter(predicate)` | `IClientQuery` | Filter by any predicate — **this node only** |
| `.LocalClients()` | `IClientQuery` | The matching `ClientContext`s owned by this node |
| `GetClientById(id)` | `ClientManager` | Single client by connection ID |

### Why predicates are node-local

A predicate is a delegate over `ClientContext` objects. It cannot be shipped to another node, and the
other nodes' `ClientContext` instances do not exist in this process to run it against — the connection
registry stores snapshots, not live contexts. So `WithLocalFilter` narrows the query to this node, and
everything chained after it stays local as well:

```csharp
// This node's admins only — the name says so
_clients.WithHub<AppHub>().WithLocalFilter(c => c.User.IsInRole("Admin"))
```

To narrow a query that should still reach the whole cluster, filter on something the registry can
answer for every node — group, user, or a connection attribute:

```csharp
// Every admin on every node
_clients.WithHub<AppHub>().WithAttribute("role", "admin")
```

::: warning Changed in 5.0
`WithHub<T>()` used to return `IEnumerable<ClientContext>`, so `.Where(...)` compiled — and silently
turned a cluster-wide broadcast into a node-local one, because the LINQ result no longer carried the
query's cluster scope. Enumerating the result had never shown clients from other nodes either. Both
gaps are now closed by the type: `.Where(...)` no longer compiles, `WithLocalFilter` says what it
does, and `LocalClients()` is the one way to get at `ClientContext` objects.
:::

## Single-client calls

Single clients support full RPC with return values:

```csharp
// By connection ID
var client = _clients.GetClientById(connectionId);
var methods = client.GetTypedMethods<IChatClient>();
methods.ReceiveMessage("System", "Hello!");
string name = await methods.GetClientName();      // ← with return value

// Or: filter down, then pick one of this node's matches
var primary = _clients.WithHub<AlertHub>()
    .WithGroup("dashboard")
    .WithAttribute("role", "primary")
    .LocalClients()
    .First();
string status = await primary.GetTypedMethods<IDeviceClient>().GetStatus();
```

## Multi-client operations

All broadcast and multi-client operations are methods on `IClientQuery`, so they are only reachable
from a query — which is what keeps them cluster-aware.

::: info Return values are discarded on broadcasts
When using `SendAsync`, methods with return values still work — the client executes the method — but the return value is discarded since there's no single caller to send it back to. A warning is logged. Use `InvokeAllAsync` if you need return values.
:::

::: warning Errors are discarded too
`SendAsync` completes when the message reaches the transport, not when the clients have run the
method. A handler that throws is logged **on the client** and nowhere else — the server sees a
successful send either way. `InvokeAllAsync` and `InvokeOneAsync` do surface client-side failures,
because they wait for an answer. See
[what happens when a handler throws](/guide/dotnet-client/server-to-client#what-happens-when-a-handler-throws).
:::

### SendAsync — fire-and-forget, one SignalR call

Collects ConnectionIds and sends a **single** `Clients.Clients(ids).SendCoreAsync` call.

```csharp
// Notify all dashboard clients
await _clients.WithHub<AlertHub>().WithGroup("dashboard")
    .SendAsync<IAlertClient>(c => c.AlertUpdated(alertId));

// Notify all admins, on every node
await _clients.WithHub<AppHub>().WithAttribute("role", "admin")
    .SendAsync<IAlertClient>(c => c.SecurityAlert(details));

// Notify iOS users
await _clients.WithHub<AppHub>().WithAttribute("Platform", "iOS")
    .SendAsync<IAppClient>(c => c.PushUpdate(version));
```

### InvokeAllAsync — call all, collect all results

Invokes on **each** client individually (N calls), awaits all in parallel, returns results per client.

```csharp
var results = await _clients.WithHub<DeviceHub>()
    .InvokeAllAsync<IDeviceClient, string>(c => c.GetStatus());

foreach (var r in results) {
    Console.WriteLine($"Client {r.ClientId}: {r.Value}");
}
```

### InvokeOneAsync — first responder wins

Calls clients one by one until the **first** succeeds.

```csharp
var result = await _clients.WithHub<DeviceHub>()
    .WithAttribute("role", "primary")
    .InvokeOneAsync<IDeviceClient, string>(c => c.GetStatus());
// result.ClientId — which client responded
// result.Value — the return value
```

### API summary

| Method | Calls | Returns | Use case |
|--------|-------|---------|----------|
| `.SendAsync<T>(action)` | 1 (broadcast) | Nothing | Notifications, events |
| `.InvokeAllAsync<T, TResult>(func)` | N (parallel) | All results | Status polling, data collection |
| `.InvokeOneAsync<T, TResult>(func)` | 1–N (sequential) | First success | Failover, load distribution |

## Presence snapshots

`ClientManager` can also return connection and user presence snapshots:

```csharp
var allConnections = await _clients.GetConnectionsAsync<AlertHub>();
var aliceConnections = await _clients.GetConnectionsByUserAsync<AlertHub>("alice");
var dashboardConnections = await _clients.GetConnectionsInGroupAsync<AlertHub>("dashboard");
var primaryNodes = await _clients.GetConnectionsByAttributeAsync<AlertHub>("role", "primary");
var onlineUsers = await _clients.GetOnlineUsersAsync<AlertHub>();
var isAliceOnline = await _clients.IsUserOnlineAsync<AlertHub>("alice");
```

When no backplane is configured, these APIs return data from the current node only. With the Redis-compatible backplane enabled, they return cluster-wide snapshots.

#### On `ClientManager` — group management

| Method | Description |
|--------|-------------|
| `.AddToGroupAsync(connectionId, groupName)` | Adds client to SignalR group AND tracks in `ClientContext.Groups` |
| `.RemoveFromGroupAsync(connectionId, groupName)` | Removes from SignalR group AND `ClientContext.Groups` |

## Groups

SignalARRR integrates SignalR groups directly into `ClientManager`. When you add a client to a group, it's tracked in both SignalR (for broadcasting) and `ClientContext.Groups` (for querying).

### Managing groups

```csharp
// Add a client to a group — syncs both SignalR and ClientContext
await _clients.AddToGroupAsync(connectionId, "dashboard");
await _clients.AddToGroupAsync(connectionId, "alerts");

// Remove from group
await _clients.RemoveFromGroupAsync(connectionId, "dashboard");

// Query groups on a client
var client = _clients.GetClientById(connectionId);
var groups = client.Groups;  // → IReadOnlyCollection<string> { "alerts" }
```

### Broadcasting to a group

```csharp
await _clients.WithHub<AlertHub>().WithGroup("dashboard")
    .SendAsync<IAlertClient>(c => c.AlertUpdated(alertId));
```

## Use in controllers

```csharp
[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly ClientManager _clients;

    public NotificationController(ClientManager clients) => _clients = clients;

    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] string message)
    {
        await _clients.WithHub<AppHub>()
            .SendAsync<IChatClient>(c => c.ReceiveMessage("API", message));
        return Ok();
    }

    [HttpPost("alert/{group}")]
    public async Task<IActionResult> AlertGroup(string group, [FromBody] AlertData alert)
    {
        await _clients.WithHub<AlertHub>().WithGroup(group)
            .SendAsync<IAlertClient>(c => c.AlertUpdated(alert.Id));
        return Ok();
    }
}
```

## Use in background services

```csharp
public class HeartbeatService : BackgroundService
{
    private readonly ClientManager _clients;

    public HeartbeatService(ClientManager clients) => _clients = clients;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _clients.WithHub<AppHub>()
                .SendAsync<IChatClient>(c => c.ReceiveMessage("System", "heartbeat"));
            await Task.Delay(30_000, ct);
        }
    }
}
```

## ClientContext properties

Each `ClientContext` provides detailed information about the connected client:

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Connection ID |
| `RemoteIp` | `IPAddress?` | Client's IP address |
| `User` | `ClaimsPrincipal` | Authenticated user claims |
| `UserIdentifier` | `string?` | SignalR's user identifier, or the principal's name identifier |
| `AuthMode` | `AuthenticationMode` | How this connection authenticates — see [Authorization](/guide/server/authorization) |
| `ClientCertificate` | `X509Certificate2?` | The certificate presented at the TLS handshake, if any |
| `ConnectedAt` | `DateTime` | Connection timestamp |
| `ReconnectedAt` | `List<DateTime>` | Reconnection history |
| `Groups` | `IReadOnlyCollection<string>` | SignalR groups this client belongs to |
| `Attributes` | `ClientAttributes` | Custom key-value storage |
| `ConnectedTo` | `Uri` | Hub URL |

### Dropping a connection

`Abort()` closes the connection. Use it when you learn out-of-band that a client should no longer be
connected — a revoked session, a decommissioned device — rather than letting each of its calls fail
while the socket stays up:

```csharp
foreach (var client in _clients.WithHub<AppHub>().LocalClients())
{
    if (await _sessions.IsRevokedAsync(client.UserIdentifier))
        client.Abort();
}
```

Safe to call more than once, and after the connection has already gone. Note `LocalClients()` — a
connection can only be aborted by the node holding it.

Re-validation can reach the same outcome from inside the authorization pipeline by returning
`RevalidationResult.Abort()`; see [Authorization](/guide/server/authorization).

## Custom client attributes

Clients can pass custom attributes via HTTP headers (prefixed with `#`) or query parameters (prefixed with `@`) during the initial handshake:

```csharp
// Server: read custom attributes
var version = client.Attributes["AppVersion"];
var platform = client.Attributes["Platform"];

// Check attribute existence
bool hasPlatform = client.Attributes.Has("Platform");
bool isIOS = client.Attributes.Has("Platform", "iOS");
```

## Next steps

- [Server Methods](/guide/server/server-methods) — server-to-client calls inside the hub
- [Authorization](/guide/server/authorization) — filter clients by authentication state
- [Backplane & Clustering](/guide/server/backplane) — enable multi-node routing and presence
- [Connection Setup (.NET)](/guide/dotnet-client/connection-setup) — configure the .NET client
