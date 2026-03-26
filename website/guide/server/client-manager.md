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

// Standard LINQ works too
_clients.WithHub<AlertHub>().Where(c => c.User.IsInRole("Admin"))
```

| Method | On | Description |
|--------|---|-------------|
| `WithHub<THub>()` | `ClientManager` | Select hub — always start here |
| `.WithGroup(groupName)` | `IEnumerable<ClientContext>` | Filter by SignalR group |
| `.WithAttribute(key)` | `IEnumerable<ClientContext>` | Filter by attribute existence |
| `.WithAttribute(key, value)` | `IEnumerable<ClientContext>` | Filter by attribute key-value match |
| `.Where(predicate)` | `IEnumerable<ClientContext>` | Standard LINQ |
| `GetClientById(id)` | `ClientManager` | Single client by connection ID |

## Single-client calls

Single clients support full RPC with return values:

```csharp
// By connection ID
var client = _clients.GetClientById(connectionId);
var methods = client.GetTypedMethods<IChatClient>();
methods.ReceiveMessage("System", "Hello!");
string name = await methods.GetClientName();      // ← with return value

// Or: filter down to one, then call with return value
var primary = _clients.WithHub<AlertHub>()
    .WithGroup("dashboard")
    .WithAttribute("role", "primary")
    .First();
string status = await primary.GetTypedMethods<IDeviceClient>().GetStatus();
```

## Multi-client operations

All broadcast and multi-client operations are extension methods on `IEnumerable<ClientContext>`.

::: info Return values are discarded on broadcasts
When using `SendAsync`, methods with return values still work — the client executes the method — but the return value is discarded since there's no single caller to send it back to. A warning is logged. Use `InvokeAllAsync` if you need return values.
:::

### SendAsync — fire-and-forget, one SignalR call

Collects ConnectionIds and sends a **single** `Clients.Clients(ids).SendCoreAsync` call.

```csharp
// Notify all dashboard clients
await _clients.WithHub<AlertHub>().WithGroup("dashboard")
    .SendAsync<IAlertClient>(c => c.AlertUpdated(alertId));

// Notify all admins
await _clients.WithHub<AppHub>().Where(c => c.User.IsInRole("Admin"))
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
| `HARRRType` | `Type` | Hub type the client is connected to |
| `RemoteIp` | `IPAddress?` | Client's IP address |
| `User` | `ClaimsPrincipal` | Authenticated user claims |
| `ConnectedAt` | `DateTime` | Connection timestamp |
| `ReconnectedAt` | `List<DateTime>` | Reconnection history |
| `Groups` | `IReadOnlyCollection<string>` | SignalR groups this client belongs to |
| `Attributes` | `ClientAttributes` | Custom key-value storage |
| `ConnectedTo` | `Uri` | Hub URL |

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
- [Connection Setup (.NET)](/guide/dotnet-client/connection-setup) — configure the .NET client
