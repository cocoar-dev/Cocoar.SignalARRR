package dev.cocoar.signalarrr.protocol

import dev.cocoar.signalarrr.HubProtocolKind
import dev.cocoar.signalarrr.SerializationFailedException
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.msgpack.core.MessageBufferPacker
import org.msgpack.core.MessagePack
import org.msgpack.value.Value
import org.msgpack.value.ValueType
import java.io.ByteArrayOutputStream
import java.util.Base64

/**
 * The SignalR MessagePack hub protocol.
 *
 * Framing is a VarInt length prefix (7 bits per byte, high bit = continuation) followed by one
 * MessagePack array per message. The handshake is JSON text like the JSON protocol. Decoded
 * payloads are lifted into [JsonElement] so the rest of the client is protocol-agnostic: binary
 * becomes base64 (what System.Text.Json does with `byte[]`), timestamps become ISO-8601 strings.
 */
public class MessagePackHubProtocol : HubProtocol {
    override val kind: HubProtocolKind = HubProtocolKind.MESSAGE_PACK
    override val isBinary: Boolean = true

    override fun writeHandshakeRequest(): ByteArray =
        "{\"protocol\":\"messagepack\",\"version\":1}".toByteArray(Charsets.UTF_8) + HubProtocol.RECORD_SEPARATOR

    override fun parseHandshake(data: ByteArray): HandshakeResult = parseTextHandshake(data)

    override fun writeInvocation(target: String, arguments: List<JsonElement>, invocationId: String?): ByteArray =
        pack { p ->
            p.packArrayHeader(6)
            p.packInt(HubMessageType.INVOCATION.code)
            p.packMapHeader(0)
            if (invocationId == null) p.packNil() else p.packString(invocationId)
            p.packString(target)
            p.packArrayHeader(arguments.size)
            arguments.forEach { writeElement(p, it) }
            p.packArrayHeader(0)
        }

    override fun writeStreamInvocation(target: String, arguments: List<JsonElement>, invocationId: String): ByteArray =
        pack { p ->
            p.packArrayHeader(6)
            p.packInt(HubMessageType.STREAM_INVOCATION.code)
            p.packMapHeader(0)
            p.packString(invocationId)
            p.packString(target)
            p.packArrayHeader(arguments.size)
            arguments.forEach { writeElement(p, it) }
            p.packArrayHeader(0)
        }

    override fun writeCompletion(invocationId: String, result: JsonElement?, error: String?): ByteArray =
        pack { p ->
            when {
                error != null -> {
                    p.packArrayHeader(5)
                    p.packInt(HubMessageType.COMPLETION.code)
                    p.packMapHeader(0)
                    p.packString(invocationId)
                    p.packInt(RESULT_KIND_ERROR)
                    p.packString(error)
                }
                result != null -> {
                    p.packArrayHeader(5)
                    p.packInt(HubMessageType.COMPLETION.code)
                    p.packMapHeader(0)
                    p.packString(invocationId)
                    p.packInt(RESULT_KIND_NON_VOID)
                    writeElement(p, result)
                }
                else -> {
                    p.packArrayHeader(4)
                    p.packInt(HubMessageType.COMPLETION.code)
                    p.packMapHeader(0)
                    p.packString(invocationId)
                    p.packInt(RESULT_KIND_VOID)
                }
            }
        }

    override fun writeCancelInvocation(invocationId: String): ByteArray =
        pack { p ->
            p.packArrayHeader(3)
            p.packInt(HubMessageType.CANCEL_INVOCATION.code)
            p.packMapHeader(0)
            p.packString(invocationId)
        }

    override fun writePing(): ByteArray =
        pack { p ->
            p.packArrayHeader(1)
            p.packInt(HubMessageType.PING.code)
        }

    override fun parseMessages(data: ByteArray): List<HubMessage> {
        val messages = mutableListOf<HubMessage>()
        var offset = 0
        while (offset < data.size) {
            val (length, prefixSize) = readVarInt(data, offset) ?: break
            offset += prefixSize
            if (length <= 0 || offset + length > data.size) break
            val slice = data.copyOfRange(offset, offset + length)
            offset += length
            runCatching { parseOne(slice) }.getOrNull()?.let(messages::add)
        }
        return messages
    }

    // --- Framing ---

    private fun pack(body: (MessageBufferPacker) -> Unit): ByteArray {
        val packer = MessagePack.newDefaultBufferPacker()
        packer.use { body(it) }
        val payload = packer.toByteArray()
        val out = ByteArrayOutputStream(payload.size + 5)
        writeVarInt(out, payload.size)
        out.write(payload)
        return out.toByteArray()
    }

    private fun writeVarInt(out: ByteArrayOutputStream, value: Int) {
        var v = value
        do {
            var b = v and 0x7f
            v = v ushr 7
            if (v != 0) b = b or 0x80
            out.write(b)
        } while (v != 0)
    }

    /** Returns (value, bytesConsumed) or `null` for a truncated prefix. */
    private fun readVarInt(data: ByteArray, start: Int): Pair<Int, Int>? {
        var result = 0
        var shift = 0
        var i = start
        while (i < data.size && shift < 35) {
            val b = data[i].toInt() and 0xff
            result = result or ((b and 0x7f) shl shift)
            i++
            if (b and 0x80 == 0) return result to (i - start)
            shift += 7
        }
        return null
    }

    // --- Message layout ---

    private fun parseOne(bytes: ByteArray): HubMessage? {
        val value = MessagePack.newDefaultUnpacker(bytes).use { it.unpackValue() }
        if (!value.isArrayValue) return null
        val array = value.asArrayValue().list()
        if (array.isEmpty() || !array[0].isIntegerValue) return null
        return when (HubMessageType.fromCode(array[0].asIntegerValue().toInt())) {
            HubMessageType.INVOCATION -> {
                if (array.size < 5) return null
                HubMessage.Invocation(
                    target = array[3].stringOrEmpty(),
                    arguments = array[4].takeIf { it.isArrayValue }?.asArrayValue()?.list()?.map(::toJson) ?: emptyList(),
                    invocationId = array[2].takeIf { it.isStringValue }?.asStringValue()?.asString(),
                )
            }
            HubMessageType.STREAM_ITEM -> {
                if (array.size < 4) return null
                HubMessage.StreamItem(array[2].stringOrEmpty(), toJson(array[3]))
            }
            HubMessageType.COMPLETION -> {
                if (array.size < 4) return null
                val id = array[2].stringOrEmpty()
                when (array[3].takeIf { it.isIntegerValue }?.asIntegerValue()?.toInt()) {
                    RESULT_KIND_ERROR -> HubMessage.Completion(
                        id,
                        error = array.getOrNull(4)?.takeIf { it.isStringValue }?.asStringValue()?.asString() ?: "Server error",
                        result = null,
                    )
                    RESULT_KIND_NON_VOID -> HubMessage.Completion(
                        id,
                        error = null,
                        result = array.getOrNull(4)?.let(::toJson)?.takeUnless { it is JsonNull },
                    )
                    else -> HubMessage.Completion(id, error = null, result = null)
                }
            }
            HubMessageType.PING -> HubMessage.Ping
            HubMessageType.CLOSE -> HubMessage.Close(
                error = array.getOrNull(1)?.takeIf { it.isStringValue }?.asStringValue()?.asString(),
                allowReconnect = array.getOrNull(2)?.takeIf { it.isBooleanValue }?.asBooleanValue()?.boolean ?: false,
            )
            HubMessageType.STREAM_INVOCATION, HubMessageType.CANCEL_INVOCATION, null -> null
        }
    }

    private fun Value.stringOrEmpty(): String = if (isStringValue) asStringValue().asString() else ""

    // --- Value conversion ---

    private fun writeElement(p: MessageBufferPacker, element: JsonElement) {
        when (element) {
            is JsonNull -> p.packNil()
            is JsonPrimitive -> writePrimitive(p, element)
            is JsonArray -> {
                p.packArrayHeader(element.size)
                element.forEach { writeElement(p, it) }
            }
            is JsonObject -> {
                p.packMapHeader(element.size)
                element.forEach { (k, v) ->
                    p.packString(k)
                    writeElement(p, v)
                }
            }
        }
    }

    private fun writePrimitive(p: MessageBufferPacker, primitive: JsonPrimitive) {
        if (primitive.isString) {
            p.packString(primitive.content)
            return
        }
        when (val content = primitive.content) {
            "true" -> p.packBoolean(true)
            "false" -> p.packBoolean(false)
            "null" -> p.packNil()
            else -> {
                content.toLongOrNull()?.let { p.packLong(it); return }
                content.toBigIntegerOrNull()?.let { p.packBigInteger(it); return }
                content.toDoubleOrNull()?.let { p.packDouble(it); return }
                throw SerializationFailedException("Cannot encode JSON literal '$content' as MessagePack")
            }
        }
    }

    private fun toJson(value: Value): JsonElement = when (value.valueType) {
        ValueType.NIL -> JsonNull
        ValueType.BOOLEAN -> JsonPrimitive(value.asBooleanValue().boolean)
        ValueType.INTEGER -> {
            val iv = value.asIntegerValue()
            if (iv.isInLongRange) JsonPrimitive(iv.toLong()) else JsonPrimitive(iv.toBigInteger())
        }
        ValueType.FLOAT -> JsonPrimitive(value.asFloatValue().toDouble())
        ValueType.STRING -> JsonPrimitive(value.asStringValue().asString())
        ValueType.BINARY -> JsonPrimitive(Base64.getEncoder().encodeToString(value.asBinaryValue().asByteArray()))
        ValueType.ARRAY -> JsonArray(value.asArrayValue().list().map(::toJson))
        ValueType.MAP -> JsonObject(
            value.asMapValue().map().entries.associate { (k, v) ->
                (if (k.isStringValue) k.asStringValue().asString() else k.toString()) to toJson(v)
            },
        )
        ValueType.EXTENSION -> {
            val ext = value.asExtensionValue()
            if (ext.isTimestampValue) {
                JsonPrimitive(ext.asTimestampValue().toInstant().toString())
            } else {
                JsonPrimitive(Base64.getEncoder().encodeToString(ext.data))
            }
        }
        null -> JsonNull
    }

    private companion object {
        const val RESULT_KIND_ERROR = 1
        const val RESULT_KIND_VOID = 2
        const val RESULT_KIND_NON_VOID = 3
    }
}
