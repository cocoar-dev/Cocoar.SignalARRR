# SignalARRR

[![CI](https://github.com/cocoar-dev/Cocoar.SignalARRR/actions/workflows/ci-pr-validation.yml/badge.svg)](https://github.com/cocoar-dev/Cocoar.SignalARRR/actions/workflows/ci-pr-validation.yml)
[![NuGet](https://img.shields.io/nuget/v/Cocoar.SignalARRR.Server?label=NuGet)](https://www.nuget.org/packages/Cocoar.SignalARRR.Server)
[![npm](https://img.shields.io/npm/v/@cocoar/signalarrr?label=npm)](https://www.npmjs.com/package/@cocoar/signalarrr)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)

Typed bidirectional RPC over ASP.NET Core SignalR.

Server and client call each other's methods through shared interfaces — with compile-time proxy generation, streaming, cancellation propagation, and ASP.NET Core authorization. Clients available for **.NET**, **TypeScript/JavaScript**, and **Swift**.

> **[Read the full documentation](https://docs.cocoar.dev/signalarrr/)**

## Packages

### .NET

| Package | Purpose |
|---|---|
| [`Cocoar.SignalARRR.Contracts`](https://www.nuget.org/packages/Cocoar.SignalARRR.Contracts) | `[SignalARRRContract]` attribute + source generator — reference from shared interface projects |
| [`Cocoar.SignalARRR.Server`](https://www.nuget.org/packages/Cocoar.SignalARRR.Server) | Server-side: HARRR hub, ServerMethods, authorization, ClientManager |
| [`Cocoar.SignalARRR.Client`](https://www.nuget.org/packages/Cocoar.SignalARRR.Client) | Client-side: HARRRConnection, typed proxies, event handlers |
| [`Cocoar.SignalARRR.DynamicProxy`](https://www.nuget.org/packages/Cocoar.SignalARRR.DynamicProxy) | Opt-in runtime proxy fallback via DispatchProxy |
| [`Cocoar.SignalARRR.Client.FullFramework`](https://www.nuget.org/packages/Cocoar.SignalARRR.Client.FullFramework) | Client for .NET Framework 4.6.2+ — typed proxies, streaming, file transfer |

### TypeScript / JavaScript

```
npm install @cocoar/signalarrr
```

### Swift (iOS / macOS)

```swift
.package(url: "https://github.com/cocoar-dev/Cocoar.SignalARRR.git", from: "4.0.0")
```

## Quick Start

Define shared interfaces, set up the server, and call methods with full type safety:

```csharp
// Shared interface
[SignalARRRContract]
public interface IChatHub {
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
}

// Client usage — one line to get a typed proxy
var chat = connection.GetTypedMethods<IChatHub>();
await chat.SendMessage("Alice", "Hello!");
```

```typescript
// TypeScript client
const history = await connection.invoke<string[]>('ChatMethods.GetHistory');
```

```swift
// Swift client — @HubProxy macro generates the proxy
@HubProxy protocol IChatHub { ... }
let chat = connection.getTypedMethods(IChatHubProxy.self)
```

For full setup guides, streaming, authorization, and server-to-client calls, see the **[documentation](https://docs.cocoar.dev/signalarrr/)**.

## Features

- **Typed bidirectional RPC** — server and client call each other through shared interfaces
- **Compile-time proxy generation** — Roslyn source generator (zero reflection)
- **Organized hub methods** — split logic across `ServerMethods<T>` classes with full DI
- **Streaming** — `IAsyncEnumerable<T>`, `IObservable<T>`, `ChannelReader<T>` in both directions
- **HTTP stream references** — file download/upload through SignalR hub methods
- **CancellationToken propagation** — server can cancel client operations remotely
- **Authorization** — method-level, class-level, and hub-level `[Authorize]`
- **Server-to-client calls from anywhere** — inject `ClientManager` in controllers, background services, etc.
- **Four clients** — .NET, .NET Framework, TypeScript/JavaScript, Swift
- **Typed broadcasts** — `WithHub<T>().WithGroup().SendAsync<T>()` for groups and filtered clients
- **Redis-compatible multi-node backplane** — opt-in scale-out with Redis, Valkey, or Garnet

## Redis-compatible backplane

SignalARRR stays pure in-memory by default. If you do **not** configure a backplane, behavior remains single-node and process-local exactly as before.

For multi-node scale-out, add the `Cocoar.SignalARRR.Server.Backplane.Redis` package and opt in.
It is a separate package as of 5.0, so single-node applications do not carry `StackExchange.Redis`:

```csharp
builder.Services.AddSignalARRR(b =>
    b.AddServerMethodsFrom(typeof(ChatHub).Assembly));

builder.Services.AddSignalARRRRedisBackplane(options => options
    .WithConnectionString("localhost:6379,abortConnect=false")
    .WithChannelPrefix("my-app")
    .WithNodeId($"{Environment.MachineName}-api-1"));
```

This works with **Redis**, **Valkey**, and **Garnet** because SignalARRR talks to a Redis-compatible backend via `StackExchange.Redis`.

### What becomes cluster-aware

- `GetTypedMethods<T>(connectionId)` send/invoke across nodes
- `WithHub<T>().SendAsync(...)` across all nodes
- `WithGroup(...)`, `WithUser(...)`, and `WithAttribute(...)` across nodes
- `InvokeAllAsync(...)` and `InvokeOneAsync(...)` across nodes
- `AddToGroupAsync(...)` / `RemoveFromGroupAsync(...)` for remote connections
- Presence APIs on `ClientManager`:
  - `GetConnectionsAsync<THub>()`
  - `GetConnectionsByUserAsync<THub>(...)`
  - `GetConnectionsInGroupAsync<THub>(...)`
  - `GetConnectionsByAttributeAsync<THub>(...)`
  - `GetOnlineUsersAsync<THub>()`
  - `IsUserOnlineAsync<THub>(...)`

### Cluster semantics

- **Transient transport**: the backplane distributes live messages; it is not a durable queue or event store.
- **Eventual convergence**: connection, group, user, and attribute metadata propagate quickly, but not atomically across all nodes. Right after connect/disconnect/group changes there can be a short convergence window.
- **Crash cleanup**: dead nodes are removed by heartbeat + timeout sweep. Tune `WithHeartbeatInterval(...)` and `WithNodeTimeout(...)` if you want faster stale-node cleanup.
- **Safe fallback**: without `AddSignalARRRRedisBackplane(...)`, all APIs continue to use the old in-memory single-node path.

## Connection loss semantics

What happens to in-flight work when a connection drops — worth knowing before you rely on it:

- **Pending invocations fail immediately, and are not safely retryable.** When the connection drops, every outstanding invoke fails right away — a 50 ms blip is no different from a long outage. After reconnecting it is a new connection with a new `ConnectionId`; the lost answer is gone for good. And if the drop happened *after* the server executed but *before* the response arrived, the caller cannot know whether the call took effect — retry only operations that are idempotent.
- **SignalR's resilience features are available, but pass-through.** `MapSignalARRRHub<THub>(...)` returns SignalR's `HubEndpointConventionBuilder` (so `AllowStatefulReconnects()` can be applied), and `HARRRConnection.Create(...)` hands you the real `HubConnectionBuilder` (so `WithAutomaticReconnect()` / `WithStatefulReconnect()` work as documented by SignalR). SignalARRR does not interfere with either: its own lifetime signals hang off SignalR's, which Stateful Reconnect extends. **Caveat:** whether SignalARRR's connection-bound state — stream ownership, upload slots, presence registrations — survives a stateful reconnect has not been verified by tests; treat that combination as unvalidated before building on it. `WithAutomaticReconnect()` always yields a new `ConnectionId`, so connection-bound state does not carry over there by design.
- **A fire-and-forget call to a client cannot report failure.** For a contract member returning `void` or `Task`, the server's send completes when the message reaches the transport — before the client runs the method. If the handler then throws, the error is logged on the client and nowhere else; the server saw a successful send. Members returning `Task<T>` or `T` do surface client-side failures, as a `HubException`. Give a member a return value when the server needs to know it worked.
- **Two deadlines run independently of connection state.** The stream upload wait (`StreamUploadTimeout`, default 2 minutes) and the backplane invoke timeout (`WithInvokeTimeout(...)`, default 15 seconds) keep ticking while a reconnect is bridging a gap — a long enough outage fails them even though the connection technically survives. Both are configurable; size them with reconnect windows in mind.

## Framework Support

| Target | Version |
|---|---|
| .NET (server + client) | .NET 8 / .NET 9 / .NET 10 |
| .NET Framework (client) | 4.6.2+ (via `Cocoar.SignalARRR.Client.FullFramework`) |
| TypeScript / JavaScript | Node.js 22 / modern browsers |
| Swift (iOS / macOS) | Swift 5.10+, iOS 14+ / macOS 11+ |

## Building from Source

```bash
# .NET
dotnet build src/Cocoar.SignalARRR.slnx
dotnet test src/Cocoar.SignalARRR.slnx

# TypeScript
cd src/Cocoar.SignalARRR.Typescript && npm install && npm run build

# Swift
swift build && swift test
```

## License

Apache License 2.0 — see [LICENSE](LICENSE) for details.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.
