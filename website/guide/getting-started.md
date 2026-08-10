# Getting Started

SignalARRR enables typed bidirectional RPC over ASP.NET Core SignalR. Define shared interfaces, implement them on the server, and call them from .NET or TypeScript clients with full type safety.

## Installation

::: code-group

```bash [Server]
dotnet add package Cocoar.SignalARRR.Server
```

```bash [.NET Client]
dotnet add package Cocoar.SignalARRR.Client
```

```bash [.NET Framework Client]
dotnet add package Cocoar.SignalARRR.Client.FullFramework
```

```bash [Shared Contracts]
dotnet add package Cocoar.SignalARRR.Contracts
```

```bash [TypeScript / JavaScript]
npm install @cocoar/signalarrr
```

```swift [Swift (Package.swift)]
.package(url: "https://github.com/cocoar-dev/Cocoar.SignalARRR.git", from: "5.0.0")
// Products: CocoarSignalARRR, CocoarSignalARRRMacros
```

:::

## 1. Define shared interfaces

Create a shared project and reference `Cocoar.SignalARRR.Contracts`. Mark each interface with `[SignalARRRContract]` — the source generator will produce typed proxies at build time.

```csharp
using Cocoar.SignalARRR.Common.Attributes;

[SignalARRRContract]
public interface IChatHub
{
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
    IAsyncEnumerable<string> StreamMessages(CancellationToken ct);
}

[SignalARRRContract]
public interface IChatClient
{
    void ReceiveMessage(string user, string message);
    Task<string> GetClientName();
}
```

## 2. Server setup

Register SignalARRR and map the hub in `Program.cs`:

```csharp
builder.Services.AddSignalR();
builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly));

app.UseRouting();
app.MapSignalARRRHub<ChatHub>("/chathub");
```

Create an empty hub — the actual method implementations go into `ServerMethods<T>` classes:

```csharp
public class ChatHub : HARRR
{
    public ChatHub(IServiceProvider sp) : base(sp) { }
}

public class ChatMethods : ServerMethods<ChatHub>, IChatHub
{
    public Task SendMessage(string user, string message)
    {
        // Broadcast to all clients
        var client = ClientContext.GetTypedMethods<IChatClient>();
        client.ReceiveMessage(user, message);
        return Task.CompletedTask;
    }

    public Task<List<string>> GetHistory() =>
        Task.FromResult(new List<string> { "Hello", "World" });

    public async IAsyncEnumerable<string> StreamMessages(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return $"msg-{DateTime.Now:ss}";
            await Task.Delay(1000, ct);
        }
    }
}
```

## 3. .NET client

```csharp
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://localhost:5001/chathub");
});
await connection.StartAsync();

// Typed calls through the shared interface
var chat = connection.GetTypedMethods<IChatHub>();
await chat.SendMessage("Alice", "Hello!");
var history = await chat.GetHistory();

// Streaming
await foreach (var msg in chat.StreamMessages(cancellationToken))
{
    Console.WriteLine(msg);
}
```

## 4. TypeScript client

```ts
import { HARRRConnection } from '@cocoar/signalarrr';

const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://localhost:5001/chathub');
    builder.withAutomaticReconnect();
});
await connection.start();

// Invoke with return value
const history = await connection.invoke<string[]>('ChatMethods.GetHistory');

// Fire-and-forget
await connection.send('ChatMethods.SendMessage', 'Alice', 'Hello!');

// Stream
connection.stream<string>('ChatMethods.StreamMessages').subscribe({
    next: msg => console.log(msg),
    complete: () => console.log('done'),
});

// Handle server-to-client calls. The name is the contract's wire name, 'interface|method' —
// a bare method name never matches a contract call. See /guide/server/contracts-wire-names
connection.onServerMethod('MyApp.Contracts.IChatClient|GetClientName', () => navigator.userAgent);
```

## 5. Swift client (iOS / macOS)

```swift
import CocoarSignalARRR
import CocoarSignalARRRMacros

@HubProxy
protocol IChatHub {
    func sendMessage(user: String, message: String) async throws
    func getHistory() async throws -> [String]
    func streamMessages() async throws -> AsyncThrowingStream<String, Error>
}

let connection = await HARRRConnection.create { builder in
    builder.withUrl(url: "https://localhost:5001/chathub")
    builder.withAutoReconnect()
}
try await connection.start()

// Typed calls through @HubProxy macro
let chat = connection.getTypedMethods(IChatHubProxy.self)
try await chat.sendMessage(user: "Alice", message: "Hello!")
let history = try await chat.getHistory()

// Streaming
for try await msg in try await chat.streamMessages() {
    print(msg)
}

// Handle server-to-client calls. The name is the contract's wire name, "interface|method".
await connection.onServerMethod("MyApp.Contracts.IChatClient|GetClientName") { _ in
    AnyCodable(stringLiteral: UIDevice.current.name)
}
```

## Next steps

- [Why SignalARRR?](/guide/why-signalarrr) — what problems SignalARRR solves compared to raw SignalR
- [Hub Setup](/guide/server/hub-setup) — HARRR base class and configuration
- [Server Methods](/guide/server/server-methods) — organizing hub logic across classes
- [TypeScript Client](/guide/typescript-client/setup) — complete TypeScript/JavaScript guide
- [Swift Client](/guide/swift-client/setup) — complete Swift/iOS/macOS guide
