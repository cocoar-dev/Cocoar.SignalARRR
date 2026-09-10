package dev.cocoar.signalarrr.protocol

import dev.cocoar.signalarrr.HandshakeFailedException
import dev.cocoar.signalarrr.HubProtocolKind
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/** The wire encoding of hub messages: JSON or MessagePack. */
public interface HubProtocol {
    public val kind: HubProtocolKind

    /** Whether messages travel as binary frames (MessagePack) or text (JSON). */
    public val isBinary: Boolean

    public fun writeHandshakeRequest(): ByteArray
    public fun parseHandshake(data: ByteArray): HandshakeResult

    public fun writeInvocation(target: String, arguments: List<JsonElement>, invocationId: String?): ByteArray
    public fun writeStreamInvocation(target: String, arguments: List<JsonElement>, invocationId: String): ByteArray
    public fun writeCompletion(invocationId: String, result: JsonElement?, error: String?): ByteArray
    public fun writeCancelInvocation(invocationId: String): ByteArray
    public fun writePing(): ByteArray

    public fun parseMessages(data: ByteArray): List<HubMessage>

    public companion object {
        /** ASCII record separator — the delimiter of the text-based framing. */
        public const val RECORD_SEPARATOR: Byte = 0x1e

        public fun create(kind: HubProtocolKind): HubProtocol = when (kind) {
            HubProtocolKind.JSON -> JsonHubProtocol()
            HubProtocolKind.MESSAGE_PACK -> MessagePackHubProtocol()
        }
    }
}

/**
 * Splits the text handshake response: JSON up to the first record separator, the rest is payload
 * that arrived bundled with it. Both protocols share this — the handshake is always JSON text.
 */
internal fun parseTextHandshake(data: ByteArray): HandshakeResult {
    val sep = data.indexOf(HubProtocol.RECORD_SEPARATOR)
    if (sep < 0) throw HandshakeFailedException("Incomplete handshake response")
    val chunk = data.copyOfRange(0, sep)
    val error = if (chunk.isEmpty()) {
        null
    } else {
        val obj = runCatching { Json.parseToJsonElement(String(chunk, Charsets.UTF_8)) }.getOrNull() as? JsonObject
        (obj?.get("error") as? JsonPrimitive)?.takeIf { it.isString }?.content
    }
    val remaining = if (sep + 1 < data.size) data.copyOfRange(sep + 1, data.size) else null
    return HandshakeResult(error, remaining)
}
