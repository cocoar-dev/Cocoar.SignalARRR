---
description: "Organize hub logic in ServerMethods<T> classes: compile-time discovery, several classes per hub, why every declared interface is a public RPC surface, method naming and overloads, DI and [FromServices], generic methods and [GenericArguments], server-to-client calls, custom names"
---

# Server Methods

`ServerMethods<T>` classes let you organize hub logic into separate, focused classes instead of putting everything into a single hub. The source generator finds them at compile time, they support full dependency injection, and implement shared contract interfaces.

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

No registration needed — the source generator finds every `ServerMethods<T>` class at compile time, and `AddSignalARRR()` registers those of the assemblies you pass to `AddServerMethodsFrom`. There is no runtime scanning: an assembly built without the generator, which comes with `Cocoar.SignalARRR.Server`, fails at startup, naming it.

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

Registration does not distinguish an interface you intended as a contract from one you implement for other reasons. `[SignalARRRContract]` does not narrow this — it tells the source generator to emit a proxy and a dispatcher and nothing else; interfaces without it are registered just the same, which is what lets the TypeScript, Swift and Kotlin clients call them by wire name.

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

| Declared on | Wire name |
|---|---|
| The hub class | `MethodName` |
| A `ServerMethods` class | `ClassName.MethodName` |
| An interface the hub or class declares (including members it inherits from base interfaces) | `Namespace.InterfaceName\|MethodName` |

A member an interface declares itself hides inherited members of the same wire name. Public overrides of `Hub` and `object` members — `OnConnectedAsync`, `ToString` — are not callable.

### Overloads and default parameters

The wire carries a method name and an argument list, no parameter types. Methods sharing a wire name are told apart by argument count: trailing parameters with a default value may be omitted, and `CancellationToken` and `[FromServices]` parameters take no argument slot. Two methods reachable under the same name with an overlapping argument count — also through defaults, or two base interfaces declaring the same member — cannot be told apart, and the build fails with `SARRR002`. Rename one of them.

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

## Generic methods

A client names the type argument of a generic call as a string, and the server calls only instantiations generated at compile time. Declare them with `[GenericArguments]` (`Cocoar.SignalARRR.Common.Attributes`):

```csharp
[GenericArguments(typeof(Order), typeof(Invoice))]   // Load<Order> and Load<Invoice>
public Task<T> Load<T>(Guid id) { ... }

// several type parameters: one attribute per allowed instantiation, in parameter order
[GenericArguments(typeof(int), typeof(string))]
[GenericArguments(typeof(string), typeof(int))]
public string Pair<A, B>(A a, B b) { ... }
```

The server accepts exactly the declared instantiations; a call with any other type argument is refused with an error naming `[GenericArguments]`. A generic method without the attribute, or an attribute with the wrong number of types, fails the build with `SARRR003`. How a type name on the wire matches a declared type is in [Wire Protocol](/reference/wire-protocol#clientrequestmessage). The .NET proxy sends the names itself; Kotlin declares them with [`@GenericArguments`](/guide/kotlin-client/typed-proxies#generic-methods); Swift passes them in the `genericArguments` parameter of `invoke` and `send`; TypeScript has no generic calls.

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

Clients call this as `ClassName.CustomName` instead of `ClassName.MyMethod`. A name that is empty or contains `|` — the separator between interface and method — fails the build with `SARRR004`.

## Next steps

- [Authorization](/guide/server/authorization) — protect methods with `[Authorize]`
- [Client Manager](/guide/server/client-manager) — call clients from controllers and services
- [Streaming](/guide/streaming/server-to-client) — return streams from server methods