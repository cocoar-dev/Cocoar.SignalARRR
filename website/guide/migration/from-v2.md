---
description: "Upgrade from 2.x to 4.x: target frameworks, source-generated proxies instead of ImpromptuInterface, the authentication model, hub-level authorization inheritance, removed APIs and new packages"
---

# Migration from v2.x

SignalARRR v4 is a major release with significant architectural changes. This guide covers all breaking changes and the steps to upgrade.

## Target framework

**v2.x:** `netstandard2.0`
**v4:** `net10.0` (except SourceGenerator which targets `netstandard2.0` per Roslyn requirements)

Update your project target frameworks:

```xml
<TargetFramework>net10.0</TargetFramework>
```

## Proxy generation

**v2.x:** `ImpromptuInterface` for runtime proxy creation
**v4:** Roslyn source generator + optional `DispatchProxy` fallback

### Steps

1. Remove `ImpromptuInterface` package references
2. Add `Cocoar.SignalARRR.Contracts` to shared interface projects
3. Mark interfaces with `[SignalARRRContract]`:

```csharp
// Before (v2.x) — no attribute needed
public interface IChatHub { ... }

// After (v4) — attribute required
[SignalARRRContract]
public interface IChatHub { ... }
```

4. If you need runtime proxy generation (plugin scenarios), add `Cocoar.SignalARRR.DynamicProxy`

## Authentication

**v2.x:** Custom `IAuthenticator` interface with `TryAuthenticate()` and `SetAuthData()`
**v4:** Standard ASP.NET Core `[Authorize]` attributes and authentication handlers

### Steps

1. Remove `IAuthenticator` implementations
2. Configure ASP.NET Core authentication:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { ... });
```

3. Use `[Authorize]` on methods and classes instead of custom auth logic

## Hub-level authorization inheritance

**v2.x:** Hub-level `[Authorize]` was disabled — ServerMethods didn't inherit it
**v4:** Hub-level `[Authorize]` is inherited by all ServerMethods classes

If you had a hub with `[Authorize]` but relied on individual ServerMethods classes being unauthenticated, add `[AllowAnonymous]` to those classes:

```csharp
[Authorize]
public class SecureHub : HARRR { ... }

[AllowAnonymous]  // opt out of hub-level auth
public class PublicMethods : ServerMethods<SecureHub> { ... }
```

## HTTP proxy pass-through

**v2.x:** Available for large response streaming
**v4:** Removed

The `UseHttpResponse()` option and all related code has been removed. For large data transfer, use [HTTP Stream References](/guide/advanced/http-streams) instead — `Stream` parameters are automatically routed through HTTP.

## Removed APIs

| Removed | Replacement |
|---------|-------------|
| `ImpromptuInterface` | `[SignalARRRContract]` + source generator |
| `IAuthenticator` | ASP.NET Core `[Authorize]` |
| `RegisterMethods()` | `RegisterInterface()` or typed proxies |
| `TryAuthenticate()` / `SetAuthData()` | `ClientContext.User` / `[Authorize]` |
| `netstandard2.0` polyfill packages | Native `net10.0` APIs |
| Non-generic `Invoke(Type, ...)` overloads | Generic `Invoke<T>(...)` |

## New packages

| Package | Purpose |
|---------|---------|
| `Cocoar.SignalARRR.Contracts` | `[SignalARRRContract]` + source generator — add to shared projects |
| `Cocoar.SignalARRR.DynamicProxy` | Optional runtime proxy fallback |

## New features

- **Source generator** — compile-time proxies, AOT-friendly
- **CancellationToken propagation** — server can cancel client operations
- **Server stream requests** — server can request `IAsyncEnumerable<T>` from clients
- **`StreamItemToServer` / `StreamCompleteToServer`** — client-to-server streaming
- **`ClientManager` typed extensions** — `GetTypedMethods<T>(connectionId)` for server-to-client RPC outside hub context
- **Authorization tests** — comprehensive test coverage for auth scenarios

## Next steps

- [Getting Started](/guide/getting-started) — fresh start with v4
- [Proxy Generation](/guide/advanced/proxy-generation) — source generator details
- [Authorization](/guide/server/authorization) — new authorization system
