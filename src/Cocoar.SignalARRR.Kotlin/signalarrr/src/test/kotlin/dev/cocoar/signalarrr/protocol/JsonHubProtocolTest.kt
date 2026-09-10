package dev.cocoar.signalarrr.protocol

import dev.cocoar.signalarrr.HandshakeFailedException
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class JsonHubProtocolTest {

    private val protocol = JsonHubProtocol()
    private val rs = HubProtocol.RECORD_SEPARATOR

    private fun wire(text: String): ByteArray = text.toByteArray(Charsets.UTF_8) + rs

    private fun withoutSeparator(bytes: ByteArray): String {
        assertEquals(rs, bytes.last())
        return String(bytes, 0, bytes.size - 1, Charsets.UTF_8)
    }

    private fun JsonPrimitive.int(): Int = content.toInt()

    // --- writeHandshakeRequest ---

    @Test
    fun `writeHandshakeRequest ends with record separator and contains protocol json`() {
        val bytes = protocol.writeHandshakeRequest()
        assertEquals(rs, bytes.last())
        val text = String(bytes, Charsets.UTF_8)
        assertTrue(text.contains("\"protocol\":\"json\""))
    }

    // --- writeInvocation ---

    @Test
    fun `writeInvocation with invocationId produces expected JSON fields`() {
        val bytes = protocol.writeInvocation("Method", listOf(JsonPrimitive(1), JsonPrimitive("x")), "42")
        val obj = Json.parseToJsonElement(withoutSeparator(bytes)).jsonObject
        assertEquals(1, obj["type"]!!.jsonPrimitive.int())
        assertEquals("42", obj["invocationId"]!!.jsonPrimitive.content)
        assertEquals("Method", obj["target"]!!.jsonPrimitive.content)
        assertEquals(listOf(JsonPrimitive(1), JsonPrimitive("x")), obj["arguments"]!!.jsonArrayElements())
    }

    @Test
    fun `writeInvocation without invocationId omits invocationId field`() {
        val bytes = protocol.writeInvocation("Method", emptyList(), null)
        val obj = Json.parseToJsonElement(withoutSeparator(bytes)).jsonObject
        assertFalse(obj.containsKey("invocationId"))
        assertEquals("Method", obj["target"]!!.jsonPrimitive.content)
    }

    // --- writeCompletion ---

    @Test
    fun `writeCompletion with result`() {
        val bytes = protocol.writeCompletion("1", JsonPrimitive(5), null)
        val obj = Json.parseToJsonElement(withoutSeparator(bytes)).jsonObject
        assertEquals(3, obj["type"]!!.jsonPrimitive.int())
        assertEquals("1", obj["invocationId"]!!.jsonPrimitive.content)
        assertEquals(5, obj["result"]!!.jsonPrimitive.int())
        assertFalse(obj.containsKey("error"))
    }

    @Test
    fun `writeCompletion with error`() {
        val bytes = protocol.writeCompletion("1", null, "boom")
        val obj = Json.parseToJsonElement(withoutSeparator(bytes)).jsonObject
        assertEquals("boom", obj["error"]!!.jsonPrimitive.content)
        assertFalse(obj.containsKey("result"))
    }

    @Test
    fun `writeCompletion void has null result`() {
        val bytes = protocol.writeCompletion("1", null, null)
        val obj = Json.parseToJsonElement(withoutSeparator(bytes)).jsonObject
        assertTrue(obj["result"] is JsonNull)
        assertFalse(obj.containsKey("error"))
    }

    // --- writeCancelInvocation / writePing ---

    @Test
    fun `writeCancelInvocation`() {
        val bytes = protocol.writeCancelInvocation("7")
        val obj = Json.parseToJsonElement(withoutSeparator(bytes)).jsonObject
        assertEquals(5, obj["type"]!!.jsonPrimitive.int())
        assertEquals("7", obj["invocationId"]!!.jsonPrimitive.content)
    }

    @Test
    fun `writePing`() {
        val bytes = protocol.writePing()
        val obj = Json.parseToJsonElement(withoutSeparator(bytes)).jsonObject
        assertEquals(6, obj["type"]!!.jsonPrimitive.int())
    }

    // --- parseMessages ---

    @Test
    fun `parseMessages splits multiple concatenated messages`() {
        val data = wire("{\"type\":6}") + wire("{\"type\":6}")
        val messages = protocol.parseMessages(data)
        assertEquals(2, messages.size)
        assertTrue(messages[0] is HubMessage.Ping)
        assertTrue(messages[1] is HubMessage.Ping)
    }

    @Test
    fun `parseMessages skips empty chunks and unknown types`() {
        // A leading separator produces an empty chunk; type 99 is unknown.
        val data = byteArrayOf(rs) + wire("{\"type\":6}") + wire("{\"type\":99}")
        val messages = protocol.parseMessages(data)
        assertEquals(1, messages.size)
        assertTrue(messages[0] is HubMessage.Ping)
    }

    @Test
    fun `parseMessages parses Invocation with invocationId`() {
        val data = wire("{\"type\":1,\"invocationId\":\"5\",\"target\":\"Foo\",\"arguments\":[1,\"a\"]}")
        val messages = protocol.parseMessages(data)
        val invocation = messages.single() as HubMessage.Invocation
        assertEquals("Foo", invocation.target)
        assertEquals("5", invocation.invocationId)
        assertEquals(listOf(JsonPrimitive(1), JsonPrimitive("a")), invocation.arguments)
    }

    @Test
    fun `parseMessages parses Invocation without invocationId as null`() {
        val data = wire("{\"type\":1,\"target\":\"Foo\",\"arguments\":[]}")
        val messages = protocol.parseMessages(data)
        val invocation = messages.single() as HubMessage.Invocation
        assertNull(invocation.invocationId)
    }

    @Test
    fun `parseMessages parses StreamItem`() {
        val data = wire("{\"type\":2,\"invocationId\":\"3\",\"item\":42}")
        val messages = protocol.parseMessages(data)
        val item = messages.single() as HubMessage.StreamItem
        assertEquals("3", item.invocationId)
        assertEquals(JsonPrimitive(42), item.item)
    }

    @Test
    fun `parseMessages parses Completion with result`() {
        val data = wire("{\"type\":3,\"invocationId\":\"3\",\"result\":42}")
        val messages = protocol.parseMessages(data)
        val completion = messages.single() as HubMessage.Completion
        assertEquals("3", completion.invocationId)
        assertNull(completion.error)
        assertEquals(JsonPrimitive(42), completion.result)
    }

    @Test
    fun `parseMessages parses Completion with error`() {
        val data = wire("{\"type\":3,\"invocationId\":\"3\",\"error\":\"boom\"}")
        val messages = protocol.parseMessages(data)
        val completion = messages.single() as HubMessage.Completion
        assertEquals("boom", completion.error)
        assertNull(completion.result)
    }

    @Test
    fun `parseMessages parses Completion with null result as null`() {
        val data = wire("{\"type\":3,\"invocationId\":\"3\",\"result\":null}")
        val messages = protocol.parseMessages(data)
        val completion = messages.single() as HubMessage.Completion
        assertNull(completion.error)
        assertNull(completion.result)
    }

    @Test
    fun `parseMessages parses Ping`() {
        val messages = protocol.parseMessages(wire("{\"type\":6}"))
        assertTrue(messages.single() is HubMessage.Ping)
    }

    @Test
    fun `parseMessages parses Close with error and allowReconnect`() {
        val data = wire("{\"type\":7,\"error\":\"bye\",\"allowReconnect\":true}")
        val messages = protocol.parseMessages(data)
        val close = messages.single() as HubMessage.Close
        assertEquals("bye", close.error)
        assertTrue(close.allowReconnect)
    }

    // --- parseHandshake ---

    @Test
    fun `parseHandshake with empty object has no error and no remaining`() {
        val result = protocol.parseHandshake(wire("{}"))
        assertNull(result.error)
        assertNull(result.remaining)
    }

    @Test
    fun `parseHandshake with error field`() {
        val result = protocol.parseHandshake(wire("{\"error\":\"bad\"}"))
        assertEquals("bad", result.error)
    }

    @Test
    fun `parseHandshake with bundled remaining bytes`() {
        val data = wire("{}") + wire("{\"type\":6}")
        val result = protocol.parseHandshake(data)
        assertNull(result.error)
        assertTrue(String(result.remaining!!, Charsets.UTF_8).contains("\"type\":6"))
    }

    @Test
    fun `parseHandshake with no separator throws HandshakeFailedException`() {
        assertThrows(HandshakeFailedException::class.java) {
            protocol.parseHandshake("{}".toByteArray(Charsets.UTF_8))
        }
    }

    private fun kotlinx.serialization.json.JsonElement.jsonArrayElements(): List<kotlinx.serialization.json.JsonElement> =
        (this as kotlinx.serialization.json.JsonArray).toList()
}
