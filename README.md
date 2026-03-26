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
