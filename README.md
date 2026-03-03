# SignalARRR

Typed bidirectional RPC over ASP.NET Core SignalR.

Both server and client can call each other's methods through shared interfaces, with compile-time proxy generation, streaming, cancellation propagation, and ASP.NET Core authorization.

## Features

- **Typed bidirectional RPC** — server calls client methods, client calls server methods, both through shared interfaces
- **Compile-time proxy generation** — Roslyn source generator produces proxies from `[SignalARRRContract]` interfaces (zero reflection)
- **Organized hub methods** — split hub logic across multiple `ServerMethods<T>` classes with full DI support
- **Streaming** — `IAsyncEnumerable<T>`, `IObservable<T>`, and `ChannelReader<T>` in both directions
- **CancellationToken propagation** — server can cancel client operations remotely
- **Authorization** — method-level, class-level, and hub-level `[Authorize]` with automatic inheritance
- **Server-to-client calls from anywhere** — inject `ClientManager` in controllers, background services, etc.
- **Optional runtime proxy fallback** — `DispatchProxy`-based package for plugin/dynamic scenarios

## Packages

| Package | Purpose |
|---|---|
| `Cocoar.SignalARRR.Contracts` | `[SignalARRRContract]` attribute + source generator — reference from shared interface projects |
| `Cocoar.SignalARRR.Server` | Server-side: HARRR hub, ServerMethods, authorization, ClientManager |
| `Cocoar.SignalARRR.Client` | Client-side: HARRRConnection, typed proxies, event handlers |
| `Cocoar.SignalARRR.DynamicProxy` | Opt-in runtime proxy fallback via DispatchProxy |

## Quick Start

### 1. Define shared interfaces

In your shared project, reference `Cocoar.SignalARRR.Contracts`:

```csharp
[SignalARRRContract]
public interface IChatHub {
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
    IAsyncEnumerable<string> StreamMessages(CancellationToken ct);
}

[SignalARRRContract]
public interface IChatClient {
    void ReceiveMessage(string user, string message);
    Task<string> GetClientName();
}
```

### 2. Server setup

```csharp
// Program.cs
builder.Services.AddSignalR();
builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly));

app.UseRouting();
app.MapHARRRController<ChatHub>("/chathub");
```

```csharp
// Hub (can be empty — methods go in ServerMethods classes)
public class ChatHub : HARRR {
    public ChatHub(IServiceProvider sp) : base(sp) { }
}

// Server methods (auto-discovered, DI works)
public class ChatMethods : ServerMethods<ChatHub>, IChatHub {
    public Task SendMessage(string user, string message) { ... }
    public Task<List<string>> GetHistory() { ... }
    public async IAsyncEnumerable<string> StreamMessages(
        [EnumeratorCancellation] CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            yield return $"msg-{DateTime.Now:ss}";
            await Task.Delay(1000, ct);
        }
    }
}
```

### 3. Client setup

```csharp
var connection = HARRRConnection.Create(builder => {
    builder.WithUrl("https://localhost:5001/chathub");
});
await connection.StartAsync();

// Typed calls
var chat = connection.GetTypedMethods<IChatHub>();
await chat.SendMessage("Alice", "Hello!");
var history = await chat.GetHistory();

// Streaming
await foreach (var msg in chat.StreamMessages(cancellationToken)) {
    Console.WriteLine(msg);
}
```

### 4. Server-to-client calls

```csharp
// Inside ServerMethods — use ClientContext
var client = ClientContext.GetTypedMethods<IChatClient>();
var name = await client.GetClientName();

// Outside hub context — inject ClientManager
public class NotificationService {
    private readonly ClientManager _clients;
    public NotificationService(ClientManager clients) => _clients = clients;

    public void Notify(string connectionId) {
        var client = _clients.GetTypedMethods<IChatClient>(connectionId);
        client.ReceiveMessage("System", "Hello from server!");
    }
}
```

## Framework Support

- **Server**: .NET 10
- **Client**: .NET 10

## Building from Source

```bash
dotnet build src/Cocoar.SignalARRR.slnx
dotnet test src/Cocoar.SignalARRR.slnx
```

## License

MIT License — see [LICENSE](LICENSE) for details.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

---

**Maintainer**: Bernhard Windisch
