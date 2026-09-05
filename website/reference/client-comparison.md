---
description: "Feature matrix of the four clients — .NET, .NET Framework, TypeScript, Swift: platforms, RPC, item streaming, server-to-client RPC, file transfer, authorization, proxies, transports, side-by-side API samples"
---

# Client Comparison

SignalARRR has four client implementations. This page shows what each client supports.

## Platform Support

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Target | net8.0 / net9.0 / net10.0 | net462+ (netstandard2.0) | Node 22 / browsers | iOS 14+ / macOS 11+ |
| Package | `Cocoar.SignalARRR.Client` | `Cocoar.SignalARRR.Client.FullFramework` | `@cocoar/signalarrr` | `CocoarSignalARRR` |

## RPC

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Invoke (await result) | ✓ | ✓ | ✓ | ✓ |
| Send (fire & forget) | ✓ | ✓ | ✓ | ✓ |
| Generic arguments | ✓ | ✓ | ✓ | ✓ |

## Item Streaming

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Server→Client | ✓ | ✓ | ✓ | ✓ |
| Client→Server | ✓ | ✓ | ✓ | ✓ |
| Stream method handlers | ✓ | ✓ | ✓ | ✓ |

::: info .NET Framework streaming
The FullFramework client supports streaming via polyfill packages (`Microsoft.Bcl.AsyncInterfaces` and `System.Threading.Channels`). `IAsyncEnumerable<T>`, `ChannelReader<T>`, and `await foreach` all work on .NET Framework 4.6.2+.
:::

## Server-to-Client RPC

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Method handlers | ✓ | ✓ | ✓ | ✓ |
| Interface registration | ✓ | ✓ | — | ✓ |
| CancellationToken | ✓ | ✓ | ✓ | ✓ |

## File Transfer (HTTP Stream References)

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Download (`Stream` return) | ✓ | ✓ | ✓ | ✓ |
| Upload (`Stream` parameter) | ✓ | ✓ | ✓ | ✓ |

## Authorization

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Token provider | ✓ | ✓ | ✓ | ✓ |
| Auto challenge/refresh | ✓ | ✓ | ✓ | ✓ |

## Proxy Generation

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Compile-time proxies | Source Generator | — | — | `@HubProxy` Macro |
| Runtime proxies | DispatchProxy | DispatchProxy | — | — |

::: info .NET Framework uses DispatchProxy only
The Roslyn source generator requires projects to reference `Cocoar.SignalARRR.Contracts` which targets net8.0+. On .NET Framework, typed proxies are created at runtime via `DispatchProxy`. The interfaces don't need `[SignalARRRContract]` — any C# interface works.
:::

## Connection

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Auto-reconnect | ✓ | ✓ | ✓ | ✓ |
| Connection events | ✓ | ✓ | ✓ | ✓ |
| Raw SignalR access | ✓ | ✓ | ✓ | ✓ |
| Raw `on/off` overloads | 16 | — | — | 8 |

## Concurrency Model

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| Async pattern | async/await | async/await | Promise | async/await |
| Cancellation | CancellationToken | CancellationToken | AbortSignal | Actor |
| Serialization | System.Text.Json | System.Text.Json | JSON | Codable |
| MessagePack | ✓ (optional) | ✓ (optional) | ✓ (optional) | ✓ (built-in) |

::: info MessagePack is optional
MessagePack support is **not** included by default. Install the protocol package separately and register it on the connection builder:

**.NET / .NET Framework:**
```bash
dotnet add package Microsoft.AspNetCore.SignalR.Protocols.MessagePack
```
```csharp
builder.AddMessagePackProtocol();
```

**TypeScript:**
```bash
npm install @microsoft/signalr-protocol-msgpack
```
```ts
builder.withHubProtocol(new MessagePackHubProtocol());
```

**Server:**
```csharp
builder.Services.AddSignalR().AddMessagePackProtocol();
```

**Swift** has MessagePack built-in — use `hubProtocol: .messagepack` when creating the connection.

SignalARRR auto-detects the active protocol and uses the correct serializer. No additional configuration needed.
:::

## Transport

| | .NET | .NET Framework | TS | Swift |
|-|:----:|:--------------:|:--:|:-----:|
| WebSockets | ✓ | ✓ | ✓ | ✓ |
| Server-Sent Events | ✓ | ✓ | ✓ | ✓ |
| Long Polling | ✓ | ✓ | ✓ | ✓ |
| Transport fallback | ✓ | ✓ | ✓ | ✓ |

## API Comparison

### Creating a connection

::: code-group

```csharp [.NET]
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://server/hub");
});
await connection.StartAsync();
```

```csharp [.NET Framework]
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://server/hub");
});
await connection.StartAsync();
```

```ts [TypeScript]
const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://server/hub');
});
await connection.start();
```

```swift [Swift]
let connection = await HARRRConnection.create(
    url: "https://server/hub"
)
try await connection.start()
```

:::

### Typed proxies

::: code-group

```csharp [.NET — Source Generator]
// Shared project: mark with [SignalARRRContract]
[SignalARRRContract]
public interface IChatHub {
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
}

// Client: get typed proxy
var chat = connection.GetTypedMethods<IChatHub>();
await chat.SendMessage("Alice", "Hello!");
```

```csharp [.NET Framework — DispatchProxy]
// Define interface (no attribute needed)
public interface IChatHub {
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
}

// Client: get typed proxy (runtime-generated via DispatchProxy)
var chat = connection.GetTypedMethods<IChatHub>();
await chat.SendMessage("Alice", "Hello!");
```

```swift [Swift — @HubProxy Macro]
// Client: mark with @HubProxy
@HubProxy
protocol IChatHub {
    func sendMessage(user: String, message: String) async throws
    func getHistory() async throws -> [String]
}

// Client: get typed proxy
let chat = connection.getTypedMethods(IChatHubProxy.self)
try await chat.sendMessage(user: "Alice", message: "Hello!")
```

:::

TypeScript has no typed proxy generation — method names are passed as strings.

### Invoke / Send / Stream

::: code-group

```csharp [.NET]
// Invoke (await result)
var result = await connection.InvokeCoreAsync<string>(message, ct);

// Send (fire-and-forget)
await connection.SendCoreAsync(message, ct);

// Stream
await foreach (var item in connection.StreamAsyncCore<int>(message, ct))
    Console.WriteLine(item);
```

```csharp [.NET Framework]
// Invoke (await result)
var result = await connection.InvokeCoreAsync<string>(message, ct);

// Send (fire-and-forget)
await connection.SendCoreAsync(message, ct);

// Stream (via polyfill packages)
await foreach (var item in connection.StreamAsyncCore<int>(message, ct))
    Console.WriteLine(item);
```

```ts [TypeScript]
// Invoke
const result = await connection.invoke<string>('Method.Name');

// Send
await connection.send('Method.Name', arg1, arg2);

// Stream
connection.stream<number>('Method.Name').subscribe({
    next: item => console.log(item),
});
```

```swift [Swift]
// Invoke
let result: String = try await connection.invoke("Method.Name")

// Send
try await connection.send("Method.Name", arguments: arg1, arg2)

// Stream
for try await item in try await connection.stream("Method.Name") as AsyncThrowingStream<Int, Error> {
    print(item)
}
```

:::

### Server-to-client handlers

::: code-group

```csharp [.NET]
connection.RegisterInterface<IChatClient, ChatClientImpl>(new ChatClientImpl());
```

```csharp [.NET Framework]
connection.RegisterInterface<IChatClient, ChatClientImpl>(new ChatClientImpl());
```

```ts [TypeScript]
// The contract's wire name: the .NET clients resolve the interface for you, TypeScript and Swift
// address it directly. See /guide/server/contracts-wire-names
connection.onServerMethod('MyApp.Contracts.IChatClient|ReceiveMessage', (user, message) => {
    console.log(`${user}: ${message}`);
});
```

```swift [Swift]
await connection.onServerMethod("MyApp.Contracts.IChatClient|ReceiveMessage") { args in
    print("\(args[0]): \(args[1])")
    return AnyCodable.nil
}
```

:::

### CancellationToken handling

| Client | Mechanism | Type |
|--------|-----------|------|
| .NET | Standard `CancellationToken` | Native |
| .NET Framework | Standard `CancellationToken` | Native |
| TypeScript | `AbortSignal` via `CancellationManager` (Map-based) | Web API |
| Swift | Actor-based `CancellationManager` with continuations | Swift Concurrency |

### Packages

| Client | Package | Install |
|--------|---------|---------|
| .NET | `Cocoar.SignalARRR.Client` | `dotnet add package` |
| .NET Framework | `Cocoar.SignalARRR.Client.FullFramework` | `dotnet add package` |
| TypeScript | `@cocoar/signalarrr` | `npm install` |
| Swift | `CocoarSignalARRR` + `CocoarSignalARRRMacros` | Swift Package Manager |
