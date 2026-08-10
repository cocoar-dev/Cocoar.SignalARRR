# Swift Client Setup

The `CocoarSignalARRR` Swift package provides a native client for iOS, macOS, tvOS, and watchOS. It is a full SignalR client built from scratch — no dependency on Microsoft's `signalr-client-swift`.

::: info Requirements
Swift 5.10+, iOS 14+ / macOS 11+ / tvOS 14+ / watchOS 7+. No external dependencies beyond Swift standard library and Foundation.
:::

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

```swift
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    accessTokenFactory: {
        await getAuthToken()
    }
)
```

Token challenges are handled automatically — when the server detects an expired token, the client calls `accessTokenFactory` to get a fresh token.

## All options

```swift
let connection = await HARRRConnection.create(
    url: "https://localhost:5001/apphub",
    hubProtocol: .json,                         // or .messagepack
    accessTokenFactory: { await getAuthToken() },
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

::: info Server setup
The server needs `.AddMessagePackProtocol()`. See [MessagePack](/roadmap/messagepack) for details.
:::

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

- [Typed Proxies & Server Methods](/guide/swift-client/typed-proxies) — `@HubProxy` macro and server-to-client handlers
- [Getting Started](/guide/getting-started) — full setup walkthrough
- [Packages](/reference/packages) — all available packages
