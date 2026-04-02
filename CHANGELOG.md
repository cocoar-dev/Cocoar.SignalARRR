# Changelog

All notable changes to SignalARRR will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [4.2.0]

### Added
- **Transport-Level Authentication**: Support for client certificates (mTLS), cookies, and Windows/Negotiate authentication alongside existing token-based auth. The server auto-detects the authentication mode per client and re-validates transport credentials server-side when the auth cache expires — no challenge round-trip needed.
- **`AuthenticationMode` enum**: `None`, `MessageLevel`, `TransportLevel` — exposed on `ClientContext.AuthMode` for per-client auth mode inspection
- **`ClientContext.ClientCertificate`**: Stores the client certificate from the TLS handshake for server-side re-validation
- **`ITransportAuthRevalidationService`**: Pluggable interface for custom transport-auth re-validation logic (e.g., custom CRL endpoints, OCSP stapling, session store checks)
- **`DefaultTransportAuthRevalidationService`**: Built-in implementation with certificate expiry checks, X509 chain validation (CRL/OCSP), and custom validator callback support
- **Certificate validation options**: `WithCertificateRevocationCheck(bool)`, `WithCertificateRevocationMode(X509RevocationMode)`, `WithCustomCertificateValidator(Func<X509Certificate2, bool>)` on `SignalARRRServerOptionsBuilder`
- **Mixed-mode authentication**: A single hub can serve both token-based and certificate-based clients simultaneously
- **Certificate refresh on reconnect**: `ClientContext` updates transport credentials (cert + principal) when a client reconnects with a new certificate
- **9 new integration tests** for transport-level auth: cert auth, auto-detection, cache expiry with server-side re-validation, expired cert rejection, AllowAnonymous bypass, and mixed-mode scenarios

### Fixed
- **Source Generator cross-assembly discovery**: The Source Generator now discovers `[SignalARRRContract]` interfaces in referenced assemblies (not just in the current compilation's source code). This fixes the common scenario where contract interfaces are defined in a shared library and referenced by server/client projects. The generator only scans assemblies that reference `Cocoar.SignalARRR.Contracts`, avoiding unnecessary work.

---

## [4.1.0]

### Added
- **Multi-targeting**: All library packages now support `net8.0`, `net9.0`, and `net10.0`
- **`Cocoar.SignalARRR.Client.FullFramework`**: New client package targeting `netstandard2.0` for .NET Framework 4.6.2+ — typed proxies via `DispatchProxy`, streaming (via `Microsoft.Bcl.AsyncInterfaces` polyfill), server-to-client RPC, cancellation, file transfer, and optional MessagePack support
- **Framework-conditional package versions**: ASP.NET Core packages automatically resolve to the correct version per target framework (8.0.x / 9.0.x / 10.0.x), preventing transitive `MissingMethodException` on older runtimes
- **CI matrix testing**: PR validation now tests on .NET 8, 9, and 10 across Ubuntu, Windows, and macOS
- **Typed broadcasts**: `WithHub<T>().WithGroup().SendAsync<T>()` — chainable LINQ-style API for typed fire-and-forget sends to groups, filtered clients, and all clients
- **Group tracking**: `ClientManager.AddToGroupAsync/RemoveFromGroupAsync` syncs both SignalR groups and `ClientContext.Groups` for queryable group membership
- **Typed multi-client invoke**: `InvokeAllAsync<T, TResult>()` (parallel, all results) and `InvokeOneAsync<T, TResult>()` (first responder wins) on `IEnumerable<ClientContext>`
- **Documentation**: "vs. gRPC vs. REST" comparison page, updated client comparison with all four clients, MessagePack install instructions

### Changed
- **MessagePack is now optional**: `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` is no longer a dependency of `Cocoar.SignalARRR.Client`. Install it separately and call `.AddMessagePackProtocol()` when needed — same as with raw SignalR.
- **`Cocoar.SignalARRR.Common`** now also targets `netstandard2.0` (with `System.Text.Json` NuGet polyfill)

### Fixed
- **SignalR 10.0.5 compatibility**: Client-side `InvokeServerMessage` handler now properly awaits async operations instead of fire-and-forget (was causing connection drops on SignalR 10.0.5)
- **`npm version` in CI**: Added `--allow-same-version` flag to prevent failures when calculated version matches `package.json`

---

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
