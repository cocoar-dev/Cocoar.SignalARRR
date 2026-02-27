# SignalARRR Feature Specification & Modernization Roadmap

> Target: .NET 10 (LTS) | C# 14 | Full Native AOT compatibility

---

## 0. PACKAGE ARCHITECTURE (v4.0)

### Package Structure

```
Cocoar.SignalARRR.Contracts            -- Attributes, message types, shared interfaces
                                          Target: net10.0
                                          Dependencies: minimal (System.Text.Json)

Cocoar.SignalARRR.SourceGenerator      -- Roslyn source generator (compile-time only)
                                          Target: netstandard2.0 (required by Roslyn)
                                          Output: proxy classes, dispatch code, JSON contexts
                                          Packed as analyzer into Client/Server packages

Cocoar.SignalARRR.DynamicProxy         -- Runtime proxy via DispatchProxy (EXPLICIT OPT-IN)
                                          Target: net10.0
                                          For: plugin scenarios, runtime-loaded DLLs
                                          Annotated: [RequiresDynamicCode] (trimmed away under AOT)

Cocoar.SignalARRR.Server               -- Hub, ServerMethods<T>, auth, streaming, HTTP bridge
                                          Target: net10.0
                                          References: Contracts + SourceGenerator (compile-time)

Cocoar.SignalARRR.Client               -- HARRRConnection, client message handling
                                          Target: net10.0
                                          References: Contracts + SourceGenerator (compile-time)
```

### Proxy Generation: Dual Strategy

| Path | Mechanism | AOT? | Use Case |
|---|---|---|---|
| **Source Generator** (primary) | Roslyn compile-time codegen | Yes | Interfaces known at compile time |
| **DispatchProxy** (runtime fallback) | BCL `System.Reflection.DispatchProxy` | No (JIT only) | Plugin-loaded interfaces at runtime |

**Key insight:** Plugins inherently require JIT (dynamic DLL loading), so the runtime
proxy path does NOT need AOT compatibility. This allows clean separation:
- Source generator path: zero-reflection, AOT-safe, optimal performance
- DispatchProxy path: explicit opt-in package, JIT-only, for dynamic scenarios

**Why DispatchProxy replaces ImpromptuInterface:**
- Built into BCL (zero external dependency, Microsoft-maintained)
- Direct `MethodInfo` access (no fragile DLR reflection hacks)
- `TryInvokeMember` maps 1:1 to `DispatchProxy.Invoke(MethodInfo, object[])`

**Feature guards for AOT trimming:**
```csharp
public static class SignalARRRProxyFactory
{
    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    public static bool SupportsRuntimeProxies
        => RuntimeFeature.IsDynamicCodeSupported;
}
```
When published with AOT, the trimmer removes the entire dynamic proxy code path automatically.

---

## 1. CORE ARCHITECTURE

### 1.1 HARRR Hub Base Class [EXISTS]
The enhanced SignalR Hub base class that routes incoming messages to the method dispatch system.

**Current:** Extends `Hub`, provides `OnConnectedAsync`/`OnDisconnectedAsync` lifecycle, 4 message entry points (`InvokeMessage`, `InvokeMessageResult`, `SendMessage`, `StreamMessage`), creates `MessageHandler` per request.

**Modernization:**
- Drop Newtonsoft.Json dependency entirely, use `System.Text.Json` with source-generated `JsonSerializerContext`
- Add `ActivitySource` integration for distributed tracing (hook into `Microsoft.AspNetCore.SignalR.Server`)
- Consider making HARRR non-abstract with convention-based configuration

### 1.2 ServerMethods\<T\> Pattern [EXISTS]
Split hub logic into multiple organized classes, each scoped to a specific hub type.

**Current:** Properties (ClientContext, Clients, Groups, Logger) injected via reflection after DI instantiation. Methods discovered via assembly scanning at startup.

**Modernization:**
- Replace reflection-based property injection with constructor injection or `[FromKeyedServices]`
- Use source generator to generate the dispatch code at compile time instead of runtime reflection
- Consider using `IServiceProvider` scoped injection pattern instead of manual property setting

### 1.3 MessageHandler & Method Dispatch [EXISTS]
Routes incoming `ClientRequestMessage` to the correct method, handles auth, builds parameters, invokes.

**Current:** Heavy use of `Cocoar.Reflectensions` for type conversion, `InvokeHelper` for dynamic invocation. Method lookup via `ConcurrentDictionary`. Interface methods detected by `|` delimiter in method name.

**Modernization:**
- **Source generator** should generate strongly-typed dispatch methods at compile time, eliminating runtime reflection entirely
- Use `System.Text.Json` polymorphic deserialization for argument conversion
- Replace `par.Reflect().To(parameterType)` with generated type-safe converters
- Method overloading support (currently only one method per name)

### 1.4 Wire Protocol / Message Types [EXISTS]
`ClientRequestMessage` (client->server) and `ServerRequestMessage` (server->client) as the RPC envelope format.

**Current:** `{ Method, Arguments[], GenericArguments[], Authorization }` serialized as JSON via SignalR.

**Modernization:**
- Add `[JsonPolymorphic]` / `[JsonDerivedType]` for polymorphic argument types
- Add versioning field for protocol evolution
- Use `JsonSerializerOptions.Strict` to catch contract mismatches
- Source-generated `JsonSerializerContext` for AOT-compatible serialization
- Consider dropping `GenericArguments` as string[] in favor of compile-time generic resolution via source generator

---

## 2. CLIENT-SIDE

### 2.1 HARRRConnection [EXISTS]
Wrapper around `HubConnection` that adds SignalARRR protocol handling.

**Current:** Registers handlers for `ChallengeAuthentication`, `InvokeServerRequest`, `InvokeServerMessage`, `CancelTokenFromServer`. Attaches auth tokens to every outgoing message. Delegates most properties/methods to inner `HubConnection`.

**Modernization:**
- Drop `netstandard2.0` target - target `net10.0` only (or `net10.0` + `net8.0` at minimum)
- Remove `SimpleAsyncHelper.RunSync()` anti-pattern (sync-over-async)
- Use `WebSocketStream` (.NET 10) for custom transport scenarios
- Integrate with `ActivitySource` for client-side distributed tracing

### 2.2 Client-Side MessageHandler [EXISTS]
Processes server-initiated method calls on the client.

**Current:** Resolves registered interface implementations, invokes methods, sends response back.

**Modernization:**
- Source-generated dispatch instead of reflection-based invocation
- Strongly-typed error handling with proper exception serialization

### 2.3 TypeScript/JavaScript Client [LOST - RESTORE]
Complete TS/JS client wrapping `@microsoft/signalr`.

**Old implementation:** `HARRRConnection` class, `ClientResponseMessage`, `onServerRequest` handler, `ChallengeAuthentication` handling. Deleted in project restructure (`3796372`).

**Modernization:**
- Rewrite from scratch in modern TypeScript with proper npm package structure
- Auto-generate TypeScript client types from .NET interfaces via source generator
- Use modern `@microsoft/signalr` API (v8+)
- Consider generating OpenAPI-compatible contract descriptions

---

## 3. PROXY GENERATION [EXISTS - MAJOR REWRITE NEEDED]

### 3.1 Current: DynamicObject + ImpromptuInterface
`SignalARRRDynamicProxy<T>` uses `DynamicObject.TryInvokeMember()` intercepted by `Impromptu.ActLike<T>()`.

**Problems:**
- **NOT compatible with Native AOT** (requires `System.Reflection.Emit`)
- **NOT trimmable** (dynamic dispatch is opaque to the trimmer)
- Runtime overhead from dynamic dispatch on every call
- No compile-time validation of interface contracts
- `Cocoar.Reflectensions` dependency adds more reflection

### 3.2 New: Roslyn Source Generator [NEW - REPLACE]
Generate concrete proxy classes at compile time from interfaces marked with `[SignalARRRContract]` (or similar attribute).

**What it generates:**
```csharp
// User defines:
[SignalARRRContract]
public interface IMyService {
    Task<string> GetName(int id);
    IAsyncEnumerable<int> StreamCounts(int n, CancellationToken ct);
}

// Source generator produces:
public sealed class IMyServiceClientProxy : IMyService {
    private readonly HARRRConnection _connection;

    public async Task<string> GetName(int id) {
        var msg = new ClientRequestMessage("Namespace.IMyService|GetName", new object[] { id });
        return await _connection.InvokeCoreAsync<string>(msg);
    }

    public IAsyncEnumerable<int> StreamCounts(int n, CancellationToken ct) {
        var msg = new ClientRequestMessage("Namespace.IMyService|StreamCounts", new object[] { n });
        return _connection.StreamAsyncCore<int>(msg, ct);
    }
}

// And the server-side dispatch:
public sealed class IMyServiceServerDispatch {
    public static async Task<object?> Dispatch(IMyService impl, string method, object[] args) {
        return method switch {
            "GetName" => await impl.GetName((int)args[0]),
            "StreamCounts" => throw new InvalidOperationException("Use streaming dispatch"),
            _ => throw new MethodNotFoundException(method)
        };
    }
}
```

**Benefits:**
- Full Native AOT compatibility
- Compile-time contract validation
- Zero reflection at runtime
- IntelliSense and compile errors for mismatched contracts
- Trimmable
- Can generate TypeScript types alongside

**Dual-mode strategy:** Use `[FeatureSwitchDefinition]` / `[FeatureGuard]` to keep the dynamic proxy path for JIT scenarios (backward compat) while the source generator path is the primary recommendation.

---

## 4. AUTHENTICATION & AUTHORIZATION

### 4.1 Per-Method Authorization [EXISTS]
`[Authorize]` on ServerMethods classes and individual methods, evaluated per call.

**Current:** `SignalARRRAuthentication.Authorize()` combines policies via `AuthorizationPolicy.CombineAsync()`, evaluates with `IPolicyEvaluator`.

**Modernization:**
- Use .NET 10 authentication metrics for observability
- Leverage 401/403 behavior for API endpoints (.NET 10 default)

### 4.2 Continuous Token Re-validation [EXISTS]
3-minute token cache with challenge/refresh flow when stale.

**Current:** `ClientContext.UserValidUntil` tracks expiry. On stale token, sends `ChallengeAuthentication` to client, awaits fresh token, re-authenticates.

**Modernization:**
- Make cache duration configurable (currently hardcoded 3 minutes)
- Add token refresh event/callback for monitoring
- Support sliding vs absolute expiration
- Consider SignalR's built-in token refresh capabilities

### 4.3 Access Token Middleware [EXISTS]
`SignalARRRAccessTokenValidationMiddleware` bridges query-string `?access_token=` to `Authorization: Bearer` header.

**Modernization:**
- This is a standard pattern - verify if ASP.NET Core 10 handles this natively now
- If not, keep but simplify

---

## 5. STREAMING

### 5.1 Three-Paradigm Streaming [EXISTS]
`IObservable<T>`, `IAsyncEnumerable<T>`, `ChannelReader<T>` all supported, normalized to `IAsyncEnumerable<object>` for transport.

**Current:** `StreamingResult<T>` wraps all types. Per-item authentication check before yielding. `ObservableExtensions.AsChannelReaderInternal()` converts observables.

**Modernization:**
- Use `ValueTask`-based async enumeration where possible
- Consider AOT limitation: `IAsyncEnumerable<T>` and `ChannelReader<T>` with value types NOT supported under AOT
- Add backpressure support for `IObservable<T>` conversion (currently unbounded channels)
- Support `ServerSentEvents` (.NET 10) as alternative transport for one-way streaming

### 5.2 Server-to-Client Streaming via Proxy [LOST - RESTORE]
`ServerProxyCreatorHelper.StreamAsync<T>()` - currently `throw new NotImplementedException()`.

**What it should do:** Allow server to call a streaming method on a client and receive `IAsyncEnumerable<T>` back.

**Implementation approach:**
- Source generator generates streaming dispatch code
- Use SignalR's native `StreamAsChannelCoreAsync` for transport
- Handle backpressure and cancellation properly

---

## 6. BIDIRECTIONAL RPC

### 6.1 Server-to-Client Method Invocation [EXISTS]
Server invokes client methods and awaits responses via `InvokeCoreAsync` (SignalR 3.0+).

**Current:** `ClientContext.GetTypedMethods<T>()` creates proxy, `ServerProxyCreatorHelper` dispatches via `ClientContextDispatcher<THub>`.

**Modernization:**
- Source-generated proxies for server-to-client calls too
- Better error propagation (currently wraps in `HARRRException`)
- Timeout configuration per method call

### 6.2 CancellationToken Propagation Server->Client [LOST - RESTORE]
Allow server to cancel long-running client operations remotely.

**Old state:** `MethodArgumentPreperator` has a TODO. `CancelTokenFromServer` wire protocol method exists. Missing: `CancellationTokenReference` class.

**Implementation plan:**
1. Create `CancellationTokenReference` in `Common.RemoteReferenceTypes`
   - Contains `Guid` reference ID
2. Server side (`MethodArgumentPreperator`):
   - Convert `CancellationToken` argument to `CancellationTokenReference`
   - Register cancellation callback to send `CancelTokenFromServer` message
3. Client side:
   - Receive `CancellationTokenReference`, create local `CancellationTokenSource`
   - On `CancelTokenFromServer` message, cancel the local source
4. Source generator handles the plumbing

**Use case:** Video conversion farm - cancel remote worker when user cancels in UI.

---

## 7. HTTP-TO-SIGNALR BRIDGE

### 7.1 Client Manager REST API [EXISTS]
`ClientManager` accessible from HTTP controllers/endpoints for invoking connected clients.

**Current:** `GetAllClients()`, `GetHARRRClients<THub>()`, `WithAttribute()` filtering, `InvokeAllAsync<T>()` broadcast, `InvokeOneAsync<T>()` failover, `GetTypedMethods<T>()` typed proxy.

**Modernization:**
- Add OpenAPI metadata for auto-generated HTTP endpoints
- Use minimal API conventions instead of controller-based patterns
- Consider auto-generating HTTP endpoints from interface definitions

### 7.2 HTTP Proxy Pass-through [LOST - RESTORE]
HTTP request forwarded to SignalR client, client response streamed back to HTTP caller.

**Old implementation:**
- `ServerRequestManager` with `TaskCompletionSource<JToken>` for pending requests
- `InvokeResponse` endpoint at `{hub}/response/{id}` received client HTTP POST responses
- `ServerClassCreatorProxyHelper` handled proxy-specific dispatch
- `ProxyClientAsync` on `IClientContextDispatcher`
- Supported `RequestType.Default` (normal RPC) and `RequestType.Proxy` (HTTP pass-through)

**Modernization:**
- Rewrite using modern `TaskCompletionSource<T>` (non-generic JToken was Newtonsoft-specific)
- Use `System.Text.Json` `JsonElement` instead of `JToken`
- Use minimal API endpoint instead of MVC controller for the response endpoint
- Add timeout and circuit breaker patterns
- Consider using `IAsyncEnumerable` for streaming proxy responses
- Add proper `IDisposable` cleanup for abandoned requests

### 7.3 Stream Download Endpoint [EXISTS]
Auto-registered `GET {hub}/download/{id}` for large file transfers via `ServerPushStreamManager`.

**Modernization:**
- Add range request support (resume downloads)
- Add content-type detection
- Consider using `IResult` return types

### 7.4 HttpContext Response Helpers [EXISTS]
`HttpContextWriteActionExtensions` provides fluent response writing (`Ok()`, `BadRequest()`, `File()`, etc).

**Modernization:**
- Consider replacing with `TypedResults` from minimal APIs (.NET 10)
- Much of this may be unnecessary if using minimal API patterns

---

## 8. CLIENT CONTEXT & CONNECTION MANAGEMENT

### 8.1 ClientContext [EXISTS]
Per-connection state: ID, RemoteIP, User, ConnectedAt, ReconnectedAt, Attributes, HARRRType.

**Current:** Custom attributes from headers (`#` prefix) and query params (`@` prefix).

**Modernization:**
- Add `IDisposable`/`IAsyncDisposable` for cleanup
- Add connection metadata dictionary (typed, not just string/string)
- Add groups tracking (which groups the client is in)
- Add last-activity timestamp for idle detection

### 8.2 InMemoryHARRRClientManager [EXISTS]
`ConcurrentDictionary<string, ClientContext>` singleton.

**Current:** Simple in-memory storage. No distributed support.

**Modernization:**
- Extract `IHARRRClientManager` properly for distributed implementations (Redis, SQL, etc.)
- Add events for connect/disconnect/reconnect (observable)
- Add client enumeration with filtering/paging
- Consider using `FrozenDictionary` for read-heavy scenarios (where applicable)

---

## 9. TYPE SYSTEM & CONTRACTS

### 9.1 Interface-Based Contracts [EXISTS]
Shared interfaces define the RPC contract between client and server.

**Current:** `ISignalARRRMethodsCollection`, `ISignalARRRInterfaceCollection`, `ClientInterfaceMethodsCache`, `ClientMethodsCache`. Method naming: `"Namespace.IInterface|MethodName"`.

**Modernization:**
- `[SignalARRRContract]` attribute marks interfaces for source generation
- `[SignalARRRMethod("custom_name")]` replaces `[MessageName]` (which was never actively used)
- Source generator validates contracts at compile time
- Generate JSON schemas for contracts (`JsonSchemaExporter` .NET 9)

### 9.2 TypeHelper Cross-Wire Type Resolution [EXISTS]
`TypeHelper.FindType()` scans `AppDomain.CurrentDomain.GetAssemblies()` to resolve type strings.

**Current:** Thread-safe cache, 3-tier resolution (System types, case-sensitive, case-insensitive fallback).

**Modernization:**
- For source-generated code: eliminate entirely (types known at compile time)
- For dynamic fallback: keep but add assembly load context awareness
- AOT: type resolution by string is inherently problematic - source generator should resolve all types at compile time

### 9.3 Generic Method Support [EXISTS]
`GenericArguments` as `string[]` in messages, resolved via `TypeHelper.FindType()` + `MakeGenericMethod()`.

**Modernization:**
- Source generator generates concrete generic instantiations at compile time
- No need for runtime `MakeGenericMethod()` (which breaks AOT)
- For truly open generics: use `[JsonDerivedType]` registration

---

## 10. DEPENDENCY MANAGEMENT

### 10.1 Dependencies to Remove/Replace

| Current Dependency | Problem | Replacement |
|---|---|---|
| `ImpromptuInterface` | AOT-incompatible, dynamic dispatch | Source generator |
| `Cocoar.Reflectensions` | Reflection-heavy, used for type conversion & invocation | Source-generated code + `System.Text.Json` |
| `Cocoar.Reflectensions.Invoke` | Runtime method invocation | Source-generated dispatch |
| `Microsoft.CodeAnalysis.CSharp` (in ProxyGen) | Runtime code analysis | Move to source generator (compile-time) |
| `System.Interactive.Async` | Utility for async enumerable | Built-in .NET 10 equivalents |
| `Microsoft.Bcl.AsyncInterfaces` | Polyfill for netstandard2.0 | Drop netstandard2.0 target |
| `System.Net.Http` (explicit in Client) | Already part of framework | Remove explicit reference |

### 10.2 Dependencies to Keep

| Dependency | Reason |
|---|---|
| `System.Reactive.Linq` | `IObservable<T>` support (no replacement) |
| `System.Threading.Channels` | `ChannelReader<T>` support (core .NET) |
| `Microsoft.AspNetCore.SignalR.Client` | Client transport (core) |

### 10.3 New Dependencies

| Dependency | Purpose |
|---|---|
| `Microsoft.CodeAnalysis.CSharp` (analyzer/generator) | Source generator (compile-time only, not runtime) |

---

## 11. TARGET FRAMEWORK & COMPATIBILITY

### 11.1 Current Targets
- Server: `net8.0`
- Client/Common/ProxyGen: `net8.0` + `netstandard2.0`

### 11.2 New Targets
- **Server:** `net10.0` only (LTS, requires modern ASP.NET Core)
- **Client:** `net10.0` (drop `netstandard2.0`)
- **Common:** `net10.0`
- **ProxyGenerator:** Roslyn analyzer/generator targeting `netstandard2.0` (required by Roslyn tooling)
- **Optional:** `net8.0` support via multi-targeting if backward compat needed

### 11.3 AOT Compatibility
- Add `<IsAotCompatible>true</IsAotCompatible>` to all projects
- Use `[FeatureSwitchDefinition]` for dual-mode (dynamic/generated) support
- Document AOT limitations (value-type streaming, strongly-typed hubs)

---

## 12. NEW FEATURES (FUTURE)

### 12.1 OpenAPI / Contract Metadata Endpoint
Auto-generate endpoint that describes all registered RPC methods, their signatures, and types.
- Use `JsonSchemaExporter` (.NET 9) for type schemas
- Expose at `{hub}/_meta` or similar
- Feed into TypeScript client generation

### 12.2 Distributed Tracing / OpenTelemetry
Full `ActivitySource` integration for:
- Client proxy method calls (start/end span)
- Server method dispatch (start/end span)
- Auth challenge flows
- Streaming item counts
- Propagate trace context through `ClientRequestMessage`/`ServerRequestMessage`

### 12.3 Health Checks
- `IHealthCheck` implementation for SignalARRR
- Reports: connected clients count, hub status, method registry health

### 12.4 Metrics
- Connected clients gauge
- RPC calls counter (by method, hub, success/failure)
- Auth challenges counter
- Streaming items counter
- Latency histograms

### 12.5 Rate Limiting
Per-client or per-method rate limiting for RPC calls.
- Integrate with ASP.NET Core rate limiting middleware
- Configurable per-method via attributes: `[RateLimit(permits: 10, window: "1m")]`

### 12.6 Method Overloading Support
Currently only one method per name. Add parameter-count or parameter-type based dispatch.

### 12.7 Client Groups with Metadata
Track which groups each client belongs to in `ClientContext`. Enable group-based typed invocations.

### 12.8 Connection Resilience
- Auto-reconnect with backoff strategy
- Pending call queue during disconnection
- Replay failed calls on reconnect

### 12.9 Interceptors / Middleware Pipeline for RPC
Per-method middleware pipeline (like ASP.NET Core middleware but for RPC calls):
```csharp
services.AddSignalARRR(options => {
    options.UseInterceptor<LoggingInterceptor>();
    options.UseInterceptor<ValidationInterceptor>();
    options.UseInterceptor<CachingInterceptor>();
});
```

---

## 13. PRIORITY MATRIX

### Phase 1: Foundation (Must-Have for v4.0)
| # | Feature | Type | Effort |
|---|---|---|---|
| 1 | Source generator for proxy generation | NEW/REPLACE | High |
| 2 | Drop netstandard2.0, target net10.0 | MODERNIZE | Low |
| 3 | Remove ImpromptuInterface + Reflectensions | MODERNIZE | Medium |
| 4 | System.Text.Json source-generated serialization | MODERNIZE | Medium |
| 5 | Native AOT compatibility | MODERNIZE | Medium |
| 6 | CancellationToken server->client propagation | RESTORE | Medium |
| 7 | ServerProxyCreatorHelper.StreamAsync | RESTORE | Medium |

### Phase 2: Power Features (v4.1)
| # | Feature | Type | Effort |
|---|---|---|---|
| 8 | HTTP Proxy pass-through system | RESTORE | High |
| 9 | Distributed tracing / OpenTelemetry | NEW | Medium |
| 10 | Metrics + Health checks | NEW | Medium |
| 11 | RPC interceptors/middleware pipeline | NEW | Medium |
| 12 | Method overloading support | NEW | Medium |
| 13 | Configurable auth cache duration | IMPROVE | Low |

### Phase 3: Ecosystem (v4.2+)
| # | Feature | Type | Effort |
|---|---|---|---|
| 14 | TypeScript client (auto-generated) | RESTORE/NEW | High |
| 15 | OpenAPI contract metadata endpoint | NEW | Medium |
| 16 | Rate limiting | NEW | Medium |
| 17 | Connection resilience (auto-reconnect queue) | NEW | Medium |
| 18 | Distributed client manager (Redis) | NEW | High |
| 19 | Client groups with metadata | NEW | Low |

---

## 14. BREAKING CHANGES (v3 -> v4)

- Drop `netstandard2.0` / .NET Framework support
- Drop `Newtonsoft.Json` support (System.Text.Json only)
- Remove `ImpromptuInterface` dependency (source generator replaces it)
- Remove `Cocoar.Reflectensions` dependency
- `[MessageName]` renamed to `[SignalARRRMethod]`
- Proxy creation changes from runtime to compile-time (new API surface)
- `HARRRConnectionOptions` simplified
- Auth cache duration now configurable (default may change)
