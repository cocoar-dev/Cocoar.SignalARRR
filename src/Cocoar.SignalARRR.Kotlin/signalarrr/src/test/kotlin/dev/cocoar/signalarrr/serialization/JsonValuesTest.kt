package dev.cocoar.signalarrr.serialization

import dev.cocoar.signalarrr.SerializationFailedException
import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertThrows
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.time.Instant
import java.util.Base64
import java.util.UUID

class JsonValuesTest {

    @Serializable
    private enum class Color { RED, GREEN }

    private enum class PlainColor { BLUE, YELLOW }

    @Serializable
    private data class Point(@SerialName("Name") val name: String, val x: Int)

    private class Foo(val value: Int)

    @Serializable
    private data class MyData(val a: Int, val b: String)

    // --- encode ---

    @Test
    fun `encode null`() {
        assertEquals(JsonNull, JsonValues.encode(null))
    }

    @Test
    fun `encode String`() {
        assertEquals(JsonPrimitive("hi"), JsonValues.encode("hi"))
    }

    @Test
    fun `encode Int`() {
        assertEquals(5, JsonValues.encode(5).jsonPrimitive.content.toInt())
    }

    @Test
    fun `encode Long`() {
        assertEquals(5L, JsonValues.encode(5L).jsonPrimitive.content.toLong())
    }

    @Test
    fun `encode Double`() {
        assertEquals(1.5, JsonValues.encode(1.5).jsonPrimitive.content.toDouble())
    }

    @Test
    fun `encode Boolean`() {
        assertTrue(JsonValues.encode(true).jsonPrimitive.boolean)
    }

    @Test
    fun `encode Serializable enum uses its serializer`() {
        assertEquals(JsonPrimitive("RED"), JsonValues.encode(Color.RED))
    }

    @Test
    fun `encode plain enum falls back to its name`() {
        assertEquals(JsonPrimitive("BLUE"), JsonValues.encode(PlainColor.BLUE))
    }

    @Test
    fun `encode UUID as string`() {
        val uuid = UUID.randomUUID()
        assertEquals(JsonPrimitive(uuid.toString()), JsonValues.encode(uuid))
    }

    @Test
    fun `encode Instant as ISO string`() {
        val instant = Instant.parse("2024-01-02T03:04:05Z")
        assertEquals(JsonPrimitive(instant.toString()), JsonValues.encode(instant))
    }

    @Test
    fun `encode ByteArray as base64`() {
        val bytes = byteArrayOf(1, 2, 3)
        assertEquals(JsonPrimitive(Base64.getEncoder().encodeToString(bytes)), JsonValues.encode(bytes))
    }

    @Test
    fun `encode List as JsonArray`() {
        val result = JsonValues.encode(listOf(1, 2, 3))
        assertEquals(JsonArray(listOf(JsonPrimitive(1), JsonPrimitive(2), JsonPrimitive(3))), result)
    }

    @Test
    fun `encode Set as JsonArray`() {
        val result = JsonValues.encode(setOf(1)) as JsonArray
        assertEquals(listOf(JsonPrimitive(1)), result.toList())
    }

    @Test
    fun `encode Array as JsonArray`() {
        val result = JsonValues.encode(arrayOf("a", "b"))
        assertEquals(JsonArray(listOf(JsonPrimitive("a"), JsonPrimitive("b"))), result)
    }

    @Test
    fun `encode Map as JsonObject with string keys`() {
        val result = JsonValues.encode(mapOf(1 to "a")).jsonObject
        assertEquals("a", result["1"]!!.jsonPrimitive.content)
    }

    @Test
    fun `encode nested list of Serializable data classes`() {
        val result = JsonValues.encode(listOf(Point("p1", 1), Point("p2", 2))).jsonArray
        assertEquals(2, result.size)
        assertEquals("p1", result[0].jsonObject["Name"]!!.jsonPrimitive.content)
        assertEquals(2, result[1].jsonObject["x"]!!.jsonPrimitive.content.toInt())
    }

    @Test
    fun `encode Serializable data class honors SerialName`() {
        val result = JsonValues.encode(Point("p1", 1)).jsonObject
        assertTrue(result.containsKey("Name"))
        assertEquals("p1", result["Name"]!!.jsonPrimitive.content)
    }

    @Test
    fun `encode JsonElement passes through unchanged`() {
        val element: JsonElement = JsonPrimitive("already json")
        assertEquals(element, JsonValues.encode(element))
    }

    @Test
    fun `encode non-serializable class throws SerializationFailedException`() {
        assertThrows(SerializationFailedException::class.java) {
            JsonValues.encode(Foo(1))
        }
    }

    // --- decode ---

    @Test
    fun `decode Int`() {
        assertEquals(5, JsonValues.decode<Int>(JsonPrimitive(5)))
    }

    @Test
    fun `decode nullable String from null element`() {
        assertNull(JsonValues.decode<String?>(null))
    }

    @Test
    fun `decode List of String`() {
        val element = JsonArray(listOf(JsonPrimitive("a"), JsonPrimitive("b")))
        assertEquals(listOf("a", "b"), JsonValues.decode<List<String>>(element))
    }

    @Test
    fun `decode Map of String to Int`() {
        val element = JsonObject(mapOf("a" to JsonPrimitive(1)))
        assertEquals(mapOf("a" to 1), JsonValues.decode<Map<String, Int>>(element))
    }

    @Test
    fun `decode Serializable data class`() {
        val element = JsonObject(mapOf("a" to JsonPrimitive(1), "b" to JsonPrimitive("x")))
        val decoded = JsonValues.decode<MyData>(element)
        assertEquals(MyData(1, "x"), decoded)
    }

    @Test
    fun `decode with wrong shape throws SerializationFailedException`() {
        assertThrows(SerializationFailedException::class.java) {
            JsonValues.decode<MyData>(JsonPrimitive("not an object"))
        }
    }
}
