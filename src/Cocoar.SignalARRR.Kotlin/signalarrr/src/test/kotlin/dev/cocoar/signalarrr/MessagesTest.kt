package dev.cocoar.signalarrr

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

class MessagesTest {

    private val json = Json { ignoreUnknownKeys = true }

    // --- ClientRequestMessage ---

    @Test
    fun `ClientRequestMessage encodes to PascalCase keys`() {
        val message = ClientRequestMessage(
            method = "DoThing",
            arguments = listOf(JsonPrimitive(1)),
            authorization = "Bearer x",
            genericArguments = listOf("System.String"),
        )
        val element = json.encodeToJsonElement(ClientRequestMessage.serializer(), message).jsonObject

        assertTrue(element.containsKey("Method"))
        assertTrue(element.containsKey("Arguments"))
        assertTrue(element.containsKey("Authorization"))
        assertTrue(element.containsKey("GenericArguments"))
        assertEquals("DoThing", element["Method"]!!.jsonPrimitive.content)
        assertEquals("Bearer x", element["Authorization"]!!.jsonPrimitive.content)
    }

    // --- ServerRequestMessage ---

    @Test
    fun `ServerRequestMessage decodes full payload`() {
        val text = """{"Id":"1","Method":"M","Arguments":[1,"a"],"CancellationGuid":"g","StreamId":null}"""
        val message = json.decodeFromString(ServerRequestMessage.serializer(), text)

        assertEquals("1", message.id)
        assertEquals("M", message.method)
        assertEquals(listOf(JsonPrimitive(1), JsonPrimitive("a")), message.arguments)
        assertEquals("g", message.cancellationGuid)
        assertNull(message.streamId)
    }

    @Test
    fun `ServerRequestMessage decodes minimal payload`() {
        val text = """{"Id":"1","Method":"M"}"""
        val message = json.decodeFromString(ServerRequestMessage.serializer(), text)

        assertEquals("1", message.id)
        assertEquals("M", message.method)
        assertNull(message.arguments)
        assertNull(message.cancellationGuid)
        assertNull(message.streamId)
    }

    // --- CancellationTokenReference.from ---

    @Test
    fun `CancellationTokenReference recognises the marked form`() {
        val guid = "11111111-2222-3333-4444-555555555555"
        val element = Json.parseToJsonElement("""{"__type":"cancellationToken","Id":"$guid"}""")

        val ref = CancellationTokenReference.from(element)

        assertEquals(guid, ref?.id)
    }

    @Test
    fun `CancellationTokenReference rejects a different marker`() {
        val guid = "11111111-2222-3333-4444-555555555555"
        val element = Json.parseToJsonElement("""{"__type":"stream","Id":"$guid"}""")

        assertNull(CancellationTokenReference.from(element))
    }

    @Test
    fun `CancellationTokenReference recognises an unmarked lone Id that is a GUID`() {
        val guid = "11111111-2222-3333-4444-555555555555"
        val element = Json.parseToJsonElement("""{"Id":"$guid"}""")

        assertEquals(guid, CancellationTokenReference.from(element)?.id)
    }

    @Test
    fun `CancellationTokenReference rejects an unmarked lone Id that is not a GUID`() {
        val element = Json.parseToJsonElement("""{"Id":"not-a-guid"}""")

        assertNull(CancellationTokenReference.from(element))
    }

    @Test
    fun `CancellationTokenReference rejects an unmarked Id with extra fields`() {
        val guid = "11111111-2222-3333-4444-555555555555"
        val element = Json.parseToJsonElement("""{"Id":"$guid","Other":1}""")

        assertNull(CancellationTokenReference.from(element))
    }

    // --- StreamReference.from ---

    @Test
    fun `StreamReference recognises the marked stream form`() {
        val element = Json.parseToJsonElement("""{"__type":"stream","Uri":"http://x"}""")

        assertEquals("http://x", StreamReference.from(element)?.uri)
    }

    @Test
    fun `StreamReference rejects a cancellationToken marker even with a Uri`() {
        val element = Json.parseToJsonElement("""{"__type":"cancellationToken","Uri":"http://x"}""")

        assertNull(StreamReference.from(element))
    }

    @Test
    fun `StreamReference recognises an unmarked lone Uri`() {
        val element = Json.parseToJsonElement("""{"Uri":"http://x"}""")

        assertEquals("http://x", StreamReference.from(element)?.uri)
    }

    @Test
    fun `StreamReference rejects an unmarked Uri with extra fields`() {
        val element = Json.parseToJsonElement("""{"Uri":"http://x","X":1}""")

        assertNull(StreamReference.from(element))
    }

    @Test
    fun `StreamReference rejects a non-object element`() {
        assertNull(StreamReference.from(JsonPrimitive("http://x")))
    }

    @Test
    fun `StreamReference toJson produces the Uri wire form`() {
        val ref = StreamReference("http://x")

        val obj = ref.toJson()

        assertEquals("http://x", obj["Uri"]!!.jsonPrimitive.content)
        assertEquals(1, obj.size)
    }
}
