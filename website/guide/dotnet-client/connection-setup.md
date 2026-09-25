---
description: Create a HARRRConnection, authenticate with bearer tokens or client certificates, configure auto-reconnect, start and stop, handle errors and connection events, reach the underlying HubConnection
---

# Connection Setup

`HARRRConnection` wraps ASP.NET Core's `HubConnection` with typed RPC support. Create one using the static factory method.

## Create a connection

Use the builder pattern to configure the underlying SignalR connection:

```csharp
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://localhost:5001/apphub");
});
```

Or wrap an existing `HubConnection`:

```csharp
var hubConnection = new HubConnectionBuilder()
    .WithUrl("https://localhost:5001/apphub")
    .Build();

var connection = HARRRConnection.Create(hubConnection);
```

## Connection with authentication

### Token-based (Bearer, JWT)

A connection has two credentials. Usually they are the same token, so one option sets both:

```csharp
var connection = HARRRConnection.Create(
    builder => builder.WithUrl("https://localhost:5001/apphub"),
    options => options.WithCredential(async () => await tokenStore.GetTokenAsync()));
```

To give them different credentials — a single-use connection ticket, say, which has no business being resent with every message — set them separately:

```csharp
options => options
    .WithConnectionCredential(async () => await GetConnectionTicketAsync())
    .WithMessageCredential(async () => await tokenStore.GetTokenAsync())
```

| Option | Authenticates | Travels as | Checked by | When it expires |
|---|---|---|---|---|
| `WithConnectionCredential` | negotiate and the transport | `Authorization` header | `[Authorize]` on the hub class, `.RequireAuthorization()` on the mapping | fetched again on every connect and reconnect |
| `WithMessageCredential` | every message, the answer to a challenge, file transfers | the `Authorization` field of the message; a header on file transfers | `[Authorize]` on a method or a `ServerMethods` class | fetched on every use, so a refreshed token is sent from the next call on |
| `WithCredential` | both of the above | | | |

The options are named the same in every SignalARRR client, and nothing is coupled implicitly: `WithMessageCredential` alone leaves the connection anonymous, `WithConnectionCredential` alone sends no message credential. Each accepts an async factory, a `Func<string>`, or a plain `string` for a credential that does not change. See [Authorization](/guide/server/authorization#the-two-credentials) for the same table across all clients.

The message credential is what keeps a long-lived connection current. It travels with every call, and it is what answers a challenge while a stream is running — so the server can re-check the credential rather than trusting the one it saw at negotiate. A connection without it is not cut off: once the server's auth cache lapses it falls back to the principal established at negotiate, the way plain SignalR would, and the expiry stated on that principal is still enforced. What you lose is the refresh — the server can no longer catch a revoked credential, and cannot ask you for a new one.

`WithConnectionCredential` becomes SignalR's `AccessTokenProvider`, so it only works with `Create(builder => ...)`, where SignalARRR builds the `HubConnection`. Setting `AccessTokenProvider` in `WithUrl` as well, or passing a connection credential to `Create(hubConnection)`, fails at `Create` rather than silently picking one.

::: tip Former names
`WithAuthorization` is the message credential under its former name. It still works and is marked obsolete; replace it with `WithMessageCredential`, or with `WithCredential` if you also set SignalR's `AccessTokenProvider` to the same token. Using it together with the new options fails at `Create`.
:::

::: warning Changed in 5.0.0
SignalARRR used to take the message credential from SignalR's `AccessTokenProvider` automatically, by reflecting into two levels of its private fields. It no longer does. If your tokens are short-lived and refreshed — the usual reason for having them — set a message credential, or the connection will run on the identity it started with until that identity's stated expiry.
:::

### Certificate-based (mTLS)

For client certificate authentication, configure the certificate on the connection. No `AccessTokenProvider` is needed — SignalARRR auto-detects transport-level auth:

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

When the auth cache expires, the server re-validates the certificate server-side (checking expiry and optionally CRL/OCSP) without sending a challenge to the client. See [Authorization](/guide/server/authorization) for server-side configuration.

## Auto-reconnect

```csharp
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://localhost:5001/apphub");
    builder.WithAutomaticReconnect();
});
```

## Start and stop

```csharp
await connection.StartAsync();

// ... use the connection ...

await connection.StopAsync();
await connection.DisposeAsync();
```

## Error handling

When a server call fails, the client receives a structured error carrying a machine-readable code:

```csharp
try {
    var result = await chat.GetHistory();
} catch (HubException ex) {
    var error = HARRRError.Parse(ex);
    Console.WriteLine($"{error.Code}: {error.Message}");
    // "argument_binding_failed: Invalid value provided"
}
```

A connection the server rejects at negotiate — 401 or 403 for a missing or invalid connection credential — fails `StartAsync` with SignalR's `HttpRequestException`, whose `StatusCode` carries the HTTP status. The other clients expose the same as `statusCode`.

`HARRRException` extends `HubException`, so the structured error always reaches the client — no `EnableDetailedErrors` configuration needed. **How much detail it carries depends on the code**, and the split is deliberate:

| Code | What the client sees |
|------|----------------------|
| A code you threw yourself — `new HARRRException("room_full", "The room is full")` | Your code and your message, verbatim |
| A framework code — `unauthorized`, `timeout`, `cancelled`, `argument_binding_failed`, `method_not_found`, `no_client_responded`, `upload_slot_limit_reached` | The message the pipeline produced, plus the nested cause chain |
| `internal` — the invoked method threw something the pipeline does not recognize | A fixed sentence and a correlation id. Nothing else. |

::: warning Changed in 5.0
`internal` used to carry the exception's own type and message. That routinely put a `SqlException` naming the database server, or a `FileNotFoundException` naming an absolute path, in front of any caller authorized to make the call — which is what SignalR's `EnableDetailedErrors=false` default exists to prevent.

The exception is now logged on the server under the same correlation id the client is shown, so nothing is lost — it moves from somewhere the caller can read to somewhere the operator can. If you want a specific failure to reach the client, say so explicitly by throwing `HARRRException(code, message)`.
:::

## Connection events

```csharp
connection.Closed += error =>
{
    Console.WriteLine($"Connection closed: {error?.Message}");
    return Task.CompletedTask;
};

connection.Reconnecting += error =>
{
    Console.WriteLine($"Reconnecting: {error?.Message}");
    return Task.CompletedTask;
};

connection.Reconnected += connectionId =>
{
    Console.WriteLine($"Reconnected as {connectionId}");
    return Task.CompletedTask;
};
```

## Connection properties

| Property | Type | Description |
|----------|------|-------------|
| `ConnectionId` | `string?` | Current connection ID (null when disconnected) |
| `State` | `HubConnectionState` | `Disconnected`, `Connecting`, `Connected`, `Reconnecting` |
| `ServerTimeout` | `TimeSpan` | Server keepalive timeout |
| `KeepAliveInterval` | `TimeSpan` | Client keepalive ping interval |
| `HandshakeTimeout` | `TimeSpan` | Handshake timeout |

## Access the raw HubConnection

If you need SignalR features not exposed by `HARRRConnection`:

```csharp
var hubConnection = connection.AsSignalRHubConnection();
```

## Next steps

- [Typed Methods](/guide/dotnet-client/typed-methods) — call server methods through interfaces
- [Server-to-Client Handlers](/guide/dotnet-client/server-to-client) — handle server calls
- [Streaming](/guide/streaming/server-to-client) — stream data from the server
