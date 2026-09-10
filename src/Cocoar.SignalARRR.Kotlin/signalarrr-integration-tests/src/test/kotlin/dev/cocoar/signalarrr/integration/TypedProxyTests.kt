package dev.cocoar.signalarrr.integration

import dev.cocoar.signalarrr.integration.contracts.ExtraMethodsProxy
import dev.cocoar.signalarrr.integration.contracts.ITestServerMethodsProxy
import dev.cocoar.signalarrr.integration.contracts.TestHubMethodsProxy
import kotlinx.coroutines.flow.toList
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.UUID

/** The KSP-generated proxies against the live server — every [dev.cocoar.signalarrr.ProxyKind]. */
class TypedProxyTests : IntegrationTestBase() {

    @Test
    fun `contract interface proxy - INTERFACE kind`() = test {
        val server = connection.getTypedMethods(ITestServerMethodsProxy)
        assertEquals("MyName", server.getName())
        assertEquals("MyNameAsync", server.getNameAsync())
        UUID.fromString(server.getGuid())
        UUID.fromString(server.getGuidAsync())
        server.nothing()
        server.nothingAsync()
        assertEquals("Cocoar.SignalARRR.Tests.SharedModels.ITestServerMethods", ITestServerMethodsProxy.PREFIX)
    }

    @Test
    fun `hub methods proxy - HUB kind`() = test {
        val hub = TestHubMethodsProxy(connection)
        assertEquals("ping", hub.echo("ping"))
        assertEquals("renamed", hub.echoRenamed("renamed"))
        assertTrue(hub.getConnectionId().isNotBlank())
        assertEquals(listOf(0, 1, 2), hub.counter(3, 0).toList())
        assertEquals("", TestHubMethodsProxy.PREFIX)
    }

    @Test
    fun `ServerMethods class proxy - SERVER_METHODS kind`() = test {
        val extra = connection.getTypedMethods(ExtraMethodsProxy)
        assertEquals("Hello, Proxy!", extra.greet("Proxy"))
        assertEquals(5, extra.add(2, 3))
        assertEquals("custom-name", extra.echoWithCustomName("custom-name"))
        assertEquals(listOf("item-0", "item-1"), extra.generateItems(2))
        assertEquals(mapOf("ab" to 2, "cde" to 3), extra.wordLengths("ab cde"))
        assertEquals("p-1-False", extra.combine("p", 1, false))
        assertEquals("uploaded", extra.readStreamContent("uploaded".toByteArray()))
    }
}
