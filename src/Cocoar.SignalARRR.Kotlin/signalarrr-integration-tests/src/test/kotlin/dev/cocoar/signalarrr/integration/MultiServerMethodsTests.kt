package dev.cocoar.signalarrr.integration

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Test

/** A second `ServerMethods<TestHub>` class on the same hub, addressed as `ClassName.Method`. */
class MultiServerMethodsTests : IntegrationTestBase() {

    @Test
    fun `second ServerMethods class - Greet`() = test {
        assertEquals("Hello, Kotlin!", connection.invoke<String>("ExtraMethods.Greet", "Kotlin"))
    }

    @Test
    fun `second ServerMethods class - Add`() = test {
        assertEquals(7, connection.invoke<Int>("ExtraMethods.Add", 3, 4))
    }

    @Test
    fun `MessageName attribute renames the method`() = test {
        assertEquals("custom", connection.invoke<String>("ExtraMethods.CustomEcho", "custom"))
    }

    @Test
    fun `hub methods still work alongside`() = test {
        assertEquals("MyName", connection.invoke<String>("GetName"))
    }

    @Test
    fun `overloads resolve by argument count`() = test {
        assertEquals("one", connection.invoke<String>("ExtraMethods.PickOverload", 1))
        assertEquals("two", connection.invoke<String>("ExtraMethods.PickOverload", 1, 2))
    }

    @Test
    fun `trailing default parameter is filled by the server`() = test {
        assertEquals("3:25", connection.invoke<String>("ExtraMethods.PageInfo", 3))
        assertEquals("3:10", connection.invoke<String>("ExtraMethods.PageInfo", 3, 10))
    }
}
