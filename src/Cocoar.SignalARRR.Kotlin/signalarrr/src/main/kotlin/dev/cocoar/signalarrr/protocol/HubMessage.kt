package dev.cocoar.signalarrr.protocol

import kotlinx.serialization.json.JsonElement

/** SignalR hub protocol message types, as numbered by the protocol specification. */
public enum class HubMessageType(public val code: Int) {
    INVOCATION(1),
    STREAM_ITEM(2),
    COMPLETION(3),
    STREAM_INVOCATION(4),
    CANCEL_INVOCATION(5),
    PING(6),
    CLOSE(7),
    ;

    public companion object {
        public fun fromCode(code: Int): HubMessageType? = entries.firstOrNull { it.code == code }
    }
}

/** A parsed inbound hub protocol message. Arguments and results are carried as [JsonElement]. */
public sealed class HubMessage {
    public data class Invocation(
        val target: String,
        val arguments: List<JsonElement>,
        val invocationId: String?,
    ) : HubMessage()

    public data class StreamItem(val invocationId: String, val item: JsonElement) : HubMessage()

    /** [result] is `null` for void completions and for a `null` result alike. */
    public data class Completion(val invocationId: String, val error: String?, val result: JsonElement?) : HubMessage()

    public object Ping : HubMessage() {
        override fun toString(): String = "Ping"
    }

    public data class Close(val error: String?, val allowReconnect: Boolean) : HubMessage()
}

/** Result of parsing the handshake response. */
public class HandshakeResult(public val error: String?, public val remaining: ByteArray?)
