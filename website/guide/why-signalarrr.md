# Why SignalARRR?

ASP.NET Core SignalR is excellent for real-time communication. SignalARRR builds on top of it to solve the problems that emerge in production applications with complex server-client interactions.

## The problems

### Magic strings everywhere

With raw SignalR, every method call uses string identifiers. Rename a method and nothing warns you until runtime:

```csharp
// Raw SignalR — no compile-time safety
await Clients.All.SendAsync("ReceiveMessage", user, message);  // string!
await connection.InvokeAsync("GetHistory");                      // string!
```

With SignalARRR, calls go through typed interfaces. Rename a method and the compiler catches it:

```csharp
// SignalARRR — typed
var client = ClientContext.GetTypedMethods<IChatClient>();
client.ReceiveMessage(user, message);  // compile-time checked

var chat = connection.GetTypedMethods<IChatHub>();
await chat.GetHistory();  // compile-time checked
```

### Bidirectional RPC without type safety

Since .NET 7, SignalR supports server-to-client calls with return values via `ISingleClientProxy.InvokeAsync<T>()`. But these calls still rely on string method names:

```csharp
// Raw SignalR — works, but no compile-time safety
var result = await Clients.Single(connectionId)
    .InvokeAsync<string>("GetClientName", cancellationToken);  // string!
```

SignalARRR makes bidirectional RPC fully typed through shared interfaces:

```csharp
// SignalARRR — typed bidirectional RPC
var client = ClientContext.GetTypedMethods<IChatClient>();
string name = await client.GetClientName();  // compile-time checked
```

### Hub classes grow large

As your application grows, a single hub class becomes unwieldy. SignalARRR lets you split methods across multiple `ServerMethods<T>` classes that are auto-discovered and fully DI-enabled:

```csharp
public class ChatMethods : ServerMethods<AppHub>, IChatHub { ... }
public class AdminMethods : ServerMethods<AppHub>, IAdminHub { ... }
public class NotificationMethods : ServerMethods<AppHub>, INotificationHub { ... }
```

### Authorization without token lifecycle management

Raw SignalR supports `[Authorize]` on hub methods, but there's no built-in mechanism for continuous token validation or automatic token refresh during a long-lived connection. SignalARRR adds a challenge/refresh flow and authorization inheritance across `ServerMethods<T>` classes:

```csharp
[Authorize]  // inherited by all ServerMethods classes for this hub
public class SecureHub : HARRR { ... }

public class AdminMethods : ServerMethods<SecureHub>, IAdminHub
{
    [Authorize(Policy = "AdminOnly")]
    public Task DeleteUser(string userId) { ... }

    [AllowAnonymous]
    public Task<string> GetPublicInfo() { ... }
}
```

When a token expires mid-connection, SignalARRR automatically challenges the client for a fresh token instead of disconnecting.

### Large file transfer via HTTP (`Stream` parameters)

WebSocket connections aren't built for multi-GB transfers — buffer limits, memory pressure, and blocking the shared connection for all clients. SignalARRR solves this transparently with **HTTP Stream References**: when a method has a `System.IO.Stream` parameter, the data is automatically routed through HTTP while the RPC call looks completely normal:

```csharp
// Server sends a file to the client — looks like a normal call
var client = ClientContext.GetTypedMethods<IFileClient>();
long size = await client.ProcessFile("video.mp4", fileStream);

// Client receives a regular Stream — no idea it came via HTTP
long FileLength(string name, Stream fileStream) => fileStream.Length;
```

No chunking logic, no upload endpoints, no manual HTTP calls. Just a `Stream` parameter.

### Item streaming in both directions

Separately from file transfer, SignalARRR supports **item streaming** — sending sequences of items over SignalR using `IAsyncEnumerable<T>`, `IObservable<T>`, and `ChannelReader<T>` in both directions.

## What you get

| Feature | Raw SignalR | SignalARRR |
|---------|-------------|------------|
| Typed method calls | No | Yes — shared interfaces |
| Compile-time safety | No | Yes — source generator |
| Server → Client RPC with return | Yes (string-based) | Yes — typed via interfaces |
| Organized hub methods | Single class | Multiple ServerMethods classes |
| Authorization inheritance across classes | No | Hub → ServerMethods inheritance |
| Token auto-refresh on expiry | No | Built-in challenge flow |
| Large file transfer via RPC | No | Automatic HTTP stream references |
| CancellationToken propagation | Limited | Full — server can cancel client |
| Client Manager (outside hub) | Manual | Built-in |
| TypeScript client | Basic | Full protocol support |
| Swift client (iOS/macOS) | No | Native client with `@HubProxy` macro |
| .NET Framework 4.x client | Basic (string-based) | Typed RPC via DispatchProxy |

## Bridge between .NET Core and .NET Framework

SignalARRR supports **.NET 8, .NET 9, .NET 10** on server and client — and **.NET Framework 4.6.2+** on the client side. This enables a powerful pattern in enterprise environments: **use a modern .NET server while keeping legacy .NET Framework clients**.

Real-world example: Your server runs on .NET 10 with the latest frontend technology, while legacy clients on .NET Framework 4.8 connect to the same hub. Those legacy clients can load platform-specific DLLs (SCCM, SCSM, COM interop, legacy SDKs) that don't run on .NET Core — but they still get typed bidirectional RPC through SignalARRR.

```
                 ┌───────────────────────────────┐
                 │       .NET 10 Server          │
                 │     Latest ASP.NET Core       │
                 └───────────────────────────────┘
                    ▲          ▲          ▲
                    │          │          │
          ┌────────┘    Same hub,        └─────────┐
          │           same typed RPC               │
          ▼                 ▼                       ▼
┌──────────────────┐ ┌──────────────────┐ ┌─────────────────────┐
│  .NET 8 Client   │ │  TypeScript      │ │  .NET Framework 4.8 │
│  Cross-platform  │ │  Browser UI      │ │  SCCM/SCSM/COM DLLs │
└──────────────────┘ └──────────────────┘ └─────────────────────┘
```

The FullFramework client (`Cocoar.SignalARRR.Client.FullFramework`) has near-full feature parity: typed proxies via `DispatchProxy`, streaming, server-to-client RPC, cancellation, file transfer, and MessagePack — all on netstandard2.0.

## How it works

SignalARRR wraps SignalR's standard hub protocol. All communication flows through a small set of well-defined hub methods (`InvokeMessage`, `InvokeMessageResult`, `StreamMessage`, etc.) that carry typed payloads. The source generator produces proxy classes that serialize interface method calls into these messages — no runtime reflection needed.

A hub therefore stays an ordinary SignalR hub, and a raw SignalR client can share it with SignalARRR ones. What a raw client can do is worth being precise about, because "compatible" is easy to over-read:

**It can** connect — nothing is required of a client at connection time; call methods declared directly on the hub, as ordinary SignalR methods; and drive the SignalARRR protocol by hand, since the hub method names are constants and the payload is plain JSON with PascalCase fields:

```js
connection.invoke("InvokeMessageResult", {
    Method: "Alerts.List", Arguments: [], Authorization: ""
});
```

**It has to bring its own** handling for everything the SignalARRR clients do for it: typed proxies, the `interface|method` dispatch names, cancellation token references (`__type: "cancellationToken"`), stream references over HTTP, and the four server-to-client handlers — `InvokeServerRequest`, `InvokeServerMessage`, `ChallengeAuthentication`, `CancelTokenFromServer`. Without the last one it cannot answer an authentication challenge, which a `[Authorize]`-gated stream will eventually send.

Note also that a method reached the raw way is authorized by SignalR against the connection's principal, while the same method reached through `InvokeMessageResult` is authorized by SignalARRR against the message credential. Both checks apply; they simply consult different credentials.

## Next steps

- [Getting Started](/guide/getting-started) — install and set up your first hub
- [Hub Setup](/guide/server/hub-setup) — understand the HARRR base class
- [Packages](/reference/packages) — choose the right NuGet/npm packages
