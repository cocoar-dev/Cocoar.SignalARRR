package dev.cocoar.signalarrr.integration

import dev.cocoar.signalarrr.ConsoleLogger
import dev.cocoar.signalarrr.HARRRConnection
import dev.cocoar.signalarrr.HubConnectionState
import dev.cocoar.signalarrr.HubProtocolKind
import dev.cocoar.signalarrr.LogLevel
import dev.cocoar.signalarrr.NegotiationFailedException
import dev.cocoar.signalarrr.ReconnectPolicy
import dev.cocoar.signalarrr.TransportCredential
import dev.cocoar.signalarrr.TransportType
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.flow.toList
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Assumptions.assumeTrue
import org.junit.jupiter.api.BeforeEach
import org.junit.jupiter.api.Test
import org.junit.jupiter.params.ParameterizedTest
import org.junit.jupiter.params.provider.EnumSource

/** Every transport carries the full protocol: invoke, stream, server-to-client. */
class TransportTests {

    @BeforeEach
    fun requireServer() {
        assumeTrue(IntegrationTestBase.serverUrl != null, "SIGNALARRR_TEST_SERVER_URL not set — skipping integration tests")
    }

    private fun connect(transport: TransportType, protocol: HubProtocolKind = HubProtocolKind.JSON): HARRRConnection =
        HARRRConnection.create("${IntegrationTestBase.serverUrl}${IntegrationTestBase.HUB_PATH}") {
            allowedTransports = listOf(transport)
            hubProtocol = protocol
            logger = ConsoleLogger(LogLevel.WARNING)
        }

    @ParameterizedTest
    @EnumSource(TransportType::class)
    fun `invoke and stream work over each transport`(transport: TransportType) = runBlocking {
        withTimeout(30_000) {
            val connection = connect(transport)
            try {
                connection.start()
                assertEquals(transport, connection.client.transportType)
                assertEquals("via ${transport.wireName}", connection.invoke<String>("Echo", "via ${transport.wireName}"))
                assertEquals(listOf(0, 1, 2), connection.stream<Int>("Counter", 3, 0).toList())
            } finally {
                connection.stop()
            }
        }
    }

    private suspend fun transportCredential(transport: TransportType, credential: TransportCredential? = null): String {
        val connection = HARRRConnection.create("${IntegrationTestBase.serverUrl}${IntegrationTestBase.HUB_PATH}") {
            allowedTransports = listOf(transport)
            logger = ConsoleLogger(LogLevel.WARNING)
            accessTokenProvider = { "probe-token" }
            if (credential != null) transportCredential = credential
        }
        try {
            connection.start()
            return connection.invoke("TransportCredential")
        } finally {
            connection.stop()
        }
    }

    @ParameterizedTest
    @EnumSource(TransportType::class)
    fun `connection token travels as header by default`(transport: TransportType) = runBlocking {
        withTimeout(30_000) {
            assertEquals("header=Bearer probe-token;query=-", transportCredential(transport))
        }
    }

    @Test
    fun `a rejected negotiate reports its HTTP status`() = runBlocking {
        withTimeout(30_000) {
            val connection = HARRRConnection.create("${IntegrationTestBase.serverUrl}/signalr/no-such-hub") {
                logger = ConsoleLogger(LogLevel.WARNING)
                reconnectPolicy = ReconnectPolicy.Disabled
            }
            val e = runCatching { connection.start() }.exceptionOrNull()
            assertTrue(e is NegotiationFailedException, "expected NegotiationFailedException, got $e")
            assertEquals(404, (e as NegotiationFailedException).statusCode)
        }
    }

    @Test
    fun `credential authenticates the connection`() = runBlocking {
        withTimeout(30_000) {
            val connection = HARRRConnection.create("${IntegrationTestBase.serverUrl}${IntegrationTestBase.HUB_PATH}") {
                logger = ConsoleLogger(LogLevel.WARNING)
                credential = { "probe-token" }
            }
            try {
                connection.start()
                assertEquals("header=Bearer probe-token;query=-", connection.invoke<String>("TransportCredential"))
            } finally {
                connection.stop()
            }
        }
    }

    @ParameterizedTest
    @EnumSource(TransportType::class)
    fun `connection token travels in the url when asked to`(transport: TransportType) = runBlocking {
        withTimeout(30_000) {
            assertEquals("header=-;query=probe-token", transportCredential(transport, TransportCredential.QUERY))
        }
    }

    @ParameterizedTest
    @EnumSource(TransportType::class)
    fun `server to client works over each transport`(transport: TransportType) = runBlocking {
        withTimeout(30_000) {
            val connection = connect(transport)
            try {
                connection.start()
                connection.onServerMethod("TestShared.ITestClientMethods|GetById") { args -> transport.wireName + ":" + args.value<String>(0) }
                val id: String = connection.invoke("GetConnectionId")
                val (status, body) = TestServer.trigger("/__test/trigger-client-getbyid", "connectionId" to id, "id" to "t")
                assertEquals(200, status, body)
                assertTrue(body.contains("${transport.wireName}:t"), body)
            } finally {
                connection.stop()
            }
        }
    }

    @Test
    fun `MessagePack over long polling`() = runBlocking {
        withTimeout(30_000) {
            val connection = connect(TransportType.LONG_POLLING, HubProtocolKind.MESSAGE_PACK)
            try {
                connection.start()
                assertEquals(9, connection.invoke<Int>("ExtraMethods.Add", 4, 5))
            } finally {
                connection.stop()
            }
        }
    }

    @Test
    fun `stop fires onClosed without error and leaves DISCONNECTED`() = runBlocking {
        withTimeout(30_000) {
            val connection = connect(TransportType.WEB_SOCKETS)
            val closed = CompletableDeferred<Throwable?>()
            connection.onClosed { closed.complete(it) }
            connection.start()
            assertEquals(HubConnectionState.CONNECTED, connection.state)
            connection.stop()
            assertEquals(HubConnectionState.DISCONNECTED, connection.state)
            assertNull(withTimeout(5_000) { closed.await() })
        }
    }

    @Test
    fun `a connection can be restarted after stop`() = runBlocking {
        withTimeout(30_000) {
            val connection = connect(TransportType.WEB_SOCKETS)
            connection.start()
            connection.stop()
            connection.start()
            assertEquals("again", connection.invoke<String>("Echo", "again"))
            connection.stop()
        }
    }

    @Test
    fun `reconnect policy disabled reports closed on server-side close`() = runBlocking {
        // Sanity check of the policy plumbing; a forced server-side drop is exercised by the .NET suite.
        val connection = HARRRConnection.create("${IntegrationTestBase.serverUrl}${IntegrationTestBase.HUB_PATH}") {
            reconnectPolicy = ReconnectPolicy.Disabled
            logger = ConsoleLogger(LogLevel.WARNING)
        }
        withTimeout(30_000) {
            connection.start()
            assertEquals("x", connection.invoke<String>("Echo", "x"))
            connection.stop()
        }
    }
}
