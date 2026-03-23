# Changelog

All notable changes to SignalARRR will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [4.0.0]

### Swift Client — Complete Rewrite (v4)

The Swift client has been rewritten from scratch. Microsoft's `signalr-client-swift` has been replaced by a native implementation with no external dependencies.

#### Breaking Changes (Swift)
- `HARRRConnection.create { builder in ... }` replaced by `HARRRConnection.create(url:)` with named parameters
- `asSignalRHubConnection()` removed — no underlying `HubConnection` exists anymore
- `invoke<T>` and `stream<T>` now require `T: Decodable` constraint

#### Added (Swift)
- **Native SignalR client** (`SignalRWebSocketClient`) — replaces `signalr-client-swift`, zero external dependencies
- **MessagePack protocol** — `hubProtocol: .messagepack` parameter, fully implemented without any library
- **Multiple transports** — WebSockets, Server-Sent Events, Long Polling with automatic fallback
- **Automatic reconnection** — configurable `ReconnectPolicy` with custom retry delays
- **Handshake timeout** — `handshakeTimeout` parameter, defaults to 15s
- **`UnauthorizedException`** — thrown when the server rejects authentication
- **`os_log` logging** — `logLevel: SignalRLogLevel` parameter, visible in Xcode and Console.app
- **`HubProtocolKind`** — public enum `.json` / `.messagepack` for protocol selection
- **`TransportType`** — public enum `.webSockets` / `.serverSentEvents` / `.longPolling`
- **Concurrent handler dispatch** — each server→client invocation runs in its own `Task`, fixing the fundamental concurrency bug in `signalr-client-swift` where `CancelTokenFromServer` could be blocked by a running handler
- **31 integration tests** — including 5 new MessagePack tests covering invoke, guid, send, echo, and streaming

#### Fixed (Swift)
- `CancelTokenFromServer` now dispatched immediately even while other handlers are running (was blocked by Actor serialization in `signalr-client-swift`)
- Cancellation tests complete in ~0.2s instead of timing out at 30s

### Breaking Changes
- Target framework changed from `netstandard2.0` to `net10.0`
- `ImpromptuInterface` removed — replaced by source-generated proxies and opt-in `DispatchProxy` fallback
- Proxy creation now requires `[SignalARRRContract]` attribute on interfaces (or reference `Cocoar.SignalARRR.DynamicProxy` for runtime fallback)
- Custom `IAuthenticator` interface removed — use ASP.NET Core authentication handlers and `[Authorize]` policies instead
- HTTP Proxy pass-through feature removed (deferred to Phase 2)
- `netstandard2.0` target dropped — all packages now target `net10.0` (except SourceGenerator which targets `netstandard2.0` per Roslyn requirements)
- Hub-level `[Authorize]` inheritance restored: if the Hub class has `[Authorize]`, ServerMethods classes inherit it automatically (behavior change from v2.x where this was disabled)

### Added
- **Source Generator**: Compile-time proxy generation from `[SignalARRRContract]` interfaces — zero reflection, AOT-friendly
- **`Cocoar.SignalARRR.Contracts`**: Single-reference meta-package for shared interface projects (attribute + generator + ProxyGenerator)
- **`Cocoar.SignalARRR.DynamicProxy`**: Opt-in `DispatchProxy`-based runtime fallback for plugin/dynamic scenarios
- **CancellationToken server-to-client propagation**: Server can pass `CancellationToken` to client methods and cancel remotely
- **`ServerProxyCreatorHelper.StreamAsync<T>`**: Server can request `IAsyncEnumerable<T>` streams from client methods
- **`ServerStreamManager`**: Channel-based stream correlation for server-initiated client streams
- **`StreamItemToServer` / `StreamCompleteToServer`** hub methods for client-to-server stream item delivery
- **`ClientManager` typed extensions**: `GetTypedMethods<T>(connectionId)` for server-to-client RPC from outside hub context
- **Authorization integration tests**: Tests for authenticated calls, unauthenticated rejection, and hub-level auth inheritance
- **`TreatWarningsAsErrors`**: Enabled globally via `Directory.Build.props`

### Removed
- `ImpromptuInterface` dependency
- `netstandard2.0` target / polyfill packages (`Microsoft.Bcl.AsyncInterfaces`, `System.Threading.Channels`, etc.)
- `SignalARRRDynamicProxy.cs` and `StreamingType.cs` from ProxyGenerator (replaced by DispatchProxy package)
- Non-generic `Invoke(Type returnType, ...)` overloads from `ClientProxyCreatorHelper` and `ServerProxyCreatorHelper` (unused; DynamicProxy dispatches to generic methods)
- Old `RegisterMethods` client-side registration API (replaced by `RegisterInterface`)
- Custom `IAuthenticator` interface and `TryAuthenticate`/`SetAuthData` on `ClientContext`
- Dead commented-out code throughout the codebase

---

## [2.1.2] - Previous Release

### Features
- Split hub methods across multiple classes via `ServerMethods<T>`
- Method-level authorization with `[Authorize]` attribute
- Continuous token validation with automatic challenge/refresh
- Server-to-client RPC with response awaiting
- Support for `IObservable<T>`, `IAsyncEnumerable<T>`, and `ChannelReader<T>` streaming
- Type-safe client proxies from interfaces
- Multi-platform support (Server, .NET Client, TypeScript Client)
