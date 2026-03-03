# Authorization

SignalARRR integrates with ASP.NET Core's authorization system. Use standard
`[Authorize]` and `[AllowAnonymous]` attributes on hub methods and classes.

---

## Setup

### Server configuration

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options => {
        options.Authority = "https://auth.example.com";
        options.Audience = "my-api";
    });

builder.Services.AddAuthorization(options => {
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("admin"));
    options.AddPolicy("Premium", policy =>
        policy.RequireClaim("subscription", "premium"));
});

builder.Services.AddSignalR();
builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly));

var app = builder.Build();

app.UseRouting();
app.UseAuthentication();   // Must come before UseAuthorization
app.UseAuthorization();
app.MapHARRRController<ChatHub>("/chathub");
```

### Client token configuration

```csharp
var connection = HARRRConnection.Create(builder => {
    builder.WithUrl("https://localhost:5001/chathub", options => {
        options.AccessTokenProvider = () => Task.FromResult(GetJwtToken());
    });
});
```

---

## Method-Level Authorization

Apply `[Authorize]` to individual methods in ServerMethods classes:

```csharp
public class AdminMethods : ServerMethods<ChatHub> {

    [Authorize("AdminOnly")]
    public Task DeleteUser(int userId) {
        // Only accessible to users with "admin" role
        return _userService.Delete(userId);
    }

    [Authorize("Premium")]
    public Task<List<string>> GetPremiumContent() {
        return _contentService.GetPremium();
    }

    [AllowAnonymous]
    public Task<string> GetPublicInfo() {
        // Accessible to everyone, even without authentication
        return Task.FromResult("Public info");
    }
}
```

---

## Class-Level Authorization

Apply `[Authorize]` to the ServerMethods class — all methods inherit it:

```csharp
[Authorize]
public class SecureMethods : ServerMethods<ChatHub> {

    // All methods require authentication
    public Task<string> GetSecret() => Task.FromResult("secret");

    [AllowAnonymous]
    public Task<string> GetPublic() => Task.FromResult("public");
}
```

---

## Hub-Level Authorization Inheritance

If the Hub class has `[Authorize]`, all ServerMethods classes for that hub
inherit the authorization requirement automatically:

```csharp
[Authorize]  // All methods on all ServerMethods<SecureHub> require auth
public class SecureHub : HARRR {
    public SecureHub(IServiceProvider sp) : base(sp) { }
}

// This class inherits [Authorize] from SecureHub
public class SecureHubMethods : ServerMethods<SecureHub> {

    // Requires authentication (inherited from hub)
    public Task<string> GetData() => Task.FromResult("data");

    // Override: allow anonymous access despite hub-level auth
    [AllowAnonymous]
    public Task<string> GetPublicData() => Task.FromResult("public");
}
```

### Inheritance priority

Authorization attributes are resolved in this order:

1. **Method-level** — `[Authorize]` or `[AllowAnonymous]` on the method itself
2. **Class-level** — `[Authorize]` on the `ServerMethods<T>` class
3. **Hub-level** — `[Authorize]` on the `HARRR` hub class (via `ServerMethods<T>` generic argument)

If no authorization attributes are found at any level, the method is accessible
anonymously.

---

## Authorization flow

When a client calls an authorized method:

1. SignalARRR checks for `[AllowAnonymous]` — if present, allows access
2. Checks for `[Authorize]` on method → class → hub
3. If authorization data exists:
   a. Validates the token (from `AccessTokenProvider`) against configured schemes
   b. Caches the authentication result until `ClientContext.UserValidUntil`
   c. Evaluates the authorization policy
4. If authentication has expired, sends a challenge to the client
5. Client responds with a fresh token via the challenge protocol
6. Server re-evaluates with the new token

### Token caching

SignalARRR caches successful authentication results. It only re-authenticates
when `ClientContext.UserValidUntil` expires. This avoids re-validating the token
on every RPC call.

---

## Accessing the authenticated user

```csharp
public class UserMethods : ServerMethods<ChatHub> {
    public Task<string> WhoAmI() {
        // From ClientContext (enhanced)
        var user = ClientContext.User;
        var name = user.Identity?.Name ?? "Anonymous";

        // Or from standard SignalR context
        var signalRUser = Context.User;

        return Task.FromResult(name);
    }
}
```

---

## SignalR negotiate-level auth

Note that `[Authorize]` on the Hub class also blocks the SignalR negotiate
endpoint (HTTP level). Unauthenticated clients will get a 401 response when
trying to connect — they won't even establish a SignalR connection.

This is different from method-level auth, where the connection succeeds but
individual method calls are rejected.
