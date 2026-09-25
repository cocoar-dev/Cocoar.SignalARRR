<!-- Generated from website/guide/swift-client/setup.md by website/scripts/sync-skill.mjs. Do not edit; edit the docs page. -->

# Swift Client Setup

The `CocoarSignalARRR` Swift package provides a native client for iOS, macOS, tvOS, and watchOS. It is a full SignalR client built from scratch — no dependency on Microsoft's `signalr-client-swift`.

> **Info: Requirements**
>
> Swift 5.10+, iOS 14+ / macOS 11+ / tvOS 14+ / watchOS 7+. No external dependencies beyond Swift standard library and Foundation.

## Installation

Add the package to your `Package.swift`:

```swift
dependencies: [
    .package(url: "https://github.com/cocoar-dev/Cocoar.SignalARRR.git", from: "5.0.0"),
],
targets: [
    .target(
        name: "MyApp",
        dependencies: [
            .product(name: "CocoarSignalARRR", package: "Cocoar.SignalARRR"),
            .product(name: "CocoarSignalARRRMacros", package: "Cocoar.SignalARRR"),
        ]
    ),
]
```

## Create a connection

```swift
import CocoarSignalARRR

let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub"
)
```

## Authentication

A connection has two credentials. Usually they are the same token, so one option sets both:

```swift
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    options: HARRRConnectionOptions(credential: { await tokenStore.currentToken() })
)
```

To give them different credentials — a single-use connection ticket, say, which has no business being resent with every message — set them separately:

```swift
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    options: HARRRConnectionOptions(
        connectionCredential: { await connectionTicket() },
        messageCredential: { await tokenStore.currentToken() }
    )
)
```

| Option | Authenticates | Travels as | Checked by | When it expires |
|---|---|---|---|---|
| `connectionCredential` | negotiate and the transport | `Authorization` header | `[Authorize]` on the hub class, `.RequireAuthorization()` on the mapping | fetched again on every connect and reconnect |
| `messageCredential` | every message, the answer to a challenge, file transfers | the `Authorization` field of the message; a header on file transfers | `[Authorize]` on a method or a `ServerMethods` class | fetched on every use, so a refreshed token is sent from the next call on |
| `credential` | both of the above | | | |

The options are named the same in every SignalARRR client, and nothing is coupled implicitly: `messageCredential` alone leaves the connection anonymous, `connectionCredential` alone sends no message credential. A `connectionCredential` or `messageCredential` next to `credential` takes its part over. A value without a space is sent as a bearer token; one with a space carries its own scheme. See [Authorization](../server/authorization.md#the-two-credentials) for the same table across all clients.

The connection credential travels as a header on the negotiate request and on every request of the transport — the WebSocket upgrade, the SSE stream and its posts, every Long Polling request — never in a URL, where proxy logs and error reports would keep it. For a server that reads it only from the URL, set `transportCredential: .query`; see [where the connection token travels](../server/authorization.md#where-the-connection-token-travels).

Token challenges are handled automatically — while a stream is running the server may send a `ChallengeAuthentication` message, and the client calls the message credential to answer it.

`HARRRConnection.create` does not throw, so a credential set in two places surfaces from `start()` as a `HARRRConfigurationError`: `accessTokenFactory` next to a credential in `options`, or a connection credential passed to `create(client:)`, whose `SignalRWebSocketClient` authenticates the connection with its own factory.

> **Tip: Former way**
>
> `accessTokenFactory:` on `create(url:)` is the former way to set one factory for both credentials. It still works and keeps that behaviour; replace it with `options: HARRRConnectionOptions(credential: ...)`.

## All options

```swift
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    hubProtocol: .json,                         // or .messagepack
    transportCredential: .header,               // or .query: connection token in the transport URL
    options: HARRRConnectionOptions(credential: { await getAuthToken() }),
    serverTimeout: 30,
    keepAliveInterval: 15,
    handshakeTimeout: 15,
    reconnectPolicy: .default,                  // immediate, 2s, 10s, 30s — then give up
    allowedTransports: [.webSockets, .serverSentEvents, .longPolling],
    logLevel: .info
)
```

## Start and stop

```swift
try await connection.start()

// ... use the connection ...

await connection.stop()
```

## Invoke (call with return value)

```swift
let history: [String] = try await connection.invoke("ChatMethods.GetHistory")
let user: User = try await connection.invoke("UserMethods.GetUser", arguments: userId)
```

## Send (fire-and-forget)

```swift
try await connection.send("ChatMethods.SendMessage", arguments: "Alice", "Hello!")
```

## Stream

```swift
let stream: AsyncThrowingStream<String, Error> = try await connection.stream(
    "ChatMethods.StreamMessages"
)

for try await msg in stream {
    print(msg)
}
```

## Connection events

```swift
await connection.onClosed { error in
    print("Connection closed: \(error?.localizedDescription ?? "clean")")
}

await connection.onReconnecting { error in
    print("Reconnecting: \(error?.localizedDescription ?? "")")
}

await connection.onReconnected {
    print("Reconnected")
}
```

## Connection properties

| Property | Type | Description |
|----------|------|-------------|
| `connectionId` | `String?` | Current connection ID |
| `state` | `HubConnectionState` | Connection state (async) |
| `serverTimeoutInterval` | `TimeInterval` | Server timeout |
| `keepAliveIntervalValue` | `TimeInterval` | Keepalive interval |
| `handshakeTimeoutValue` | `TimeInterval` | Handshake timeout |

## MessagePack protocol

Use MessagePack instead of JSON for better performance and smaller payloads:

```swift
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    hubProtocol: .messagepack
)
```

No additional dependencies required — MessagePack is implemented natively in the client.

> **Info: Server setup**
>
> The server needs `.AddMessagePackProtocol()`. See [MessagePack](https://docs.cocoar.dev/signalarrr/roadmap/messagepack.html) for details.

## Reconnection policy

```swift
// Default: immediate retry, then 2s, 10s, 30s — then give up
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    reconnectPolicy: .default
)

// Custom delays
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    reconnectPolicy: ReconnectPolicy(retryDelays: [0, 1, 5, 10, 30])
)

// Disable reconnection
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    reconnectPolicy: .disabled
)
```

## Transport selection

The client tries transports in preference order and connects via the first one the server supports:

```swift
// Default: WebSockets → SSE → Long Polling
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    allowedTransports: [.webSockets, .serverSentEvents, .longPolling]
)

// Force WebSockets only
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    allowedTransports: [.webSockets]
)
```

## Logging

```swift
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    logLevel: .debug   // .debug, .info, .warning, .error, .none
)
```

Logs are emitted via `os_log` and visible in Xcode's console and macOS Console.app under the `com.cocoar.signalarrr` subsystem.

## Using SignalRWebSocketClient directly

`HARRRConnection` is the SignalARRR-specific wrapper. If you want to connect to any standard SignalR hub (without the SignalARRR server library), use `SignalRWebSocketClient` directly:

```swift
let client = SignalRWebSocketClient(
    url: "https://any-signalr-server.com/hub",
    hubProtocol: .messagepack
)
try await client.start()

let result: String = try await client.invoke(method: "MyMethod", arguments: ["param"])

client.on("OnMessage") { args in
    print(args)
    return nil
}
```

## Next steps

- [Typed Proxies & Server Methods](./typed-proxies.md) — `@HubProxy` macro and server-to-client handlers
- [Getting Started](../getting-started.md) — full setup walkthrough
- [Packages](../../reference/packages.md) — all available packages
