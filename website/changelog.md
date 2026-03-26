# Changelog

All notable changes to SignalARRR will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [4.1.0]

### Added
- **Multi-targeting**: All library packages now support `net8.0`, `net9.0`, and `net10.0`
- **`Cocoar.SignalARRR.Client.FullFramework`**: New client package targeting `netstandard2.0` for .NET Framework 4.6.2+ — typed proxies via `DispatchProxy`, streaming (via polyfills), server-to-client RPC, cancellation, file transfer, and optional MessagePack
- **Framework-conditional package versions**: ASP.NET Core packages resolve to the correct version per target framework, preventing transitive version conflicts
- **CI matrix testing**: PR validation now tests on .NET 8, 9, and 10 across all platforms

### Changed
- **MessagePack is now optional**: Install `Microsoft.AspNetCore.SignalR.Protocols.MessagePack` separately and call `.AddMessagePackProtocol()` when needed
- **`Cocoar.SignalARRR.Common`** now also targets `netstandard2.0`

### Fixed
- **SignalR 10.0.5 compatibility**: `InvokeServerMessage` handler now properly awaits async operations
- **`npm version` in CI**: Added `--allow-same-version` flag

---

## [4.0.0]

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
- Non-generic `Invoke(Type returnType, ...)` overloads from `ClientProxyCreatorHelper` and `ServerProxyCreatorHelper`
- Old `RegisterMethods` client-side registration API (replaced by `RegisterInterface`)
- Custom `IAuthenticator` interface and `TryAuthenticate`/`SetAuthData` on `ClientContext`

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
