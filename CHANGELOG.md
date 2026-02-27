# Changelog

All notable changes to SignalARRR will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Breaking Changes
- Target framework changed from `netstandard2.0` to `net10.0`
- `ImpromptuInterface` removed — replaced by source-generated proxies and opt-in `DispatchProxy` fallback
- Proxy creation now requires `[SignalARRRContract]` attribute on interfaces (or reference `Cocoar.SignalARRR.DynamicProxy` for runtime fallback)

### Added
- **Source Generator**: Compile-time proxy generation from `[SignalARRRContract]` interfaces — zero reflection, AOT-friendly
- **`Cocoar.SignalARRR.Contracts`**: Single-reference meta-package for shared interface projects (attribute + generator + ProxyGenerator)
- **`Cocoar.SignalARRR.DynamicProxy`**: Opt-in `DispatchProxy`-based runtime fallback for plugin/dynamic scenarios
- **CancellationToken server-to-client propagation**: Server can pass `CancellationToken` to client methods and cancel remotely
- **`ServerProxyCreatorHelper.StreamAsync<T>`**: Server can request `IAsyncEnumerable<T>` streams from client methods
- **`ServerStreamManager`**: Channel-based stream correlation for server-initiated client streams
- **`StreamItemToServer` / `StreamCompleteToServer`** hub methods for client-to-server stream item delivery
- **`ClientManager` typed extensions**: `GetTypedMethods<T>(connectionId)` for server-to-client RPC from outside hub context

### Removed
- `ImpromptuInterface` dependency
- `netstandard2.0` target / polyfill packages (`Microsoft.Bcl.AsyncInterfaces`, `System.Threading.Channels`, etc.)
- `SignalARRRDynamicProxy.cs` and `StreamingType.cs` from ProxyGenerator (replaced by DispatchProxy package)

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
