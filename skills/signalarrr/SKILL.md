---
name: signalarrr
description: >
  Typed bidirectional RPC over SignalR using Cocoar.SignalARRR.
  Use when working with HARRR hubs, ServerMethods, HARRRConnection,
  [SignalARRRContract] interfaces, streaming (IAsyncEnumerable, IObservable,
  ChannelReader), server-to-client calls, or SignalARRR authorization.
metadata:
  author: Bernhard Windisch
  version: "4.0"
---

# Cocoar.SignalARRR

SignalARRR extends ASP.NET Core SignalR with typed bidirectional RPC.
Both server and client can call each other's methods through shared interfaces,
with compile-time proxy generation, streaming, cancellation propagation, and
ASP.NET Core authorization.

## When to use this skill

Use this skill when:
- Setting up a HARRR hub or ServerMethods class
- Creating or configuring a HARRRConnection (client)
- Defining `[SignalARRRContract]` interfaces for typed RPC
- Implementing streaming with IAsyncEnumerable, IObservable, or ChannelReader
- Server needs to call client methods and await responses
- Configuring authorization on hub methods
- Troubleshooting SignalARRR connection or invocation issues

## Package structure

| Package | Purpose |
|---|---|
| `Cocoar.SignalARRR.Server` | Server-side: HARRR hub, ServerMethods, auth, ClientManager |
| `Cocoar.SignalARRR.Client` | Client-side: HARRRConnection, typed proxies |
| `Cocoar.SignalARRR.Contracts` | Shared: `[SignalARRRContract]` attribute + source generator (reference from shared interface projects) |
| `Cocoar.SignalARRR.Common` | Wire protocol types (referenced automatically) |
| `Cocoar.SignalARRR.ProxyGenerator` | Proxy factory infrastructure (referenced automatically) |
| `Cocoar.SignalARRR.DynamicProxy` | Opt-in runtime proxy fallback via DispatchProxy |
| `Cocoar.SignalARRR.SourceGenerator` | Roslyn source generator (bundled in Contracts) |

## Quick start

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
services.AddSignalR();
services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly));

app.UseRouting();
app.UseAuthentication();  // if using auth
app.UseAuthorization();   // if using auth
app.MapHARRRController<ChatHub>("/chathub");
```

```csharp
// Hub
public class ChatHub : HARRR {
    public ChatHub(IServiceProvider sp) : base(sp) { }
}

// Server methods (auto-discovered)
public class ChatMethods : ServerMethods<ChatHub>, IChatHub {
    public Task SendMessage(string user, string message) {
        Clients.All.SendAsync("ReceiveMessage", user, message);
        return Task.CompletedTask;
    }

    public Task<List<string>> GetHistory() => Task.FromResult(new List<string>());

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
// Inside a ServerMethods class — use ClientContext
var client = ClientContext.GetTypedMethods<IChatClient>();
var name = await client.GetClientName();

// Outside hub context — inject ClientManager
public class NotificationService {
    private readonly ClientManager _clients;
    public NotificationService(ClientManager clients) => _clients = clients;

    public void NotifyClient(string connectionId) {
        var client = _clients.GetTypedMethods<IChatClient>(connectionId);
        client.ReceiveMessage("System", "Hello from server!");
    }
}
```

## Reference documentation

For detailed API documentation, see:
- [Getting Started](references/getting-started.md) — full setup walkthrough
- [Server API](references/server-api.md) — HARRR, ServerMethods, ClientManager, AddSignalARRR
- [Client API](references/client-api.md) — HARRRConnection, typed proxies, event handlers
- [Streaming](references/streaming.md) — IAsyncEnumerable, IObservable, ChannelReader patterns
- [Authorization](references/authorization.md) — [Authorize], hub-level inheritance, token flow
- [Proxy Generation](references/proxy-generation.md) — [SignalARRRContract], source generator, DynamicProxy
- [Migration from v2.x](references/migration-v4.md) — breaking changes and upgrade guide
