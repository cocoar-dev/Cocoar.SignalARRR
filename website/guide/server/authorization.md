# Authorization

SignalARRR integrates with ASP.NET Core's authorization system. Apply `[Authorize]` at the method, class, or hub level. Tokens are validated continuously, and expired tokens trigger an automatic challenge/refresh flow.

## Method-level authorization

Apply `[Authorize]` to individual methods:

```csharp
public class AdminMethods : ServerMethods<AppHub>, IAdminHub
{
    [Authorize(Policy = "AdminOnly")]
    public Task DeleteUser(string userId) { ... }

    [Authorize(Roles = "Admin,Moderator")]
    public Task BanUser(string userId) { ... }

    [AllowAnonymous]
    public Task<string> GetServerInfo() { ... }
}
```

## Class-level authorization

Apply `[Authorize]` to the entire class — all methods require authentication:

```csharp
[Authorize]
public class SecureMethods : ServerMethods<AppHub>, ISecureHub
{
    public Task GetSecret() { ... }  // requires authentication

    [AllowAnonymous]
    public Task<string> GetPublicData() { ... }  // opt-out for this method
}
```

## Hub-level inheritance

If the hub class itself has `[Authorize]`, all `ServerMethods<T>` classes inherit it automatically:

```csharp
[Authorize]
public class SecureHub : HARRR
{
    public SecureHub(IServiceProvider sp) : base(sp) { }
}

// All methods in this class require authentication, inherited from the hub
public class SecureMethods : ServerMethods<SecureHub>, ISecureHub { ... }
```

## Authentication setup

Configure ASP.NET Core authentication as usual. SignalARRR reads the `Authorization` header from client requests:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidIssuer = "your-issuer",
            ValidAudience = "your-audience",
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});
```

## Client-side token provider

### .NET Client

Provide a token factory when creating the connection:

```csharp
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://localhost:5001/apphub", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult(GetCurrentToken());
    });
});
```

### TypeScript Client

```ts
const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://localhost:5001/apphub', {
        accessTokenFactory: () => getAuthToken(),
    });
});
```

## Automatic token challenge

When a client's token expires during an active connection, SignalARRR doesn't disconnect it. Instead, the next authorized method call triggers a **challenge flow**:

1. Server detects the cached authentication has expired
2. Server sends `ChallengeAuthentication` to the client (via SignalR's native client results)
3. Client's `AccessTokenProvider` is called to get a fresh token
4. Client returns the new token directly from the handler
5. Server validates the new token against the configured authentication scheme and continues the request

This happens transparently — no client-side code needed beyond providing a token factory.

::: tip
Authentication results are cached per client (default: 3 minutes). When a client connects to a hub with `[Authorize]`, the cache is initialized from the SignalR negotiate authentication, so the first method call uses the cached principal without triggering a challenge.
:::

The cache duration is configurable:

```csharp
builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly)
    .WithAuthCacheDuration(TimeSpan.FromMinutes(5)));
```

When `[Authorize]` is used without specifying a scheme (the common case), SignalARRR automatically uses the default authentication scheme configured via `AddAuthentication()`.

## ClientContext user data

Inside `ServerMethods`, access the authenticated user through `ClientContext`:

```csharp
public Task<string> GetUserInfo()
{
    var user = ClientContext.User;
    var name = user.FindFirst(ClaimTypes.Name)?.Value;
    var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value);

    return Task.FromResult($"{name} ({string.Join(", ", roles)})");
}
```

## Next steps

- [Client Manager](/guide/server/client-manager) — query authenticated clients
- [Connection Setup (.NET)](/guide/dotnet-client/connection-setup) — configure token providers
- [TypeScript Setup](/guide/typescript-client/setup) — authentication in the TypeScript client
