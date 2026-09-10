package dev.cocoar.signalarrr.integration

import dev.cocoar.signalarrr.HARRRConnectionOptions
import dev.cocoar.signalarrr.HubProtocolKind
import kotlinx.coroutines.flow.toList
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.UUID

/** The full SignalARRR surface over the MessagePack hub protocol. */
class MessagePackTests : IntegrationTestBase() {

    override fun configure(options: HARRRConnectionOptions) {
        options.hubProtocol = HubProtocolKind.MESSAGE_PACK
    }

    @Test
    fun `invoke returns string`() = test {
        assertEquals("MyNameAsync", connection.invoke<String>("GetNameAsync"))
    }

    @Test
    fun `invoke returns guid`() = test {
        UUID.fromString(connection.invoke<String>("GetGuidAsync"))
    }

    @Test
    fun `send void method`() = test {
        connection.send("NothingAsync")
        assertEquals("ok", connection.invoke<String>("Echo", "ok"))
    }

    @Test
    fun `mixed parameter types`() = test {
        assertEquals("mp-7-True", connection.invoke<String>("ExtraMethods.Combine", "mp", 7, true))
        assertEquals(12, connection.invoke<Int>("ExtraMethods.Add", 5, 7))
    }

    @Test
    fun `stream over MessagePack`() = test {
        assertEquals(listOf(0, 1, 2, 3, 4), connection.stream<Int>("Counter", 5, 0).toList())
    }

    @Test
    fun `complex return decodes`() = test {
        val lengths: Map<String, Int> = connection.invoke("ExtraMethods.WordLengths", "a bb ccc")
        assertEquals(mapOf("a" to 1, "bb" to 2, "ccc" to 3), lengths)
    }

    @Test
    fun `server to client over MessagePack`() = test {
        connection.onServerMethod("TestShared.ITestClientMethods|GetById") { args -> "mp:" + args.value<String>(0) }
        val (status, body) = trigger("/__test/trigger-client-getbyid", "connectionId" to connectionId(), "id" to "x1")
        assertEquals(200, status, body)
        assertTrue(body.contains("mp:x1"), body)
    }

    @Test
    fun `ByteArray upload over MessagePack`() = test {
        assertEquals("bytes", connection.invoke<String>("ExtraMethods.ReadStreamContent", "bytes".toByteArray()))
    }
}
