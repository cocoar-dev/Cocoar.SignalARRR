package dev.cocoar.signalarrr.protocol

import dev.cocoar.signalarrr.HubProtocolKind
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonObject

/** The SignalR JSON hub protocol: one JSON object per message, terminated by the record separator. */
public class JsonHubProtocol : HubProtocol {
    override val kind: HubProtocolKind = HubProtocolKind.JSON
    override val isBinary: Boolean = false

    private val json = Json

    override fun writeHandshakeRequest(): ByteArray =
        toWire("{\"protocol\":\"json\",\"version\":1}")

    override fun parseHandshake(data: ByteArray): HandshakeResult = parseTextHandshake(data)

    override fun writeInvocation(target: String, arguments: List<JsonElement>, invocationId: String?): ByteArray {
        val fields = linkedMapOf<String, JsonElement>(
            "type" to JsonPrimitive(HubMessageType.INVOCATION.code),
        )
        if (invocationId != null) fields["invocationId"] = JsonPrimitive(invocationId)
        fields["target"] = JsonPrimitive(target)
        fields["arguments"] = JsonArray(arguments)
        return toWire(JsonObject(fields).toString())
    }

    override fun writeStreamInvocation(target: String, arguments: List<JsonElement>, invocationId: String): ByteArray {
        val obj = JsonObject(
            linkedMapOf(
                "type" to JsonPrimitive(HubMessageType.STREAM_INVOCATION.code),
                "invocationId" to JsonPrimitive(invocationId),
                "target" to JsonPrimitive(target),
                "arguments" to JsonArray(arguments),
            ),
        )
        return toWire(obj.toString())
    }

    override fun writeCompletion(invocationId: String, result: JsonElement?, error: String?): ByteArray {
        val fields = linkedMapOf<String, JsonElement>(
            "type" to JsonPrimitive(HubMessageType.COMPLETION.code),
            "invocationId" to JsonPrimitive(invocationId),
        )
        if (error != null) {
            fields["error"] = JsonPrimitive(error)
        } else {
            fields["result"] = result ?: JsonNull
        }
        return toWire(JsonObject(fields).toString())
    }

    override fun writeCancelInvocation(invocationId: String): ByteArray {
        val obj = JsonObject(
            linkedMapOf(
                "type" to JsonPrimitive(HubMessageType.CANCEL_INVOCATION.code),
                "invocationId" to JsonPrimitive(invocationId),
            ),
        )
        return toWire(obj.toString())
    }

    override fun writePing(): ByteArray = toWire("{\"type\":6}")

    override fun parseMessages(data: ByteArray): List<HubMessage> {
        val messages = mutableListOf<HubMessage>()
        var start = 0
        for (i in data.indices) {
            if (data[i] == HubProtocol.RECORD_SEPARATOR) {
                if (i > start) parseMessage(String(data, start, i - start, Charsets.UTF_8))?.let(messages::add)
                start = i + 1
            }
        }
        // A trailing chunk without a separator is not a complete message; SignalR always terminates.
        return messages
    }

    private fun parseMessage(text: String): HubMessage? {
        val obj = runCatching { json.parseToJsonElement(text).jsonObject }.getOrNull() ?: return null
        val typeCode = (obj["type"] as? JsonPrimitive)?.intOrNull ?: return null
        return when (HubMessageType.fromCode(typeCode)) {
            HubMessageType.INVOCATION -> HubMessage.Invocation(
                target = obj.string("target") ?: "",
                arguments = (obj["arguments"] as? JsonArray)?.toList() ?: emptyList(),
                invocationId = obj.string("invocationId"),
            )
            HubMessageType.STREAM_ITEM -> HubMessage.StreamItem(
                invocationId = obj.string("invocationId") ?: "",
                item = obj["item"] ?: JsonNull,
            )
            HubMessageType.COMPLETION -> HubMessage.Completion(
                invocationId = obj.string("invocationId") ?: "",
                error = obj.string("error"),
                result = obj["result"]?.takeUnless { it is JsonNull },
            )
            HubMessageType.PING -> HubMessage.Ping
            HubMessageType.CLOSE -> HubMessage.Close(
                error = obj.string("error"),
                allowReconnect = (obj["allowReconnect"] as? JsonPrimitive)?.booleanOrNull ?: false,
            )
            HubMessageType.STREAM_INVOCATION, HubMessageType.CANCEL_INVOCATION, null -> null
        }
    }

    private fun JsonObject.string(key: String): String? =
        (this[key] as? JsonPrimitive)?.takeIf { it.isString }?.content

    private fun toWire(text: String): ByteArray = text.toByteArray(Charsets.UTF_8) + HubProtocol.RECORD_SEPARATOR
}
