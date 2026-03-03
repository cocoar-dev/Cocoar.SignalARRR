# Migration from v2.x to v4.0

## Breaking changes

### 1. Target framework: netstandard2.0 → net10.0

All packages now target `net10.0`. Remove polyfill packages:

```diff
- <PackageReference Include="Microsoft.Bcl.AsyncInterfaces" />
- <PackageReference Include="System.Threading.Channels" />
- <PackageReference Include="System.Net.Http" />
- <PackageReference Include="System.Interactive.Async" />
```

### 2. Proxy generation: ImpromptuInterface → Source Generator

**Before (v2.x):** Proxies were created at runtime using `ImpromptuInterface`.

**After (v4.0):** Proxies are generated at compile time from `[SignalARRRContract]`
interfaces.

**Migration steps:**

1. Add `Cocoar.SignalARRR.Contracts` to your shared interface project
2. Annotate interfaces with `[SignalARRRContract]`:
   ```csharp
   [SignalARRRContract]
   public interface IChatHub {
       Task SendMessage(string user, string message);
   }
   ```
3. Build — proxies are generated automatically
4. If you need runtime proxy creation (plugin scenarios), add
   `Cocoar.SignalARRR.DynamicProxy`

### 3. Authentication: IAuthenticator → ASP.NET Core auth

**Before (v2.x):** Custom `IAuthenticator` interface with `TryAuthenticate()`
and `SetAuthData()` on `ClientContext`.

**After (v4.0):** Standard ASP.NET Core authentication and authorization.

**Migration steps:**

1. Replace your `IAuthenticator` implementation with an ASP.NET Core
   authentication handler (e.g., `AddJwtBearer()`)
2. Use `[Authorize]` attributes instead of custom auth checks
3. Add standard middleware:
   ```csharp
   app.UseAuthentication();
   app.UseAuthorization();
   ```
4. Access the authenticated user via `ClientContext.User` (returns
   `ClaimsPrincipal`)

### 4. Client method registration: RegisterMethods → RegisterInterface

**Before (v2.x):**
```csharp
connection.RegisterClientMethods<ChatClientImpl>();
connection.RegisterClientMethods<ChatClientImpl>(instance);
```

**After (v4.0):**
```csharp
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>();
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>(instance);
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>(
    sp => new ChatClientImpl(sp.GetRequiredService<ILogger>()));
```

The new API is interface-based: you register `TInterface` + `TClass` pairs.
This enables the server to call client methods through the typed proxy system.

### 5. Hub-level authorization inheritance (behavior change)

In v2.x, hub-level `[Authorize]` inheritance was disabled.

In v4.0, if the Hub class has `[Authorize]`, all `ServerMethods<T>` classes
for that hub inherit it automatically. Use `[AllowAnonymous]` on individual
methods to opt out.

```csharp
[Authorize]  // All ServerMethods<SecureHub> inherit this
public class SecureHub : HARRR {
    public SecureHub(IServiceProvider sp) : base(sp) { }
}

public class MyMethods : ServerMethods<SecureHub> {
    // Requires auth (inherited from hub)
    public Task<string> GetSecret() => Task.FromResult("secret");

    // Override: no auth needed
    [AllowAnonymous]
    public Task<string> GetPublic() => Task.FromResult("public");
}
```

### 6. HTTP Proxy feature removed

The HTTP Proxy pass-through feature (forwarding HTTP requests through SignalR
clients) has been removed in v4.0. It will be redesigned and re-introduced in
a future version (v4.1).

If you depend on this feature, stay on v2.x until v4.1 is released.

---

## New features in v4.0

### Source-generated proxies

Zero-reflection proxy generation at compile time. Just annotate interfaces:

```csharp
[SignalARRRContract]
public interface IChatHub { ... }
```

See [Proxy Generation](proxy-generation.md) for details.

### CancellationToken server-to-client propagation

The server can pass a `CancellationToken` to client methods and cancel them
remotely:

```csharp
var cts = new CancellationTokenSource();
var client = ClientContext.GetTypedMethods<IWorkerClient>();
_ = client.DoWork(cts.Token);
// Later: cts.Cancel() — cancels on the client
```

### Server-initiated client streaming

The server can request `IAsyncEnumerable<T>` streams from clients:

```csharp
var client = ClientContext.GetTypedMethods<IDataClient>();
await foreach (var item in client.StreamData()) {
    Process(item);
}
```

See [Streaming](streaming.md) for details.

### DynamicProxy package

Opt-in `DispatchProxy`-based runtime fallback for plugin/dynamic scenarios:

```
dotnet add package Cocoar.SignalARRR.DynamicProxy
```

No code changes — it auto-registers as a fallback factory.

---

## Package changes

| v2.x | v4.0 | Notes |
|---|---|---|
| `Cocoar.SignalARRR.Server` | `Cocoar.SignalARRR.Server` | Same package, new target |
| `Cocoar.SignalARRR.Client` | `Cocoar.SignalARRR.Client` | Same package, new target |
| — | `Cocoar.SignalARRR.Contracts` | **New** — add to shared interface projects |
| — | `Cocoar.SignalARRR.DynamicProxy` | **New** — opt-in runtime proxy fallback |
| `ImpromptuInterface` (transitive) | — | **Removed** |
| `Microsoft.Bcl.AsyncInterfaces` (transitive) | — | **Removed** |
