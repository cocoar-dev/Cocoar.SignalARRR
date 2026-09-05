---
description: "[Authorize] at method, class and hub level; message-level token authentication with ChallengeAuthentication and the auth cache; transport-level authentication with client certificates, Negotiate and cookies; re-validation and mixed mode"
---

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

### When the auth cache expires

When a client's token expires during an active connection, SignalARRR doesn't disconnect it. What happens next depends on whether there is a message to carry a fresh credential.

**On an ordinary call**, there is. Every client-to-server message already carries the credential in its `Authorization` field, so the server simply validates that one against the configured scheme and continues — no round trip, nothing for the client to do beyond having configured a credential.

That validation runs against a context built for the purpose, since a message over an open socket is not an HTTP request. It carries the credential **and the connection's own request facts** — host, scheme, path, and the `HttpContext.Items` your middleware stamped before authentication ran — captured when the connection was established. So a handler that resolves anything from where the request arrived, a per-tenant issuer set being the common case, works per message exactly as it did at negotiate.

**On a running stream**, there is not. Authorization is re-checked for every streamed element, and those elements are not messages the client sends — so the server has to ask:

1. Server detects the cached authentication has expired while the stream is running
2. Server sends `ChallengeAuthentication` to the client (via SignalR's native client results)
3. The client's message credential (`WithAuthorization`, `authorization` — see the client guides) is called
4. Client returns the credential directly from the handler
5. Server validates it, extends the cache, and the stream continues

This is the only path that challenges. It happens transparently — no client-side code beyond configuring the credential.

### If the client has no credential to give

A client can legitimately authenticate its connection and nothing else — a certificate, a cookie, a bearer token passed only to SignalR's own `AccessTokenProvider`. When the cache expires there is nothing to validate and nothing to ask for, so SignalARRR falls back to the principal the connection was established with, exactly as SignalR would.

Such a client is challenged once, answers with nothing, and is not asked again — otherwise a stream would cost a round trip per element for an answer that is never going to change. It is asked again as soon as a message does arrive carrying a credential, so a user signing in mid-connection is picked up.

With one addition: **the expiry that principal states is honoured.** If it carries an `exp` claim in the past, the call is rejected. Plain SignalR never looks at `exp` again once negotiate is done, so a token that expired hours ago keeps working there until the socket drops; here it does not.

That gives three levels, and none of them is weaker than SignalR:

| What the client configures | On cache expiry | Catches |
|---|---|---|
| A message credential | validated fresh against the scheme | expiry **and** revocation |
| A transport-level scheme (see below) | `ITransportAuthRevalidationService` — certificate chain, revocation, your own checks | whatever you check |
| Neither | cached principal, `exp` enforced | expiry |

Revocation cannot be caught in the third row: noticing it requires a credential to re-check, and there is none. If that matters, configure one of the first two.

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

Adding a scheme is a statement that the credential lasts as long as the connection. What it buys you is **active re-validation**: once the cache lapses the server runs `ITransportAuthRevalidationService` for that connection rather than falling back to the principal it negotiated with.

```csharp
public class SessionRevalidation : ITransportAuthRevalidationService
{
    public async Task<RevalidationResult> RevalidateAsync(
        ClientContext client, CancellationToken cancellationToken = default)
    {
        var session = await _sessions.LookupAsync(client.UserIdentifier, cancellationToken);

        if (session is null || session.Revoked)
            return RevalidationResult.Abort();          // refuse, and drop the connection

        if (session.ExpiresAt < DateTimeOffset.UtcNow)
            return RevalidationResult.Deny();           // refuse this call, leave the socket up

        return RevalidationResult.ValidForDuration(TimeSpan.FromSeconds(30));
    }
}
```

Three things it can say:

| | Meaning |
|---|---|
| `Valid()` / `ValidForDuration(...)` | the credential holds; the duration overrides `AuthCacheDuration` for this connection, which is how one hub can re-check a reference token every few seconds and a browser cookie every few minutes |
| `Deny()` | refuse this call, leave the connection open |
| `Abort()` | refuse it **and drop the connection** — for a push connection, a client that believes it is connected while the server serves it nothing is worse than a client that knows it is gone |

Returning a `bool` still works and means `Valid()` or `Deny()`.

`ClientContext.Abort()` is public, so an application that learns a credential died outside this pipeline — a background watchdog, a revocation webhook — can drop the connection itself. It is safe to call more than once and after the connection has already gone.

Without it a cookie client is not broken — it takes the fallback described above and keeps working on its negotiated principal, with that principal's stated expiry enforced. Declaring the scheme is what lets you check more than an `exp` claim: a session store lookup, a revocation list, whatever your `ITransportAuthRevalidationService` implements. The built-in one checks the ticket's expiry, and the certificate chain when there is a certificate.

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
    public async Task<RevalidationResult> RevalidateAsync(
        ClientContext clientContext,
        CancellationToken cancellationToken = default)
    {
        if (clientContext.ClientCertificate != null)
        {
            // Check your internal revocation database, OCSP responder, etc.
            return await CheckCertificateStatus(clientContext.ClientCertificate);  // bool converts
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

- Client A connects with a token and `WithAuthorization` → message-level auth: the credential travels with every call and is re-validated once the cache lapses, and a running stream is challenged for a fresh one
- Client B connects with a client certificate → transport-level auth: re-validated server-side, no credential in the message, never challenged
- Client C connects with a token but configures no `WithAuthorization` → runs on the principal it negotiated with, with that principal's stated expiry enforced

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
