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

There are two credentials, and they are configured separately:

```csharp
var connection = HARRRConnection.Create(
    builder =>
    {
        builder.WithUrl("https://localhost:5001/apphub", options =>
        {
            // SignalR's — authenticates the connection: negotiate and transport.
            options.AccessTokenProvider = () => Task.FromResult(GetCurrentToken());
        });
    },
    options =>
    {
        // SignalARRR's — authenticates each message, answers a challenge, and carries the
        // file transfers.
        options.WithAuthorization(() => Task.FromResult(GetCurrentToken()));
    });
```

| | Configured with | Checked by |
|---|---|---|
| **Connection** | SignalR's `AccessTokenProvider` | `[Authorize]` on the hub class, `.RequireAuthorization()` on the mapping |
| **Message** | SignalARRR's `WithAuthorization` | `[Authorize]` on a method or a `ServerMethods` class |

Usually it is one credential, so you pass the same factory to both — as above. They are separate because they answer different questions, and because they are not always the same thing: a single-use connection ticket belongs on the connection and has no business being resent with every message.

The message credential is what keeps a long-lived connection current. It travels with every call, and it is what answers a challenge while a stream is running — so the server can re-check the credential rather than trusting the one it saw at negotiate.

A connection without it is not cut off: once the server's auth cache lapses it falls back to the principal established at negotiate, the way plain SignalR would, and the expiry stated on that principal is still enforced. What you lose is the refresh — the server can no longer catch a revoked credential, and cannot ask you for a new one.

::: warning Changed in 5.0.0
SignalARRR used to take the message credential from SignalR's `AccessTokenProvider` automatically, by reflecting into two levels of its private fields. It no longer does. If your tokens are short-lived and refreshed — the usual reason for having them — add `WithAuthorization`, or the connection will run on the identity it started with until that identity's stated expiry.
:::

`WithAuthorization` also accepts a `Func<string>` or a plain `string` for a credential that does not change.

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
