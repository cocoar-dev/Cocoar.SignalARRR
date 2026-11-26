package dev.cocoar.signalarrr.integration

import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonPrimitive
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

/**
 * Generic client-to-server methods: Kotlin names the type argument as a string (what
 * `@GenericArguments` emits), the server accepts the types it declared with `[GenericArguments]`.
 */
class GenericServerMethodsTests : IntegrationTestBase() {

    @Test
    fun `full type name selects the instantiation`() = test {
        val result = connection.invokeCore("GenericMethods.Echo", listOf(42), genericArguments = listOf("System.Int32"))
        assertEquals(42, result!!.jsonPrimitive.int)
    }

    @Test
    fun `short system type name is accepted`() = test {
        val result = connection.invokeCore("GenericMethods.Echo", listOf("kotlin"), genericArguments = listOf("String"))
        assertEquals("kotlin", result!!.jsonPrimitive.content)
    }

    @Test
    fun `several type arguments resolve in order`() = test {
        val result = connection.invokeCore("GenericMethods.Pair", listOf(1, "a"), genericArguments = listOf("System.Int32", "System.String"))
        assertEquals("Int32:1|String:a", result!!.jsonPrimitive.content)
    }
}
