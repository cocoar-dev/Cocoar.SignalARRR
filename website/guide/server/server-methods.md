# Server Methods

`ServerMethods<T>` classes let you organize hub logic into separate, focused classes instead of putting everything into a single hub. They are auto-discovered, support full dependency injection, and implement shared contract interfaces.

## Basic usage

Create a class that extends `ServerMethods<T>` where `T` is your hub type. Implement a shared contract interface:

```csharp
[SignalARRRContract]
public interface IChatHub
{
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
}

public class ChatMethods : ServerMethods<ChatHub>, IChatHub
{
    public Task SendMessage(string user, string message)
    {
        var client = ClientContext.GetTypedMethods<IChatClient>();
        client.ReceiveMessage(user, message);
        return Task.CompletedTask;
    }

    public Task<List<string>> GetHistory() =>
        Task.FromResult(new List<string> { "Hello", "World" });
}
```

No registration needed — `AddSignalARRR()` discovers all `ServerMethods<T>` classes in the scanned assemblies.

## Multiple classes per hub

Split your hub into domain-specific classes:

```csharp
public class ChatMethods : ServerMethods<AppHub>, IChatHub { ... }
public class UserMethods : ServerMethods<AppHub>, IUserHub { ... }
public class AdminMethods : ServerMethods<AppHub>, IAdminHub { ... }
```

All three classes serve methods on the same `AppHub` endpoint.

## Every interface you declare is a public surface

An interface on a `ServerMethods` class or on a hub is not just a shape — it is registered, and every member on it becomes callable from any client that can reach the hub:

```csharp
// IOrderRepository was meant for DI and testing. It is now RPC.
public class OrderMethods : ServerMethods<AppHub>, IOrderRepository {
    public Task DeleteAll() { ... }   // any client: invoke("MyApp.IOrderRepository|DeleteAll")
}
```

Registration does not distinguish an interface you intended as a contract from one you implement for other reasons. `[SignalARRRContract]` does not narrow this — it tells the source generator to emit a proxy and nothing else; interfaces without it are registered just the same, which is what lets the DynamicProxy, .NET Framework, TypeScript and Swift clients work without it.

Authorization still applies: whatever `[Authorize]` guards the hub or the implementing class guards these members too. But if the hub is reachable, so are they.

::: tip Keep non-contract interfaces off the class
Implement them on a separate collaborator the `ServerMethods` class holds, rather than on the class itself:

```csharp
public class OrderMethods : ServerMethods<AppHub>, IOrderContract {
    private readonly IOrderRepository _orders;   // injected, not implemented
    public OrderMethods(IOrderRepository orders) => _orders = orders;
}
```
:::

## Method naming

Clients call methods using the pattern `ClassName.MethodName`:

```csharp
// .NET client
var chat = connection.GetTypedMethods<IChatHub>();  // uses interface name mapping
await chat.SendMessage("Alice", "Hello!");
```

```ts
// TypeScript client
await connection.invoke('ChatMethods.SendMessage', 'Alice', 'Hello!');
```

When using typed proxies on the .NET client, the naming is handled automatically through the interface mapping.

## Available properties

Every `ServerMethods` class has these properties auto-injected at invocation time:

| Property | Type | Description |
|----------|------|-------------|
| `ClientContext` | `ClientContext` | Current client's context (ID, user, attributes) |
| `Context` | `HubCallerContext` | Standard SignalR caller context |
| `Clients` | `IHubCallerClients` | Send messages to other clients |
| `Groups` | `IGroupManager` | Add/remove clients from groups |
| `Logger` | `ILogger` | Logger instance |

## Dependency injection

ServerMethods classes are resolved from DI as transient services. Inject dependencies through the constructor:

```csharp
public class ChatMethods : ServerMethods<AppHub>, IChatHub
{
    private readonly IChatRepository _repo;
    private readonly ILogger<ChatMethods> _logger;

    public ChatMethods(IChatRepository repo, ILogger<ChatMethods> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<List<string>> GetHistory()
    {
        _logger.LogInformation("Client {Id} requested history", ClientContext.Id);
        return await _repo.GetRecentMessages();
    }
}
```

## Parameter injection with [FromServices]

Individual method parameters can be injected from DI using `[FromServices]`:

```csharp
public async Task ProcessData(
    string input,
    [FromServices] IDataProcessor processor)
{
    await processor.Process(input);
}
```

## Server-to-client calls

Inside a `ServerMethods` class, use `ClientContext.GetTypedMethods<T>()` to call back the current client:

```csharp
public async Task<string> Greet()
{
    var client = ClientContext.GetTypedMethods<IChatClient>();
    string name = await client.GetClientName();  // awaits client response
    return $"Hello, {name}!";
}
```

To call other clients, inject `ClientManager` and use the typed extension methods:

```csharp
public class ChatMethods : ServerMethods<AppHub>, IChatHub
{
    private readonly ClientManager _clients;

    public ChatMethods(ClientManager clients) => _clients = clients;

    public void BroadcastMessage(string message)
    {
        // GetTypedMethodsForHub returns (ClientContext, T) tuples
        foreach (var (ctx, methods) in _clients.GetTypedMethodsForHub<IChatClient, AppHub>())
        {
            methods.ReceiveMessage("System", message);
        }
    }

    public async Task<string> AskClient(string connectionId)
    {
        // GetTypedMethods extension combines GetClientById + GetTypedMethods
        var methods = _clients.GetTypedMethods<IChatClient>(connectionId);
        return await methods.GetClientName();
    }
}
```

See [Client Manager](/guide/server/client-manager) for more details.

## Custom method names

Use `[MessageName]` to override the default method name:

```csharp
[MessageName("CustomName")]
public Task MyMethod() { ... }
```

Clients call this as `ClassName.CustomName` instead of `ClassName.MyMethod`.

## Next steps

- [Authorization](/guide/server/authorization) — protect methods with `[Authorize]`
- [Client Manager](/guide/server/client-manager) — call clients from controllers and services
- [Streaming](/guide/streaming/server-to-client) — return streams from server methods
