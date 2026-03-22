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

| Method | Description |
|--------|-------------|
| `GetClientById(string id)` | Get a single client by connection ID |
| `GetAllClients()` | All connected clients |
| `GetAllClients(predicate)` | Filter clients by a predicate |
| `GetHARRRClients<T>()` | Clients connected to a specific hub type |
| `GetHARRRClients<T>(predicate)` | Filter clients of a specific hub type |

### Examples

```csharp
// Get all clients connected to the ChatHub
var chatClients = _clients.GetHARRRClients<ChatHub>();

// Find a specific user
var adminClients = _clients.GetAllClients(c =>
    c.User.IsInRole("Admin"));

// Filter by custom attributes
var mobileClients = _clients.GetAllClients()
    .WithAttribute("Platform", "iOS");
```

## Typed extension methods

Extension methods on `ClientManager` combine client lookup and typed proxy creation in one step:

| Method | Description |
|--------|-------------|
| `GetTypedMethods<T>(connectionId)` | Typed proxy for a specific client |
| `GetAllTypedMethods<T>()` | `(ClientContext, T)` tuples for all clients |
| `GetTypedMethodsForHub<T, THub>()` | `(ClientContext, T)` tuples scoped to a hub type |

```csharp
// Call a single client by ID
var methods = _clients.GetTypedMethods<IChatClient>(connectionId);
methods.ReceiveMessage("System", "Hello!");

// Broadcast to all clients (typed)
foreach (var (ctx, methods) in _clients.GetAllTypedMethods<IChatClient>())
{
    methods.ReceiveMessage("System", $"Hello {ctx.Id}!");
}

// Broadcast scoped to a specific hub type
foreach (var (ctx, methods) in _clients.GetTypedMethodsForHub<IChatClient, AppHub>())
{
    methods.ReceiveMessage("System", "Hub-scoped broadcast");
}
```

## Collection extensions on ClientContext

`IEnumerable<ClientContext>` has extension methods for batch invocations:

| Method | Description |
|--------|-------------|
| `InvokeAllAsync<T>(method, args, ct)` | Invoke method on all clients, await all results |
| `InvokeOneAsync<T>(method, args, ct)` | Invoke on clients until one succeeds |
| `WithAttribute(key)` | Filter by attribute existence |
| `WithAttribute(key, value)` | Filter by attribute key-value match |

```csharp
// Ask all clients with a specific attribute and get the first successful response
var result = await _clients.GetAllClients()
    .WithAttribute("Tag", "primary")
    .InvokeOneAsync<string>("GetStatus", Array.Empty<object>(), ct);
// result.ClientId — which client responded
// result.Value — the return value
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
    public IActionResult Broadcast([FromBody] string message)
    {
        foreach (var (_, methods) in _clients.GetTypedMethodsForHub<IChatClient, AppHub>())
        {
            methods.ReceiveMessage("API", message);
        }
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
            foreach (var (_, methods) in _clients.GetTypedMethodsForHub<IChatClient, AppHub>())
            {
                methods.ReceiveMessage("System", "heartbeat");
            }
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
