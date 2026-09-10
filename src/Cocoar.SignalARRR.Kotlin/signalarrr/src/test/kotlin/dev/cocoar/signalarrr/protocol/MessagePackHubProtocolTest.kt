package dev.cocoar.signalarrr.protocol

import dev.cocoar.signalarrr.HandshakeFailedException
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import org.junit.jupiter.api.Assertions.assertArrayEquals
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.msgpack.core.MessageBufferPacker
import org.msgpack.core.MessagePack
import org.msgpack.value.Value
import java.time.Instant
import java.util.Base64

class MessagePackHubProtocolTest {

    private val protocol = MessagePackHubProtocol()

    // --- test-side varint helpers, mirroring the protocol's own 7-bit-per-byte framing ---

    private fun varintPrefix(length: Int): ByteArray {
        val out = ArrayList<Byte>()
        var v = length
        do {
            var b = v and 0x7f
            v = v ushr 7
            if (v != 0) b = b or 0x80
            out.add(b.toByte())
        } while (v != 0)
        return out.toByteArray()
    }

    private fun stripLengthPrefix(data: ByteArray): ByteArray {
        var i = 0
        while (true) {
            val b = data[i].toInt() and 0xff
            i++
            if (b and 0x80 == 0) break
        }
        return data.copyOfRange(i, data.size)
    }

    private fun packPayload(body: (MessageBufferPacker) -> Unit): ByteArray {
        val packer = MessagePack.newDefaultBufferPacker()
        packer.use(body)
        return packer.toByteArray()
    }

    /** Builds a complete framed message (varint length prefix + msgpack body) for parseMessages tests. */
    private fun frame(body: (MessageBufferPacker) -> Unit): ByteArray {
        val payload = packPayload(body)
        return varintPrefix(payload.size) + payload
    }

    private fun unpackFrame(bytes: ByteArray): Value {
        val payload = stripLengthPrefix(bytes)
        return MessagePack.newDefaultUnpacker(payload).use { it.unpackValue() }
    }

    // --- writePing ---

    @Test
    fun `writePing is a varint length prefix plus array of 6`() {
        val bytes = protocol.writePing()
        val payload = packPayload { p ->
            p.packArrayHeader(1)
            p.packInt(6)
        }
        assertArrayEquals(varintPrefix(payload.size) + payload, bytes)

        val array = unpackFrame(bytes).asArrayValue().list()
        assertEquals(1, array.size)
        assertEquals(6, array[0].asIntegerValue().asInt())
    }

    // --- writeInvocation ---

    @Test
    fun `writeInvocation decodes to the expected array shape`() {
        val args = listOf(
            JsonPrimitive(5),
            JsonPrimitive("x"),
            JsonPrimitive(true),
            JsonNull,
            JsonArray(emptyList()),
            JsonObject(emptyMap()),
        )
        val bytes = protocol.writeInvocation("M", args, "1")
        val array = unpackFrame(bytes).asArrayValue().list()

        // [1, {}, "1", "M", [5,"x",true,null,[],{}], []]
        assertEquals(6, array.size)
        assertEquals(1, array[0].asIntegerValue().asInt())
        assertTrue(array[1].isMapValue)
        assertEquals(0, array[1].asMapValue().size())
        assertEquals("1", array[2].asStringValue().asString())
        assertEquals("M", array[3].asStringValue().asString())

        val argsArray = array[4].asArrayValue().list()
        assertEquals(6, argsArray.size)
        assertEquals(5, argsArray[0].asIntegerValue().asInt())
        assertEquals("x", argsArray[1].asStringValue().asString())
        assertTrue(argsArray[2].asBooleanValue().boolean)
        assertTrue(argsArray[3].isNilValue)
        assertTrue(argsArray[4].isArrayValue)
        assertEquals(0, argsArray[4].asArrayValue().size())
        assertTrue(argsArray[5].isMapValue)
        assertEquals(0, argsArray[5].asMapValue().size())

        val streamIds = array[5].asArrayValue().list()
        assertEquals(0, streamIds.size)
    }

    // --- writeCompletion ---

    @Test
    fun `writeCompletion with error`() {
        val bytes = protocol.writeCompletion("1", null, "err")
        val array = unpackFrame(bytes).asArrayValue().list()
        // [3, {}, id, 1, "err"]
        assertEquals(5, array.size)
        assertEquals(3, array[0].asIntegerValue().asInt())
        assertEquals(0, array[1].asMapValue().size())
        assertEquals("1", array[2].asStringValue().asString())
        assertEquals(1, array[3].asIntegerValue().asInt())
        assertEquals("err", array[4].asStringValue().asString())
    }

    @Test
    fun `writeCompletion with result`() {
        val bytes = protocol.writeCompletion("1", JsonPrimitive(42), null)
        val array = unpackFrame(bytes).asArrayValue().list()
        // [3, {}, id, 3, value]
        assertEquals(5, array.size)
        assertEquals(3, array[0].asIntegerValue().asInt())
        assertEquals("1", array[2].asStringValue().asString())
        assertEquals(3, array[3].asIntegerValue().asInt())
        assertEquals(42, array[4].asIntegerValue().asInt())
    }

    @Test
    fun `writeCompletion void`() {
        val bytes = protocol.writeCompletion("1", null, null)
        val array = unpackFrame(bytes).asArrayValue().list()
        // [3, {}, id, 2]
        assertEquals(4, array.size)
        assertEquals(3, array[0].asIntegerValue().asInt())
        assertEquals("1", array[2].asStringValue().asString())
        assertEquals(2, array[3].asIntegerValue().asInt())
    }

    // --- writeStreamInvocation / writeCancelInvocation ---

    @Test
    fun `writeStreamInvocation has type 4`() {
        val bytes = protocol.writeStreamInvocation("M", emptyList(), "1")
        val array = unpackFrame(bytes).asArrayValue().list()
        assertEquals(4, array[0].asIntegerValue().asInt())
    }

    @Test
    fun `writeCancelInvocation`() {
        val bytes = protocol.writeCancelInvocation("9")
        val array = unpackFrame(bytes).asArrayValue().list()
        // [5, {}, id]
        assertEquals(3, array.size)
        assertEquals(5, array[0].asIntegerValue().asInt())
        assertEquals(0, array[1].asMapValue().size())
        assertEquals("9", array[2].asStringValue().asString())
    }

    // --- parseMessages ---

    @Test
    fun `parseMessages parses Invocation`() {
        val data = frame { p ->
            p.packArrayHeader(6)
            p.packInt(1)
            p.packMapHeader(0)
            p.packNil()
            p.packString("Target")
            p.packArrayHeader(2)
            p.packInt(42)
            p.packString("s")
            p.packArrayHeader(0)
        }
        val invocation = protocol.parseMessages(data).single() as HubMessage.Invocation
        assertEquals("Target", invocation.target)
        assertEquals(listOf(JsonPrimitive(42), JsonPrimitive("s")), invocation.arguments)
        assertNull(invocation.invocationId)
    }

    @Test
    fun `parseMessages parses Completion with map result`() {
        val data = frame { p ->
            p.packArrayHeader(5)
            p.packInt(3)
            p.packMapHeader(0)
            p.packString("id1")
            p.packInt(3) // RESULT_KIND_NON_VOID
            p.packMapHeader(1)
            p.packString("k")
            p.packString("v")
        }
        val completion = protocol.parseMessages(data).single() as HubMessage.Completion
        assertEquals("id1", completion.invocationId)
        assertNull(completion.error)
        assertEquals(JsonObject(mapOf("k" to JsonPrimitive("v"))), completion.result)
    }

    @Test
    fun `parseMessages parses Completion with error result kind`() {
        val data = frame { p ->
            p.packArrayHeader(5)
            p.packInt(3)
            p.packMapHeader(0)
            p.packString("id2")
            p.packInt(1) // RESULT_KIND_ERROR
            p.packString("bad")
        }
        val completion = protocol.parseMessages(data).single() as HubMessage.Completion
        assertEquals("bad", completion.error)
        assertNull(completion.result)
    }

    @Test
    fun `parseMessages parses Completion with void result kind`() {
        val data = frame { p ->
            p.packArrayHeader(4)
            p.packInt(3)
            p.packMapHeader(0)
            p.packString("id3")
            p.packInt(2) // RESULT_KIND_VOID
        }
        val completion = protocol.parseMessages(data).single() as HubMessage.Completion
        assertNull(completion.error)
        assertNull(completion.result)
    }

    @Test
    fun `parseMessages parses StreamItem`() {
        val data = frame { p ->
            p.packArrayHeader(4)
            p.packInt(2)
            p.packMapHeader(0)
            p.packString("s1")
            p.packInt(7)
        }
        val item = protocol.parseMessages(data).single() as HubMessage.StreamItem
        assertEquals("s1", item.invocationId)
        assertEquals(JsonPrimitive(7), item.item)
    }

    @Test
    fun `parseMessages parses Ping`() {
        val data = frame { p ->
            p.packArrayHeader(1)
            p.packInt(6)
        }
        assertTrue(protocol.parseMessages(data).single() is HubMessage.Ping)
    }

    @Test
    fun `parseMessages parses Close`() {
        val data = frame { p ->
            p.packArrayHeader(3)
            p.packInt(7)
            p.packString("bye")
            p.packBoolean(true)
        }
        val close = protocol.parseMessages(data).single() as HubMessage.Close
        assertEquals("bye", close.error)
        assertTrue(close.allowReconnect)
    }

    @Test
    fun `parseMessages parses two concatenated messages`() {
        val ping = frame { p ->
            p.packArrayHeader(1)
            p.packInt(6)
        }
        val messages = protocol.parseMessages(ping + ping)
        assertEquals(2, messages.size)
        assertTrue(messages[0] is HubMessage.Ping)
        assertTrue(messages[1] is HubMessage.Ping)
    }

    @Test
    fun `parseMessages converts binary to base64 string`() {
        val payload = byteArrayOf(1, 2, 3, 4, 5)
        val data = frame { p ->
            p.packArrayHeader(6)
            p.packInt(1)
            p.packMapHeader(0)
            p.packNil()
            p.packString("Target")
            p.packArrayHeader(1)
            p.packBinaryHeader(payload.size)
            p.addPayload(payload)
            p.packArrayHeader(0)
        }
        val invocation = protocol.parseMessages(data).single() as HubMessage.Invocation
        val expected = JsonPrimitive(Base64.getEncoder().encodeToString(payload))
        assertEquals(expected, invocation.arguments.single())
    }

    @Test
    fun `parseMessages converts timestamp extension to ISO-8601 string`() {
        val instant = Instant.parse("2024-01-02T03:04:05Z")
        val data = frame { p ->
            p.packArrayHeader(6)
            p.packInt(1)
            p.packMapHeader(0)
            p.packNil()
            p.packString("Target")
            p.packArrayHeader(1)
            p.packTimestamp(instant)
            p.packArrayHeader(0)
        }
        val invocation = protocol.parseMessages(data).single() as HubMessage.Invocation
        assertEquals(JsonPrimitive(instant.toString()), invocation.arguments.single())
    }

    @Test
    fun `parseMessages handles a varint length prefix longer than one byte`() {
        val longString = "x".repeat(200)
        val data = frame { p ->
            p.packArrayHeader(6)
            p.packInt(1)
            p.packMapHeader(0)
            p.packNil()
            p.packString(longString)
            p.packArrayHeader(0)
            p.packArrayHeader(0)
        }
        assertTrue(data.size > 127)
        val invocation = protocol.parseMessages(data).single() as HubMessage.Invocation
        assertEquals(longString, invocation.target)
    }

    // --- parseHandshake ---

    @Test
    fun `parseHandshake works like the JSON handshake`() {
        val data = "{}".toByteArray(Charsets.UTF_8) + HubProtocol.RECORD_SEPARATOR
        val result = protocol.parseHandshake(data)
        assertNull(result.error)
        assertNull(result.remaining)
    }

    @Test
    fun `parseHandshake with error field`() {
        val data = "{\"error\":\"bad\"}".toByteArray(Charsets.UTF_8) + HubProtocol.RECORD_SEPARATOR
        val result = protocol.parseHandshake(data)
        assertEquals("bad", result.error)
    }

    @Test
    fun `parseHandshake with no separator throws`() {
        assertThrows(HandshakeFailedException::class.java) {
            protocol.parseHandshake("{}".toByteArray(Charsets.UTF_8))
        }
    }
}
