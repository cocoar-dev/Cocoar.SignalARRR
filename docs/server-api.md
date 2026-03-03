# Server API Reference

## HARRR (Base Hub Class)

**Namespace:** `Cocoar.SignalARRR.Server`

Abstract base class for SignalARRR hubs. Extends `Microsoft.AspNetCore.SignalR.Hub`.

```csharp
public abstract class HARRR : Hub {
    protected HARRR(IServiceProvider serviceProvider);

    // Properties
    protected IServiceProvider ServiceProvider { get; }
    public ILogger Logger { get; set; }
    public ClientContext ClientContext { get; set; }
}
```

### Usage

```csharp
public class MyHub : HARRR {
    public MyHub(IServiceProvider sp) : base(sp) { }
}
```

Hubs can be empty — methods go in `ServerMethods<T>` classes. But you can also
define methods directly on the hub class if preferred.

### Hub protocol methods (called by SignalARRR framework)

These are invoked by the SignalARRR wire protocol. Do not call them directly:

- `InvokeMessage(ClientRequestMessage)` — void method invocation
- `InvokeMessageResult(ClientRequestMessage)` — method with return value
- `SendMessage(ClientRequestMessage)` — fire-and-forget
- `StreamMessage(ClientRequestMessage, CancellationToken)` — streaming
- `StreamItemToServer(Guid, object)` — receive stream item from client
- `StreamCompleteToServer(Guid, string?)` — stream completion signal

---

## ServerMethods / ServerMethods\<T\>

**Namespace:** `Cocoar.SignalARRR.Server`

Organize hub methods into separate classes. Auto-discovered and registered by
`AddSignalARRR()`.

```csharp
public class ServerMethods {
    public ClientContext ClientContext { get; set; }  // Enhanced client info
    public HubCallerContext Context { get; set; }     // SignalR context
    public IHubCallerClients Clients { get; set; }    // Send to clients
    public IGroupManager Groups { get; set; }         // Manage groups
    public ILogger Logger { get; set; }               // Logging
}

public class ServerMethods<T> : ServerMethods where T : HARRR { }
```

### Usage

```csharp
public class UserMethods : ServerMethods<MyHub> {
    private readonly IUserService _users;

    // Constructor injection works — class is registered as transient
    public UserMethods(IUserService users) => _users = users;

    public Task<User> GetUser(int id) => _users.GetById(id);
}
```

### [FromServices] injection

Individual method parameters can be injected:

```csharp
public string GetDate([FromServices] IDateService dateService) {
    return dateService.GetCurrentDate();
}
```

---

## AddSignalARRR() Configuration

**Namespace:** `Cocoar.SignalARRR.Server.ExtensionMethods`

```csharp
services.AddSignalARRR(options => {
    // Register assemblies containing ServerMethods<T> classes
    options.AddServerMethodsFrom(typeof(Program).Assembly);
    options.AddServerMethodsFrom(typeof(SharedMethods).Assembly);

    // Pre-build client method collections for specific interfaces
    options.PreBuiltClientMethods<IChatClient>();
});
```

`AddSignalARRR()` registers these services:
- `ClientManager` (singleton) — query and call connected clients
- `ServerPushStreamManager` (singleton) — HTTP file stream references
- `ServerStreamManager` (singleton) — server-initiated client streams
- `IHARRRClientManager` (singleton) — internal client registry
- `ClientContextDispatcher<T>` (transient per hub type)

---

## MapHARRRController\<T\>()

**Namespace:** `Cocoar.SignalARRR.Server.ExtensionMethods`

```csharp
app.MapHARRRController<MyHub>("/myhub");

// With options
app.MapHARRRController<MyHub>("/myhub", options => {
    options.Transports = HttpTransportType.WebSockets;
});
```

This calls `MapHub<T>()` internally and also registers an HTTP endpoint at
`/myhub/download/{id}` for large file stream references.

---

## ClientContext

**Namespace:** `Cocoar.SignalARRR.Server`

Enhanced client information available in ServerMethods and hub methods.

```csharp
public class ClientContext {
    public string Id { get; }                           // Connection ID
    public IPAddress? RemoteIp { get; }                 // Client IP
    public ClaimsPrincipal User { get; }                // Authenticated user
    public DateTime ConnectedAt { get; }                // Connection time
    public List<DateTime> ReconnectedAt { get; }        // Reconnection history
    public Uri ConnectedTo { get; }                     // Hub URL
    public ClientAttributes Attributes { get; }         // Custom key-value store

    // Create typed proxy to call this specific client
    public T GetTypedMethods<T>() where T : class;
}
```

### Client attributes

Clients can pass custom attributes via headers (prefix `#`) or query parameters
(prefix `@`):

```csharp
// Client sends: header "#device-type: mobile" or query "@device-type=mobile"

// Server reads:
var deviceType = ClientContext.Attributes["device-type"];  // "mobile"
bool isMobile = ClientContext.Attributes.Has("device-type", "mobile");
```

---

## ClientManager

**Namespace:** `Cocoar.SignalARRR.Server`

Inject `ClientManager` to call clients from outside hub context (controllers,
background services, etc.).

```csharp
public class ClientManager {
    public ClientContext GetClientById(string connectionId);
    public IEnumerable<ClientContext> GetAllClients();
    public IEnumerable<ClientContext> GetAllClients(Func<ClientContext, bool> predicate);
    public IEnumerable<ClientContext> GetHARRRClients<THub>();
    public IEnumerable<ClientContext> GetHARRRClients<THub>(Func<ClientContext, bool> predicate);
}
```

### Extension methods

```csharp
// Get typed proxy for a specific client
T GetTypedMethods<T>(this ClientManager cm, string connectionId)

// Enumerate all clients with typed proxies
IEnumerable<(ClientContext, T)> GetTypedMethods<T>(this ClientManager cm)

// Enumerate clients for a specific hub
IEnumerable<(ClientContext, T)> GetTypedMethodsForHub<T, THub>(this ClientManager cm)
```

### Usage example

```csharp
public class NotificationService {
    private readonly ClientManager _clients;

    public NotificationService(ClientManager clients) => _clients = clients;

    // Call one client
    public async Task NotifyOne(string connectionId, string message) {
        var client = _clients.GetTypedMethods<IChatClient>(connectionId);
        client.ReceiveMessage("System", message);
    }

    // Call all clients matching a predicate
    public void BroadcastToMobile() {
        var mobileClients = _clients.GetAllClients()
            .WithAttribute("device-type", "mobile");

        foreach (var ctx in mobileClients) {
            var client = ctx.GetTypedMethods<IChatClient>();
            client.ReceiveMessage("System", "Mobile notification");
        }
    }

    // Invoke with response from all clients
    public async Task<IEnumerable<ClientCollectionResult<string>>> PollAll() {
        return await _clients.GetAllClients()
            .InvokeAllAsync<string>("GetStatus", Array.Empty<object>(), CancellationToken.None);
    }
}
```

### ClientCollectionResult\<T\>

```csharp
public class ClientCollectionResult<TResult> {
    public string ClientId { get; }
    public TResult Value { get; }
}
```

---

## ServerStreamManager

Used internally for server-initiated client streams. When the server calls
`StreamAsync<T>()` on a client proxy, the framework:

1. Sends a stream request to the client
2. Client enumerates the result and sends items back via `StreamItemToServer`
3. Client signals completion via `StreamCompleteToServer`
4. Server reads items from a channel

This is transparent when using typed proxies — just declare the return type as
`IAsyncEnumerable<T>` on the contract interface.
