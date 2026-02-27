# SignalARRR v4.0 - Implementation Progress

> See [FEATURE_SPEC.md](FEATURE_SPEC.md) for full details on each item.

---

## Phase 1: Foundation (v4.0)

- [x] **1.1** Source generator for proxy generation (replace ImpromptuInterface)
  - [x] Create `Cocoar.SignalARRR.Contracts` project (`[SignalARRRContract]` attribute)
  - [x] Create `Cocoar.SignalARRR.SourceGenerator` project (Roslyn incremental generator)
  - [x] Generate proxy classes from `[SignalARRRContract]` interfaces (all return types: void, Task, Task\<T\>, sync, ChannelReader, IObservable, IAsyncEnumerable)
  - [x] Generate `[ModuleInitializer]` registration (auto-registers proxy factories at assembly load)
  - [x] Add factory registry to `ProxyCreator` with ImpromptuInterface fallback
  - [x] Wire Contracts as meta-package (bundles SourceGenerator + ProxyGenerator — single reference for consumers)
  - [x] Contracts + SourceGenerator referenced transitively from Client and Server
  - [x] Annotate test interfaces (`ITestHub`, `ITestClientMethods`, `IGeneric`, `ISharedMethods`, `IStringMethods`, `ITestServerMethods`)
  - [x] Build succeeds, generated proxies verified, unit tests pass
  - [x] Integration tests for source-generated proxies (end-to-end with real SignalR — 19 tests pass)
  - [x] Unit tests verifying `[ModuleInitializer]` registration (`ProxyCreator.HasFactory<T>()` for all interfaces)
  - [ ] Generate `JsonSerializerContext` for AOT-compatible serialization (moved to 1.5)
- [ ] **1.2** Create `Cocoar.SignalARRR.DynamicProxy` package (DispatchProxy-based runtime fallback)
  - [ ] Replace `ImpromptuInterface` + `DynamicObject` with `DispatchProxy`
  - [ ] Add `[RequiresDynamicCode]` annotations
  - [ ] Add `[FeatureGuard]` for AOT trimming
  - [ ] Integration tests for dynamic proxies
- [x] **1.3** Drop `netstandard2.0`, target `net10.0`
  - [x] Update all `.csproj` target frameworks to `net10.0`
  - [x] Remove polyfill packages (`Microsoft.Bcl.AsyncInterfaces`, `System.Threading.Channels`, `System.Net.Http`, `System.Interactive.Async`)
  - [x] Replace `AsyncEnumerable.ToObservable()` with `ProxyCreatorHelper.ToObservable()` (using `System.Reactive`)
  - [ ] Remove `#if NETSTANDARD` conditionals if any
- [ ] **1.4** Remove `ImpromptuInterface` + `Cocoar.Reflectensions` dependencies
  - [ ] Replace `Reflectensions` type conversion with `System.Text.Json` / generated code
  - [ ] Replace `InvokeHelper` with generated dispatch
  - [ ] Replace reflection-based property injection in `ServerMethods<T>`
  - [ ] Remove packages from all `.csproj` files
  - [ ] Remove `ProxyCreator` ImpromptuInterface fallback (all proxies now source-generated)
- [ ] **1.5** `System.Text.Json` source-generated serialization
  - [ ] Create `JsonSerializerContext` for `ClientRequestMessage`, `ServerRequestMessage`
  - [ ] Replace any remaining `Newtonsoft.Json` usage
  - [ ] Configure `JsonHubProtocolOptions` with source-generated context
  - [ ] Use `JsonSerializerOptions.Strict` as default
- [ ] **1.6** Native AOT compatibility
  - [ ] Add `<IsAotCompatible>true</IsAotCompatible>` to all projects
  - [ ] Fix all trimming/AOT analyzer warnings
  - [ ] Document AOT limitations (value-type streaming)
  - [ ] Verify with `dotnet publish -r <rid> /p:PublishAot=true`
- [ ] **1.7** CancellationToken server-to-client propagation (restore)
  - [ ] Create `CancellationTokenReference` in `Common.RemoteReferenceTypes`
  - [ ] Implement `MethodArgumentPreperator.PrepareCancellationToken()`
  - [ ] Client-side: create local `CancellationTokenSource` from reference
  - [ ] Client-side: handle `CancelTokenFromServer` message
  - [ ] Integration tests
- [ ] **1.8** `ServerProxyCreatorHelper.StreamAsync` (restore)
  - [ ] Implement server-to-client streaming via proxy
  - [ ] Support `IAsyncEnumerable<T>` return from client methods
  - [ ] Integration tests

---

## Phase 2: Power Features (v4.1)

- [ ] **2.1** HTTP Proxy pass-through system (restore)
- [ ] **2.2** Distributed tracing / OpenTelemetry integration
- [ ] **2.3** Metrics + Health checks
- [ ] **2.4** RPC interceptors/middleware pipeline
- [ ] **2.5** Method overloading support
- [ ] **2.6** Configurable auth cache duration

---

## Phase 3: Ecosystem (v4.2+)

- [ ] **3.1** TypeScript client (auto-generated from contracts)
- [ ] **3.2** OpenAPI contract metadata endpoint
- [ ] **3.3** Rate limiting
- [ ] **3.4** Connection resilience (auto-reconnect queue)
- [ ] **3.5** Distributed client manager (Redis)
- [ ] **3.6** Client groups with metadata

---

## Consumer Usage

```
MyApp.Shared   → references Cocoar.SignalARRR.Contracts  (1 package — includes attribute + source generator + ProxyGenerator)
MyApp.Server   → references Cocoar.SignalARRR.Server + MyApp.Shared
MyApp.Client   → references Cocoar.SignalARRR.Client + MyApp.Shared
```

In `MyApp.Shared`, just annotate interfaces:
```csharp
[SignalARRRContract]
public interface IChatHub {
    Task SendMessage(string message);
    Task<List<string>> GetHistory();
    IObservable<string> StreamMessages();
}
```

Proxies are generated at compile time. `GetTypedMethods<IChatHub>()` picks them up automatically.

> **Project-reference caveat**: Analyzers don't flow transitively via `<ProjectReference>`. When developing against source (not NuGet), projects that define `[SignalARRRContract]` interfaces also need an explicit SourceGenerator analyzer reference. This is only needed during development; the NuGet package bundles the analyzer correctly.

---

## Decisions Log

| Date | Decision | Rationale |
|---|---|---|
| 2026-02-27 | Dual proxy strategy: Source Generator + DispatchProxy | Plugins need JIT anyway; AOT for compile-time path only |
| 2026-02-27 | `DispatchProxy` over ImpromptuInterface | BCL, zero deps, direct MethodInfo access |
| 2026-02-27 | Separate `DynamicProxy` package | Explicit opt-in, keeps core AOT-clean |
| 2026-02-27 | Target .NET 10 (LTS) | Drop netstandard2.0, full modern API access |
| 2026-02-27 | No Newtonsoft.Json | System.Text.Json with source gen only |
| 2026-02-27 | Contracts as meta-package | Bundles attribute + SourceGenerator + ProxyGenerator dep — single reference for consumer shared libs |
| 2026-02-27 | `ProxyCreatorHelper.ToObservable()` | Replaces `System.Linq.Async` bridge (conflicts with .NET 10 BCL `System.Linq.AsyncEnumerable`) |
| 2026-02-27 | SourceGenerator stays netstandard2.0 | Roslyn requirement — generator DLL must target netstandard2.0 |
| 2026-02-27 | Explicit analyzer ref for project-reference dev | Analyzers don't flow transitively via `<ProjectReference>`; NuGet `analyzers/` folder works automatically |

---

## New Projects

| Project | Target | Role |
|---|---|---|
| `Cocoar.SignalARRR.Contracts` | net10.0 | `[SignalARRRContract]` attribute + bundles SourceGenerator as analyzer + depends on ProxyGenerator |
| `Cocoar.SignalARRR.SourceGenerator` | netstandard2.0 | Roslyn incremental source generator producing proxy classes + `[ModuleInitializer]` registration |

## Key Files

| File | What it does |
|---|---|
| `SourceGenerator/SignalARRRGenerator.cs` | Entry point — `ForAttributeWithMetadataName` pipeline |
| `SourceGenerator/Emitters/ProxyEmitter.cs` | Generates proxy class per interface |
| `SourceGenerator/Emitters/RegistrationEmitter.cs` | Generates `[ModuleInitializer]` that registers all factories |
| `SourceGenerator/Helpers/ReturnTypeClassifier.cs` | Maps return types to dispatch method (Send/Invoke/Stream etc.) |
| `ProxyGenerator/ProxyCreator.cs` | Factory registry with `RegisterFactory<T>()` + ImpromptuInterface fallback |
| `ProxyGenerator/ProxyCreatorHelper.cs` | Abstract base class — dispatch target for generated proxies |
