---
description: "The Kotlin client for Android and the JVM: Gradle setup, create a connection, invoke / send / stream with coroutines and Flow, errors and error codes, MessagePack, token authentication, reconnection, transports, logging, and using the plain SignalR layer"
---

# Kotlin Client Setup

The `dev.cocoar:signalarrr` package is a native client for Android and the JVM. It is a full SignalR client built from scratch — no dependency on Microsoft's Java client — with coroutines and `Flow` as its async model.

::: info Requirements
Kotlin 2.0+, JVM 8+ bytecode; Android API 21+. Dependencies: `kotlinx-coroutines`, `kotlinx-serialization-json`, OkHttp 5 and `msgpack-core`. No Android SDK dependency, so the same artifact serves server-side Kotlin and desktop apps.
:::

## Installation

```kotlin
// build.gradle.kts
plugins {
    kotlin("plugin.serialization") version "2.4.20"
    id("com.google.devtools.ksp") version "2.3.12"   // only for @HubProxy typed proxies
}

dependencies {
    implementation("dev.cocoar:signalarrr:5.2.0")
    ksp("dev.cocoar:signalarrr-ksp:5.2.0")            // only for @HubProxy typed proxies
}
```

The serialization plugin is what makes your own data classes `@Serializable`; the client encodes arguments and decodes results with `kotlinx.serialization`.

## Create a connection

```kotlin
import dev.cocoar.signalarrr.HARRRConnection

val connection = HARRRConnection.create("https://localhost:5001/apphub")
```

## Authentication

```kotlin
val connection = HARRRConnection.create("https://localhost:5001/apphub") {
    messageAccessTokenProvider = { tokenStore.currentToken() }
}
```

Token challenges are handled automatically — when the server detects an expired token, the client calls the provider again for a fresh one.

The provider authenticates two separate things, and by default it is used for both:

- **the connection** — sent as an `Authorization` header on the negotiate request and on every request of the transport: the WebSocket upgrade, the SSE stream and its posts, every Long Polling request. The token never appears in a URL, where proxy logs and error reports would keep it. It is fetched again on every connect and reconnect, so a renewed token is picked up. This is what an `[Authorize]` attribute *on the hub class* checks, and without it such a hub rejects the connection at `/negotiate` with 401. For a server that reads the connection token only from the URL, set `transportCredential = TransportCredential.QUERY`; see [where the connection token travels](/guide/server/authorization#where-the-connection-token-travels).
- **each message** — travels in `ClientRequestMessage.Authorization`. This is what `[Authorize]` on a method or a `ServerMethods` class checks, and it is what answers a token challenge.

To give them different credentials, set both providers:

```kotlin
val connection = HARRRConnection.create("https://localhost:5001/apphub") {
    accessTokenProvider = { connectionTicket() }         // authenticates the connection
    messageAccessTokenProvider = { apiToken() }          // authenticates each message
}
```

A value without a space is sent as a bearer token; one with a space carries its own scheme.

## All options

```kotlin
val connection = HARRRConnection.create("https://localhost:5001/apphub") {
    hubProtocol = HubProtocolKind.JSON                  // or MESSAGE_PACK
    messageAccessTokenProvider = { tokenStore.currentToken() }
    serverTimeout = 30.seconds
    keepAliveInterval = 15.seconds
    handshakeTimeout = 15.seconds
    reconnectPolicy = ReconnectPolicy.Default           // immediate, 2s, 10s, 30s — then give up
    allowedTransports = listOf(TransportType.WEB_SOCKETS, TransportType.SERVER_SENT_EVENTS, TransportType.LONG_POLLING)
    transportCredential = TransportCredential.HEADER    // or QUERY: connection token in the transport URL
    headers = mapOf("X-Api-Key" to apiKey)              // extra headers on negotiate and transport requests
    httpClient = sharedOkHttpClient                     // reuse the app's OkHttp client
    json = Json { ignoreUnknownKeys = true }            // the kotlinx.serialization configuration
    logger = LogcatLogger(LogLevel.INFO)                // your SignalARRRLogger implementation
}
```

## Start and stop

```kotlin
connection.start()

// ... use the connection ...

connection.stop()
```

Both are `suspend` functions. `stop()` fails pending calls with `DisconnectedException` and fires `onClosed` without an error.

## Invoke (call with return value)

```kotlin
val history: List<String> = connection.invoke("ChatMethods.GetHistory")
val user = connection.invoke<User>("UserMethods.GetUser", userId)
```

The result type is reified, so generic results like `List<Item>` or `Map<String, Int>` decode directly. Arguments are encoded structurally: primitives, `UUID`, `Instant`, collections and maps need nothing, and any other class must be `@Serializable`.

## Send (fire-and-forget)

```kotlin
connection.send("ChatMethods.SendMessage", "Alice", "Hello!")
```

## Stream

```kotlin
connection.stream<String>("ChatMethods.StreamMessages").collect { msg ->
    println(msg)
}
```

`stream` returns a cold `Flow`. Cancelling the collector — leaving the scope, `take(n)`, `first()` — sends `CancelInvocation` so the server stops producing.

## Errors

A failed call throws `HARRRException` carrying the server's structured error. Branch on `code`, never on the message:

```kotlin
try {
    connection.invoke<Unit>("RoomMethods.Join", roomId)
} catch (e: HARRRException) {
    when (e.code) {
        HARRRErrorCodes.UNAUTHORIZED -> promptLogin()
        HARRRErrorCodes.METHOD_NOT_FOUND -> reportContractMismatch(e)
        else -> when (e.error.code) {           // application codes travel verbatim
            "room_full" -> showRoomFull()
            else -> showGeneric(e.error.message)
        }
    }
}
```

`e.code` is the code folded to the set this client knows (unknown codes become `internal`); `e.error.code` is the raw wire value, which is where an application's own `HARRRException("room_full", ...)` codes appear. `e.error.innerError` nests the cause chain.

## Connection events

```kotlin
connection.onClosed { error -> println("Connection closed: ${error?.message ?: "clean"}") }
connection.onReconnecting { error -> println("Reconnecting: ${error?.message}") }
connection.onReconnected { connectionId -> println("Reconnected as $connectionId") }

// Or observe the state as a StateFlow — handy for Compose/ViewModels
connection.client.stateFlow.collect { state -> render(state) }
```

## Connection properties

| Property | Type | Description |
|----------|------|-------------|
| `connectionId` | `String?` | Current connection ID |
| `state` | `HubConnectionState` | `DISCONNECTED`, `CONNECTING`, `CONNECTED`, `RECONNECTING` |
| `client.stateFlow` | `StateFlow<HubConnectionState>` | The state as a flow |
| `client.transportType` | `TransportType?` | The transport in use |
| `serverTimeout`, `keepAliveInterval`, `handshakeTimeout` | `Duration` | The configured timeouts |

## MessagePack protocol

```kotlin
val connection = HARRRConnection.create("https://localhost:5001/apphub") {
    hubProtocol = HubProtocolKind.MESSAGE_PACK
}
```

MessagePack is built in (`msgpack-core`); nothing else to install. Server-Sent Events cannot carry binary frames, so the transport list falls back to WebSockets and Long Polling.

::: info Server setup
The server needs `.AddMessagePackProtocol()`. See [MessagePack](/roadmap/messagepack) for details.
:::

## Reconnection policy

```kotlin
// Default: immediate retry, then 2s, 10s, 30s — then give up
reconnectPolicy = ReconnectPolicy.Default

// Custom delays
reconnectPolicy = ReconnectPolicy(listOf(Duration.ZERO, 1.seconds, 5.seconds, 10.seconds, 30.seconds))

// Disable reconnection
reconnectPolicy = ReconnectPolicy.Disabled
```

A reconnect is a new connection: pending calls fail, `onReconnecting` fires, and after a successful attempt `onReconnected` reports the new connection id. Server-to-client handlers registered before stay registered. The server also treats the client as gone when it receives nothing for `serverTimeout`; the client pings every `keepAliveInterval` on every transport to prevent that, and closes the connection itself when the server has been silent for `serverTimeout`.

## Transport selection

The client tries transports in preference order and connects via the first one the server supports; a transport that fails to connect is skipped for the next:

```kotlin
// Default: WebSockets → SSE → Long Polling
allowedTransports = listOf(TransportType.WEB_SOCKETS, TransportType.SERVER_SENT_EVENTS, TransportType.LONG_POLLING)

// Force WebSockets only
allowedTransports = listOf(TransportType.WEB_SOCKETS)
```

## Logging

The client has no logging dependency. It writes to `System.err` by default; on Android, plug in Logcat:

```kotlin
class LogcatLogger(override val level: LogLevel) : SignalARRRLogger {
    override fun log(level: LogLevel, message: String, throwable: Throwable?) {
        when (level) {
            LogLevel.DEBUG -> Log.d("SignalARRR", message, throwable)
            LogLevel.INFO -> Log.i("SignalARRR", message, throwable)
            LogLevel.WARNING -> Log.w("SignalARRR", message, throwable)
            LogLevel.ERROR -> Log.e("SignalARRR", message, throwable)
            LogLevel.NONE -> Unit
        }
    }
}
```

`NoopLogger` silences the client entirely.

## Android notes

- Add `<uses-permission android:name="android.permission.INTERNET" />`.
- Cleartext `http://` URLs need `android:usesCleartextTraffic="true"` (or a network security config) on API 28+; emulators reach the host machine at `10.0.2.2`.
- Keep the connection in a `ViewModel` or a foreground service scope, not an `Activity`; the connection's own coroutines run on `Dispatchers.IO`, and handlers are dispatched there too — switch to `Dispatchers.Main` before touching views.
- With R8/ProGuard, keep the `@Serializable` classes' serializers as the kotlinx.serialization guide describes; the client itself needs no rules.

## Using SignalRClient directly

`HARRRConnection` is the SignalARRR-specific wrapper. To talk to any standard SignalR hub (without the SignalARRR server library), use `SignalRClient` directly. It speaks in `JsonElement`s:

```kotlin
val client = SignalRClient("https://any-signalr-server.com/hub") {
    hubProtocol = HubProtocolKind.MESSAGE_PACK
}
client.start()

val result = client.invoke("MyMethod", listOf(JsonPrimitive("param")))

client.on("OnMessage") { args ->
    println(args)
    null
}
```

`connection.client` exposes the same object underneath a `HARRRConnection`, and `connection.on(...)` registers raw SignalR event handlers with typed argument access.

## Next steps

- [Typed Proxies & Server Methods](./typed-proxies) — `@HubProxy` and server-to-client handlers
- [Getting Started](../getting-started) — full setup walkthrough
- [Packages](../../reference/packages) — all available packages
