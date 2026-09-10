package dev.cocoar.signalarrr.integration

import kotlinx.coroutines.flow.take
import kotlinx.coroutines.flow.toList
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.UUID

class ClientToServerTests : IntegrationTestBase() {

    @Test
    fun `invoke returns string`() = test {
        val name: String = connection.invoke("GetNameAsync")
        assertEquals("MyNameAsync", name)
    }

    @Test
    fun `invoke sync hub method`() = test {
        val name: String = connection.invoke("GetName")
        assertEquals("MyName", name)
    }

    @Test
    fun `invoke returns guid as string`() = test {
        val guid: String = connection.invoke("GetGuidAsync")
        UUID.fromString(guid)
    }

    @Test
    fun `send void method`() = test {
        connection.send("NothingAsync")
        connection.send("Nothing")
        // A send has no acknowledgement; the connection must still be usable afterwards.
        assertEquals("still here", connection.invoke<String>("Echo", "still here"))
    }

    @Test
    fun `echo round-trips unicode`() = test {
        val text = "Grüße aus Österreich — 日本語 🎉"
        assertEquals(text, connection.invoke<String>("Echo", text))
    }

    @Test
    fun `stream receives all items`() = test {
        val items = connection.stream<Int>("Counter", 5, 10).toList()
        assertEquals(listOf(0, 1, 2, 3, 4), items)
    }

    @Test
    fun `cancelling the collector stops the stream`() = test {
        val items = connection.stream<Int>("Counter", 100, 50).take(3).toList()
        assertEquals(listOf(0, 1, 2), items)
        // The connection stays healthy after the cancel message.
        assertEquals("ok", connection.invoke<String>("Echo", "ok"))
    }

    @Test
    fun `connection reports its id and state`() = test {
        val id = connectionId()
        assertTrue(id.isNotBlank())
        assertEquals(id, connection.connectionId)
    }
}
