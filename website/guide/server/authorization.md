# Authorization

SignalARRR integrates with ASP.NET Core's authorization system. Apply `[Authorize]` at the method, class, or hub level.

SignalARRR supports two authentication modes:

- **Message-Level** (Bearer, Basic, API Key) — token sent per message, automatic challenge/refresh on expiry
- **Transport-Level** (client certificates, cookies, Windows/Negotiate) — authenticated at connection time, server-side re-validation on cache expiry

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

## Message-Level Authentication (Tokens)

### Authentication setup

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

### Client-side token provider

#### .NET Client

Provide the credential twice: SignalR's `AccessTokenProvider` authenticates the connection, SignalARRR's `WithAuthorization` authenticates each message and answers a challenge. Usually it is the same credential.

```csharp
var connection = HARRRConnection.Create(
    builder =>
    {
        builder.WithUrl("https://localhost:5001/apphub", options =>
        {
            options.AccessTokenProvider = () => Task.FromResult(GetCurrentToken());
        });
    },
    options => options.WithAuthorization(() => Task.FromResult(GetCurrentToken())));
```

#### TypeScript Client

```ts
const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://localhost:5001/apphub', {
        accessTokenFactory: async () => await getAuthToken(),
    });
});
```

### Automatic token challenge

When a client's token expires during an active connection, SignalARRR doesn't disconnect it. Instead, the next authorized method call triggers a **challenge flow**:

1. Server detects the cached authentication has expired
2. Server sends `ChallengeAuthentication` to the client (via SignalR's native client results)
3. Client's message credential (`WithAuthorization`, `authorization`, `accessTokenFactory` — see the client guides) is called to get a fresh token
4. Client returns the new token directly from the handler
5. Server validates the new token against the configured authentication scheme and continues the request

This happens transparently — no client-side code needed beyond providing a token factory.

When `[Authorize]` is used without specifying a scheme (the common case), SignalARRR automatically uses the default authentication scheme configured via `AddAuthentication()`.

## Transport-Level Authentication (Certificates, Negotiate, Cookies)

For scenarios where credentials exist at the transport layer (TLS client certificates, Windows/Negotiate, HTTP cookies), SignalARRR supports **transport-level authentication**. The client authenticates once at connection time, and the server re-validates credentials server-side when the auth cache expires — no challenge round-trip needed.

### How it works

1. Client connects with transport credentials (e.g., client certificate via mTLS)
2. ASP.NET Core authenticates during SignalR negotiate — `ClientContext.User` is set
3. SignalARRR **auto-detects** transport-level auth (client cert present, or Negotiate/NTLM/Kerberos/Windows auth type)
4. On cache expiry: server re-validates the stored credentials server-side (no challenge to client)
5. If re-validation fails (cert expired or revoked, ticket past its stated expiry) → request is rejected

### Cookies and other schemes are opt-in

Auto-detection covers only credentials that are unmistakably bound to the connection. A cookie identity looks exactly like a bearer identity once it has become a `ClaimsPrincipal`, and treating a bearer one as connection-bound would let a token outlive its own expiry — so SignalARRR does not guess. Declare the scheme instead:

```csharp
builder.Services.AddSignalARRR(options =>
{
    options.ConnectionBoundSchemes.Add(CookieAuthenticationDefaults.AuthenticationScheme);
});
```

Adding a scheme is a statement that the credential lasts as long as the connection. Without it, a cookie-authenticated client is treated as message-level: it is challenged for a token once `AuthCacheDuration` has passed, has none to give, and every `[Authorize]` call is rejected from that point on.

Re-validation of a declared scheme checks the expiry stated on the principal's ticket. To check more than that — a session store lookup, for instance — register an `ITransportAuthRevalidationService`.

### Client certificate authentication

#### Server setup

Configure Kestrel for client certificates and add an authentication handler:

```csharp
builder.WebHost.UseKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.Any, 5001, listenOptions =>
    {
        listenOptions.UseHttps(https =>
        {
            https.ServerCertificate = serverCert;
            https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
        });
    });
});

builder.Services.AddAuthentication("Certificate")
    .AddCertificate(options =>
    {
        options.AllowedCertificateTypes = CertificateTypes.All;
        options.RevocationMode = X509RevocationMode.Online;
    });
```

#### .NET Client

Configure the client certificate on the connection — no `AccessTokenProvider` needed:

```csharp
var cert = new X509Certificate2("client.pfx", password);

var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://server:5001/apphub", options =>
    {
        options.ClientCertificates = new X509CertificateCollection { cert };
        options.HttpMessageHandlerFactory = handler =>
        {
            if (handler is SocketsHttpHandler socketsHandler)
            {
                socketsHandler.SslOptions.ClientCertificates =
                    new X509CertificateCollection { cert };
            }
            return handler;
        };
    });
});
```

::: tip No token needed
With transport-level auth, neither credential is required: SignalARRR detects that the client is authenticated by the connection and re-validates server-side instead of asking it for a token. `WithAuthorization` is what a token-authenticated client needs; a certificate-authenticated one does not.
:::

### Certificate re-validation

When the auth cache expires, the server re-validates the stored client certificate:

- **Expiry check**: `NotBefore` / `NotAfter` dates
- **Revocation check**: CRL/OCSP (configurable)
- **Custom validation**: Pluggable callback for custom logic

Configure via `SignalARRRServerOptions`:

```csharp
builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly)
    .WithCertificateRevocationCheck(true)                     // default: true
    .WithCertificateRevocationMode(X509RevocationMode.Online) // default: Online
    .WithCustomCertificateValidator(cert =>                   // optional
    {
        // Custom logic, e.g., check against an internal revocation list
        return !IsRevoked(cert.Thumbprint);
    }));
```

### Custom re-validation service

For full control over transport-auth re-validation, implement `ITransportAuthRevalidationService`:

```csharp
public class MyRevalidationService : ITransportAuthRevalidationService
{
    public async Task<bool> RevalidateAsync(
        ClientContext clientContext,
        CancellationToken cancellationToken = default)
    {
        if (clientContext.ClientCertificate != null)
        {
            // Check your internal revocation database, OCSP responder, etc.
            return await CheckCertificateStatus(clientContext.ClientCertificate);
        }

        // Non-cert transport auth (cookies, Negotiate)
        return clientContext.User.Identity?.IsAuthenticated == true;
    }
}

// Register before AddSignalARRR (TryAddSingleton won't override your registration)
builder.Services.AddSingleton<ITransportAuthRevalidationService, MyRevalidationService>();
```

### Certificate rotation

To rotate certificates without restarting the application:

1. Update the certificate file on disk
2. Reconnect the SignalR connection — the new TLS handshake uses the new certificate
3. Server validates the new certificate on connect

```csharp
// Client-side cert rotation
await connection.StopAsync();
// Certificate file has been updated on disk — reload it
cert = new X509Certificate2("client.pfx", password);
await connection.StartAsync(); // new TLS handshake with new cert
```

### Mixed mode

A single hub can serve both token-based and certificate-based clients simultaneously. SignalARRR detects the authentication mode per client:

```csharp
[Authorize]
public class AppHub : HARRR
{
    public AppHub(IServiceProvider sp) : base(sp) { }
}
```

- Client A connects with a token and `WithAuthorization` → message-level auth, credential resent per call, challenge on expiry
- Client B connects with a client certificate → transport-level auth, server-side re-validation, no credential in the message

## Auth cache

Authentication results are cached per client (default: 3 minutes). When a client connects to a hub with `[Authorize]`, the cache is initialized from the SignalR negotiate authentication, so the first method call uses the cached principal without triggering a challenge or re-validation.

The cache duration is configurable:

```csharp
builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly)
    .WithAuthCacheDuration(TimeSpan.FromMinutes(5)));
```

The cache duration controls how often credentials are re-checked — for both token-based and transport-level auth.

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
