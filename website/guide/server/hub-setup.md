# Hub Setup

The `HARRR` class is the SignalARRR hub base class. It extends ASP.NET Core's `Hub` with typed method dispatch, authorization, client context tracking, and streaming support.

## Create a hub

Every SignalARRR application needs at least one hub. The hub itself can be empty — actual method implementations go into [ServerMethods](/guide/server/server-methods) classes.

```csharp
public class AppHub : HARRR
{
    public AppHub(IServiceProvider sp) : base(sp) { }
}
```

## Register and map

In `Program.cs`, register SignalARRR services and map the hub endpoint:

```csharp
builder.Services.AddSignalR();
builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly));

var app = builder.Build();

app.UseRouting();
app.MapSignalARRRHub<AppHub>("/apphub");
```

### MessagePack support

For better performance with many clients, add MessagePack alongside JSON:

```csharp
builder.Services.AddSignalR()
    .AddMessagePackProtocol();  // clients can choose JSON or MessagePack
```

Both protocols run simultaneously — JSON clients and MessagePack clients connect to the same hub.

`AddServerMethodsFrom()` scans the assembly for all `ServerMethods<T>` classes and registers them in DI.

`MapSignalARRRHub<T>()` maps the hub at the specified path and also registers a download endpoint at `{path}/download/{id}` for file stream references.

## Multiple hubs

You can have multiple hubs in the same application:

```csharp
public class ChatHub : HARRR
{
    public ChatHub(IServiceProvider sp) : base(sp) { }
}

public class AdminHub : HARRR
{
    public AdminHub(IServiceProvider sp) : base(sp) { }
}
```

```csharp
app.MapSignalARRRHub<ChatHub>("/chathub");
app.MapSignalARRRHub<AdminHub>("/adminhub");
```

ServerMethods classes are scoped to a specific hub type via the generic parameter:

```csharp
public class ChatMethods : ServerMethods<ChatHub>, IChatHub { ... }   // only on ChatHub
public class AdminMethods : ServerMethods<AdminHub>, IAdminHub { ... } // only on AdminHub
```

## Connection options

`MapSignalARRRHub` accepts SignalR's `HttpConnectionDispatcherOptions` for configuring transports, buffer sizes, and authorization:

```csharp
app.MapSignalARRRHub<AppHub>("/apphub", options =>
{
    options.Transports = HttpTransportType.WebSockets;
    options.ApplicationMaxBufferSize = 64 * 1024;
});
```

## Hub lifecycle

`HARRR` provides the standard SignalR lifecycle hooks. Override them to run logic when clients connect or disconnect:

```csharp
public class AppHub : HARRR
{
    public AppHub(IServiceProvider sp) : base(sp) { }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();  // registers client in ClientManager
        Logger?.LogInformation("Client {Id} connected", Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        Logger?.LogInformation("Client {Id} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);  // unregisters client
    }
}
```

::: warning
Always call `base.OnConnectedAsync()` and `base.OnDisconnectedAsync()` — they manage the client registration in [ClientManager](/guide/server/client-manager).
:::

## Hub properties

| Property | Type | Description |
|----------|------|-------------|
| `ServiceProvider` | `IServiceProvider` | DI container for the current request |
| `Logger` | `ILogger?` | Logger instance (resolved from DI) |
| `ClientContext` | `ClientContext` | Enhanced context for the calling client |
| `Context` | `HubCallerContext` | Standard SignalR caller context |
| `Clients` | `IHubCallerClients` | Access to client connections |
| `Groups` | `IGroupManager` | Group management |

## Next steps

- [Server Methods](/guide/server/server-methods) — split hub logic across classes
- [Authorization](/guide/server/authorization) — protect methods with `[Authorize]`
- [Client Manager](/guide/server/client-manager) — call clients from outside the hub
