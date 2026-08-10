# Typed Proxies & Server Methods

The Swift client supports compile-time proxy generation via the `@HubProxy` macro and server-to-client method handling.

## @HubProxy Macro

Mark a protocol with `@HubProxy` to generate a typed proxy class at build time:

```swift
import CocoarSignalARRRMacros

@HubProxy
protocol IChatHub {
    func sendMessage(user: String, message: String) async throws
    func getHistory() async throws -> [String]
    func streamMessages() async throws -> AsyncThrowingStream<String, Error>
}
```

The macro generates `IChatHubProxy` that routes each method to the correct connection call:

| Return type | Generated call | Protocol |
|-------------|----------------|----------|
| `async throws` (void) | `connection.send("IChatHub\|method", ...)` | `SendMessage` |
| `async throws -> T` | `connection.invoke("IChatHub\|method", ...)` | `InvokeMessageResult` |
| `async throws -> AsyncThrowingStream<T, Error>` | `connection.stream("IChatHub\|method", ...)` | `StreamMessage` |

## Use typed proxies

```swift
let chat = connection.getTypedMethods(IChatHubProxy.self)

try await chat.sendMessage(user: "Alice", message: "Hello!")
let history = try await chat.getHistory()

for try await msg in try await chat.streamMessages() {
    print(msg)
}
```

## Server-to-client handlers

Register handlers for methods the server can call on the client. The name is the contract's **wire
name** — `interface|method` — and it is matched exactly; a handler that does not match is not an
error, the call is simply dropped. See [Contract Wire Names](/guide/server/contracts-wire-names).

```swift
await connection.onServerMethod("MyApp.Contracts.IChatClient|ReceiveMessage") { args in
    let user = args[0] as! String
    let message = args[1] as! String
    print("\(user): \(message)")
    return AnyCodable(nilLiteral: ())
}

await connection.onServerMethod("MyApp.Contracts.IChatClient|GetClientName") { _ in
    return AnyCodable(stringLiteral: UIDevice.current.name)
}
```

If the contract declares its own names with `[MessageName]`, use those instead — for
`[MessageName("chat.client")]` on the interface and `[MessageName("received")]` on the member, the
name is `"chat.client|received"`.

Handlers return `AnyCodable` — a type-erased wrapper that supports all JSON-serializable types.

::: warning A throwing handler may go unnoticed
If the server awaited a result, the error travels back to it. If the server sent fire-and-forget, the
error is written to the client's logger as `Failed to handle server message '<name>'` and goes no
further — the server's send completed long before the handler ran. See
[what happens when a handler throws](/guide/dotnet-client/server-to-client#what-happens-when-a-handler-throws).
:::

## Streaming handlers

For server-to-client streaming methods, use `onServerStreamMethod`:

```swift
await connection.onServerStreamMethod("StreamData") { args in
    AsyncThrowingStream { continuation in
        for i in 0..<10 {
            continuation.yield(AnyCodable(integerLiteral: i))
        }
        continuation.finish()
    }
}
```

## Interface registration

For structured handler registration with a prefix:

```swift
await connection.registerHandlers(prefix: "ChatClient", handlers: [
    "ReceiveMessage": { args in
        // handle message
        return AnyCodable(nilLiteral: ())
    },
    "GetClientName": { _ in
        return AnyCodable(stringLiteral: "SwiftClient")
    },
])
```

Or implement the `ServerInterfaceHandler` protocol:

```swift
struct MyChatClient: ServerInterfaceHandler {
    static var interfaceName: String { "ChatClient" }

    func handlers() -> [String: @Sendable ([Any]) async throws -> AnyCodable] {
        [
            "ReceiveMessage": { args in /* ... */ },
            "GetClientName": { _ in AnyCodable(stringLiteral: "SwiftClient") },
        ]
    }
}

await connection.registerInterface(MyChatClient())
```

## Cancellation support

When the server passes a `CancellationToken` to a client method, the Swift client uses an actor-based `CancellationManager`. The handler's continuation is cancelled when the server sends `CancelTokenFromServer`.

## HTTP stream references

The Swift client supports `StreamReference` resolution. When a server-to-client call includes a `Stream` parameter, the client detects the `StreamReference` marker and downloads the data via HTTP:

```swift
let data = try await StreamReferenceResolver.resolve(streamRef)
```

::: info
Stream reference detection in server method dispatch is available. The client downloads via `URLSession`.
:::

## Next steps

- [Setup & Usage](/guide/swift-client/setup) — connection basics
- [Cancellation Propagation](/guide/advanced/cancellation) — how cancellation works across clients
- [HTTP Stream References](/guide/advanced/http-streams) — large file transfer
