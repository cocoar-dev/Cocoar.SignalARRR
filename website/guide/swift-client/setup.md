# Swift Client Setup

The `CocoarSignalARRR` Swift package provides a native client for iOS, macOS, tvOS, and watchOS with support for `invoke`, `send`, `stream`, server-to-client method handling, HTTP stream references, and compile-time proxy generation via Swift macros.

::: info Requirements
Swift 5.10+, iOS 14+ / macOS 11+ / tvOS 14+ / watchOS 7+. Depends on Microsoft's `signalr-client-swift` (preview).
:::

## Installation

Add the package to your `Package.swift`:

```swift
dependencies: [
    .package(url: "https://github.com/cocoar-dev/Cocoar.SignalARRR.git", from: "4.0.0"),
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

let connection = await HARRRConnection.create { builder in
    builder.withUrl(url: "https://localhost:5001/apphub")
    builder.withAutoReconnect()
}
```

Or wrap an existing `HubConnection`:

```swift
let connection = await HARRRConnection.create(
    hubConnection: existingHubConnection
)
```

## Authentication

```swift
let connection = await HARRRConnection.create(
    { builder in
        builder.withUrl(url: "https://localhost:5001/apphub")
    },
    accessTokenFactory: {
        await getAuthToken()
    }
)
```

Token challenges are handled automatically — when the server detects an expired token, the client calls `accessTokenFactory` to get a fresh token.

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

## Access the raw HubConnection

```swift
let hubConnection = connection.asSignalRHubConnection()
```

## Next steps

- [Typed Proxies & Server Methods](/guide/swift-client/typed-proxies) — `@HubProxy` macro and server-to-client handlers
- [Getting Started](/guide/getting-started) — full setup walkthrough
- [Packages](/reference/packages) — all available packages
