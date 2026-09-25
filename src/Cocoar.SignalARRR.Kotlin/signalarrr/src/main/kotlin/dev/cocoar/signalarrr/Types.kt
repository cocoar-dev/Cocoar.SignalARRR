package dev.cocoar.signalarrr

import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/** State of the underlying SignalR connection. */
public enum class HubConnectionState {
    DISCONNECTED,
    CONNECTING,
    CONNECTED,
    RECONNECTING,
}

/** SignalR transports, named as the server lists them in the negotiate response. */
public enum class TransportType(public val wireName: String) {
    WEB_SOCKETS("WebSockets"),
    SERVER_SENT_EVENTS("ServerSentEvents"),
    LONG_POLLING("LongPolling"),
}

/** Where the transport carries the connection token (negotiate always sends it as a header). */
public enum class TransportCredential {
    /** `Authorization` header on the WebSocket upgrade and on every SSE and Long Polling request. */
    HEADER,

    /** `access_token` query parameter of the transport URL, for servers that read it only there. */
    QUERY,
}

/** The hub protocol the connection speaks. */
public enum class HubProtocolKind(public val wireName: String) {
    JSON("json"),
    MESSAGE_PACK("messagepack"),
}

/**
 * Automatic reconnection: one delay per attempt. After the last delay is exhausted the
 * connection is considered lost and `onClosed` fires. An empty list disables reconnection.
 */
public class ReconnectPolicy(public val retryDelays: List<Duration>) {
    public companion object {
        /** Immediately, then 2s, 10s, 30s — then give up. Matches the other clients. */
        public val Default: ReconnectPolicy =
            ReconnectPolicy(listOf(Duration.ZERO, 2.seconds, 10.seconds, 30.seconds))

        /** No automatic reconnection. */
        public val Disabled: ReconnectPolicy = ReconnectPolicy(emptyList())
    }

    override fun toString(): String = "ReconnectPolicy(retryDelays=$retryDelays)"
}
