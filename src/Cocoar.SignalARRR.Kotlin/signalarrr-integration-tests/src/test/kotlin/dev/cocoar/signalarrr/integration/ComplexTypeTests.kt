package dev.cocoar.signalarrr.integration

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.io.ByteArrayInputStream
import java.util.UUID

/** Mirrors `TestShared.ComplexTestClass`; the server serialises PascalCase property names. */
@Serializable
data class ComplexTestClass(
    @SerialName("Name") val name: String,
    @SerialName("Age") val age: Int,
    @SerialName("Ok") val ok: Boolean,
    @SerialName("Timestamp") val timestamp: String,
    @SerialName("Properties") val properties: Map<String, JsonElement>,
)

class ComplexTypeTests : IntegrationTestBase() {

    @Test
    fun `DateTime argument serialises correctly`() = test {
        val result: String = connection.invoke("ExtraMethods.FormatDate", "2025-06-15T00:00:00Z")
        assertEquals("2025-06-15", result)
    }

    @Test
    fun `Guid argument passes correctly`() = test {
        val guid = UUID.randomUUID()
        val result: String = connection.invoke("ExtraMethods.GuidToString", guid)
        assertEquals(guid.toString(), result.lowercase())
    }

    @Test
    fun `List return decodes`() = test {
        val items: List<String> = connection.invoke("ExtraMethods.GenerateItems", 4)
        assertEquals(listOf("item-0", "item-1", "item-2", "item-3"), items)
    }

    @Test
    fun `Dictionary return decodes`() = test {
        val lengths: Map<String, Int> = connection.invoke("ExtraMethods.WordLengths", "hello world")
        assertEquals(5, lengths["hello"])
        assertEquals(5, lengths["world"])
    }

    @Test
    fun `mixed parameter types`() = test {
        val result: String = connection.invoke("ExtraMethods.Combine", "test", 42, true)
        assertEquals("test-42-True", result)
    }

    @Test
    fun `complex object round-trips`() = test {
        val input = ComplexTestClass(
            name = "Kotlin",
            age = 7,
            ok = true,
            timestamp = "2025-06-15T12:30:00Z",
            properties = mapOf("k" to JsonPrimitive("v"), "n" to JsonPrimitive(1)),
        )
        val result: ComplexTestClass = connection.invoke("ExtraMethods.EchoComplex", input)
        assertEquals(input.name, result.name)
        assertEquals(input.age, result.age)
        assertEquals(input.ok, result.ok)
        assertTrue(result.timestamp.startsWith("2025-06-15T12:30:00"), result.timestamp)
        assertEquals("v", result.properties["k"]?.jsonPrimitive?.content)
    }

    @Test
    fun `ByteArray argument is uploaded automatically`() = test {
        val content = "AutoUploadFromKotlin"
        val result: String = connection.invoke("ExtraMethods.ReadStreamContent", content.toByteArray())
        assertEquals(content, result)
    }

    @Test
    fun `InputStream argument is uploaded automatically`() = test {
        val content = "StreamedUploadFromKotlin"
        val result: String = connection.invoke("ExtraMethods.ReadStreamContent", ByteArrayInputStream(content.toByteArray()))
        assertEquals(content, result)
    }

    @Test
    fun `FromServices parameter is injected server-side`() = test {
        val result: String = connection.invoke("ExtraMethods.GetServiceInfo")
        assertEquals("ServiceProviderInjected", result)
    }
}
