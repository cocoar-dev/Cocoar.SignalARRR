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

Pass a token factory through SignalR's `WithUrl` options:

```csharp
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://localhost:5001/apphub", options =>
    {
        options.AccessTokenProvider = () => Task.FromResult(GetCurrentToken());
    });
});
```

The token is sent with every SignalARRR request and automatically refreshed when the server challenges an expired token.

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

When a server method throws an exception, the client receives a structured error with the exception type and message:

```csharp
try {
    var result = await chat.GetHistory();
} catch (HubException ex) {
    var error = HARRRError.Parse(ex);
    Console.WriteLine($"{error.Type}: {error.Message}");
    // "System.ArgumentException: Invalid value provided"
}
```

`HARRRException` extends `HubException`, so error details always reach the client — no `EnableDetailedErrors` configuration needed.

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
