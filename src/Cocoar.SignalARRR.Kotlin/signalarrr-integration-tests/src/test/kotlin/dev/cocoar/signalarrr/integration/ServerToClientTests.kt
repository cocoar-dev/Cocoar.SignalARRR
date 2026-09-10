package dev.cocoar.signalarrr.integration

import dev.cocoar.signalarrr.ServerCancelledException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.withTimeout
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import java.util.UUID

class ServerToClientTests : IntegrationTestBase() {

    private val contract = "TestShared.ITestClientMethods"
    private val pushContract = "Cocoar.SignalARRR.Tests.SharedModels.ITestServerPushClient"

    @Test
    fun `server calls void method`() = test {
        val called = CompletableDeferred<Unit>()
        connection.onServerMethod("$contract|Nix") { called.complete(Unit) }

        val (status, _) = trigger("/__test/trigger-client-typed-call", "connectionId" to connectionId())
        assertEquals(200, status)
        withTimeout(5_000) { called.await() }
    }

    @Test
    fun `server awaits a string result`() = test {
        connection.onServerMethod("$contract|GetById") { args -> "got:" + args.value<String>(0) }

        val (status, body) = trigger("/__test/trigger-client-getbyid", "connectionId" to connectionId(), "id" to "test-42")
        assertEquals(200, status, body)
        assertTrue(body.contains("got:test-42"), body)
    }

    @Test
    fun `server awaits a list result`() = test {
        connection.onServerMethod("$contract|GetContent") { args ->
            List(args.value<Int>(0)) { "item-$it" }
        }

        val (status, body) = trigger("/__test/trigger-client-getcontent", "connectionId" to connectionId(), "count" to "3")
        assertEquals(200, status, body)
        assertTrue(body.contains("item-0") && body.contains("item-2"), body)
    }

    @Test
    fun `server passes a guid argument`() = test {
        val guid = UUID.randomUUID().toString()
        connection.onServerMethod("$contract|GetByGenericId") { args -> "id=" + args.value<String>(0) }

        val (status, body) = trigger("/__test/trigger-client-getbygenericid", "connectionId" to connectionId(), "id" to guid)
        assertEquals(200, status, body)
        assertTrue(body.contains(guid), body)
    }

    @Test
    fun `server consumes a stream from the client`() = test {
        connection.onServerStreamMethod("$contract|StreamNumbers") { args ->
            val count = args.value<Int>(0)
            flow {
                repeat(count) {
                    delay(10)
                    emit(it)
                }
            }
        }

        val (status, body) = trigger("/__test/trigger-client-stream", "connectionId" to connectionId(), "count" to "4")
        assertEquals(200, status, body)
        assertTrue(body.contains("0") && body.contains("3"), body)
    }

    @Test
    fun `server cancels a running handler`() = test {
        val observedCancellation = CompletableDeferred<Throwable>()
        val receivedToken = CompletableDeferred<Boolean>()
        val receivedSeconds = CompletableDeferred<Int>()

        connection.onServerMethod("$contract|Wait") { args ->
            receivedSeconds.complete(args.value<Int>(0))
            receivedToken.complete(args.cancellationToken(1) != null)
            try {
                delay(args.value<Int>(0) * 1000L)
                "done"
            } catch (e: Throwable) {
                observedCancellation.complete(e)
                throw e
            }
        }

        val started = System.nanoTime()
        val (status, body) = trigger("/__test/trigger-client-cancellation", "connectionId" to connectionId(), "delayMs" to "200")
        val elapsedMs = (System.nanoTime() - started) / 1_000_000
        assertEquals(200, status, body)

        assertEquals(30, withTimeout(5_000) { receivedSeconds.await() })
        assertTrue(withTimeout(5_000) { receivedToken.await() }, "the token slot must carry a ServerCancellationToken")
        val cause = withTimeout(5_000) { observedCancellation.await() }
        assertTrue(cause is ServerCancelledException, "expected ServerCancelledException, got $cause")
        assertTrue(elapsedMs < 5_000, "cancellation took ${elapsedMs}ms — expected well under the 30s wait")
    }

    @Test
    fun `server sends fire-and-forget push`() = test {
        val received = CompletableDeferred<String>()
        connection.onServerMethod("$pushContract|PushNotification") { args -> received.complete(args.value<String>(0)) }

        val (status, _) = trigger("/__test/push-notification", "connectionId" to connectionId(), "message" to "hello kotlin")
        assertEquals(200, status)
        assertEquals("hello kotlin", withTimeout(5_000) { received.await() })
    }

    @Test
    fun `server sends a null first argument`() = test {
        val received = CompletableDeferred<Pair<String?, String>>()
        connection.onServerMethod("$pushContract|ConfigUpdated") { args ->
            received.complete(args.value<String?>(0) to args.value<String>(1))
        }

        val (status, _) = trigger("/__test/config-updated", "connectionId" to connectionId(), "configJson" to "{\"a\":1}")
        assertEquals(200, status)
        val (path, json) = withTimeout(5_000) { received.await() }
        assertEquals(null, path)
        assertEquals("{\"a\":1}", json)
    }

    @Test
    fun `server requests client info and awaits it`() = test {
        connection.onServerMethod("$pushContract|RequestClientInfo") { "kotlin-client" }

        val (status, body) = trigger("/__test/request-client-info", "connectionId" to connectionId())
        assertEquals(200, status, body)
        assertTrue(body.contains("kotlin-client"), body)
    }

    @Test
    fun `handler returning bytes is uploaded as a stream`() = test {
        connection.onServerMethod("$contract|GetFileStream") { args ->
            ("from-kotlin:" + args.value<String>(0)).toByteArray()
        }

        val (status, body) = trigger("/__test/trigger-client-getfilestream", "connectionId" to connectionId(), "content" to "payload")
        assertEquals(200, status, body)
        assertTrue(body.contains("from-kotlin:payload"), body)
    }

    @Test
    fun `interface registration maps every handler`() = test {
        val received = CompletableDeferred<String>()
        connection.registerHandlers(
            pushContract,
            mapOf(
                "PushNotification" to { args -> received.complete(args.value<String>(0)) },
                "RequestClientInfo" to { "registered" },
            ),
        )

        val (status, body) = trigger("/__test/request-client-info", "connectionId" to connectionId())
        assertEquals(200, status, body)
        assertTrue(body.contains("registered"), body)

        trigger("/__test/push-notification", "connectionId" to connectionId(), "message" to "via-map")
        assertEquals("via-map", withTimeout(5_000) { received.await() })
    }

    @Test
    fun `server request envelope is observable`() = test {
        val seen = CompletableDeferred<String>()
        connection.onServerRequestMessageReceived = { seen.complete(it.method) }
        connection.onServerMethod("$contract|Nix") { }

        trigger("/__test/trigger-client-typed-call", "connectionId" to connectionId())
        assertEquals("$contract|Nix", withTimeout(5_000) { seen.await() })
        assertNotNull(connection.connectionId)
    }
}
