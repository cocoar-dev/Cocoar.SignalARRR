---
description: "Call server methods through GetTypedMethods<T>() proxies: return type mapping to invoke, send and stream, generic methods, proxy caching, and what the source generator needs"
---

# Typed Methods

Call server methods through shared interfaces with full compile-time safety. The source generator produces proxy classes that handle serialization and method dispatch.

## Get a typed proxy

Use `GetTypedMethods<T>()` to get a proxy for a shared contract interface:

```csharp
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://localhost:5001/apphub");
});
await connection.StartAsync();

var chat = connection.GetTypedMethods<IChatHub>();
```

## Call methods

The proxy maps interface method calls to the appropriate SignalARRR protocol message:

```csharp
[SignalARRRContract]
public interface IChatHub
{
    Task SendMessage(string user, string message);    // fire-and-forget
    Task<List<string>> GetHistory();                   // invoke with result
    IAsyncEnumerable<string> StreamMessages(CancellationToken ct);  // stream
}
```

```csharp
// Fire-and-forget (sends InvokeMessage)
await chat.SendMessage("Alice", "Hello!");

// Invoke with result (sends InvokeMessageResult, awaits response)
List<string> history = await chat.GetHistory();

// Stream (sends StreamMessage, returns IAsyncEnumerable)
await foreach (var msg in chat.StreamMessages(cancellationToken))
{
    Console.WriteLine(msg);
}
```

## Return type mapping

The proxy determines the protocol message based on the method's return type:

| Return type | Protocol | Behavior |
|-------------|----------|----------|
| `void` | `SendMessage` | Fire-and-forget (blocking) |
| `Task` | `SendMessage` | Fire-and-forget, awaitable |
| `T` (sync) | `InvokeMessageResult` | Invoke, blocks for result |
| `Task<T>` | `InvokeMessageResult` | Invoke, await result |
| `IAsyncEnumerable<T>` | `StreamMessage` | Server-to-client stream |
| `IObservable<T>` | `StreamMessage` | Server-to-client stream (Rx) |
| `ChannelReader<T>` | `StreamMessage` | Server-to-client stream (channel) |

## Generic method support

Proxies support generic method arguments. The type arguments are serialized as strings and resolved on the server:

```csharp
[SignalARRRContract]
public interface IDataHub
{
    Task<T> GetItem<T>(string key);
}
```

```csharp
var data = connection.GetTypedMethods<IDataHub>();
var user = await data.GetItem<User>("user-123");
```

## Proxy caching

`GetTypedMethods<T>()` caches proxy instances. Calling it multiple times with the same type returns the same proxy object.

## Source generator requirements

For proxy generation to work, the shared interface project must reference `Cocoar.SignalARRR.Contracts`:

```xml
<PackageReference Include="Cocoar.SignalARRR.Contracts" Version="5.*" />
```

The `[SignalARRRContract]` attribute triggers the Roslyn source generator, which produces a proxy class at build time. See [Proxy Generation](/guide/advanced/proxy-generation) for details on how this works.

## Next steps

- [Server-to-Client Handlers](/guide/dotnet-client/server-to-client) — handle callbacks from the server
- [Streaming](/guide/streaming/server-to-client) — stream data from server methods
- [Proxy Generation](/guide/advanced/proxy-generation) — how the source generator works
