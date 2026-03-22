# Client Comparison

SignalARRR has three client implementations. This page shows what each client supports.

## RPC

| | .NET | TS | Swift |
|-|:----:|:--:|:-----:|
| Invoke (await result) | ✓ | ✓ | ✓ |
| Send (fire & forget) | ✓ | ✓ | ✓ |
| Generic arguments | ✓ | ✓ | ✓ |

## Item Streaming

| | .NET | TS | Swift |
|-|:----:|:--:|:-----:|
| Server→Client | ✓ | ✓ | ✓ |
| Client→Server | ✓ | ✓ | ✓ |
| Stream method handlers | ✓ | ✓ | ✓ |

## Server-to-Client RPC

| | .NET | TS | Swift |
|-|:----:|:--:|:-----:|
| Method handlers | ✓ | ✓ | ✓ |
| CancellationToken | ✓ | ✓ | ✓ |

## File Transfer (HTTP Stream References)

| | .NET | TS | Swift |
|-|:----:|:--:|:-----:|
| `Stream` parameters | ✓ | ✓ | ✓ |

## Authorization

| | .NET | TS | Swift |
|-|:----:|:--:|:-----:|
| Token provider | ✓ | ✓ | ✓ |
| Auto challenge/refresh | ✓ | ✓ | ✓ |

## Proxy Generation

| | .NET | TS | Swift |
|-|:----:|:--:|:-----:|
| Compile-time proxies | Source Generator | — | `@HubProxy` Macro |
| Runtime fallback | DispatchProxy | — | — |

## Connection

| | .NET | TS | Swift |
|-|:----:|:--:|:-----:|
| Auto-reconnect | ✓ | ✓ | ✓ |
| Connection events | ✓ | ✓ | ✓ |
| Raw SignalR access | ✓ | ✓ | ✓ |
| Raw `on/off` overloads | 16 | — | 8 |
| Interface registration | ✓ | — | ✓ |

## Concurrency Model

| | .NET | TS | Swift |
|-|:----:|:--:|:-----:|
| Async pattern | async/await | Promise | async/await |
| Cancellation | CancellationToken | AbortSignal | Actor |
| Serialization | System.Text.Json | JSON | Codable |
| MessagePack | — | — | — |

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

```ts [TypeScript]
const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://server/hub');
});
await connection.start();
```

```swift [Swift]
let connection = await HARRRConnection.create { builder in
    builder.withUrl(url: "https://server/hub")
}
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
connection.OnServerRequest<string>("GetClientName", name =>
{
    return Environment.MachineName;
});
```

```ts [TypeScript]
connection.onServerMethod('GetClientName', () => {
    return navigator.userAgent;
});
```

```swift [Swift]
await connection.onServerMethod("GetClientName") { _ in
    AnyCodable(stringLiteral: UIDevice.current.name)
}
```

:::

### CancellationToken handling

| Client | Mechanism | Type |
|--------|-----------|------|
| .NET | Standard `CancellationToken` | Native |
| TypeScript | `AbortSignal` via `CancellationManager` (Map-based) | Web API |
| Swift | Actor-based `CancellationManager` with continuations | Swift Concurrency |

### Packages

| Client | Package | Install |
|--------|---------|---------|
| .NET | `Cocoar.SignalARRR.Client` | `dotnet add package` |
| TypeScript | `@cocoar/signalarrr` | `npm install` |
| Swift | `CocoarSignalARRR` + `CocoarSignalARRRMacros` | Swift Package Manager |
