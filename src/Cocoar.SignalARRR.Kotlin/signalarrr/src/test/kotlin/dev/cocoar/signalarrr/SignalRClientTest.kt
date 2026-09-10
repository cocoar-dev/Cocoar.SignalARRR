package dev.cocoar.signalarrr

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeout
import kotlinx.serialization.json.JsonPrimitive
import mockwebserver3.Dispatcher
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import mockwebserver3.RecordedRequest
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import kotlin.time.Duration.Companion.seconds

class SignalRClientTest {

    /** The SignalR JSON-protocol record separator: every text frame on the wire ends with one. */
    private val recordSeparator: Char = 0x1e.toChar()

    private fun wire(text: String): String = text + recordSeparator

    private val negotiateWebSocketsOnly = """
        {"connectionId":"c1","connectionToken":"t1","negotiateVersion":1,"availableTransports":[{"transport":"WebSockets","transferFormats":["Text","Binary"]}]}
    """.trimIndent()

    private val negotiateLongPollingOnly = """
        {"connectionId":"c1","connectionToken":"t1","negotiateVersion":1,"availableTransports":[{"transport":"LongPolling","transferFormats":["Text"]}]}
    """.trimIndent()

    private fun newClient(server: MockWebServer, configure: SignalRClientOptions.() -> Unit = {}): SignalRClient =
        SignalRClient(server.url("/hub").toString()) {
            reconnectPolicy = ReconnectPolicy.Disabled
            handshakeTimeout = 5.seconds
            logger = NoopLogger
            configure()
        }

    // An HTTP-only dispatcher: POST .../negotiate returns [negotiateBody]; everything else 404.
    private fun negotiateOnlyDispatcher(negotiateBody: String, code: Int = 200): Dispatcher =
        object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse =
                if (request.method == "POST" && request.url.encodedPath.endsWith("/negotiate")) {
                    MockResponse.Builder().code(code).body(negotiateBody).build()
                } else {
                    MockResponse.Builder().code(404).build()
                }
        }

    // Negotiate (always succeeds with WebSockets) + a WebSocket upgrade at /hub driven by [listener].
    private fun webSocketDispatcher(listener: WebSocketListener): Dispatcher =
        object : Dispatcher() {
            override fun dispatch(request: RecordedRequest): MockResponse {
                val path = request.url.encodedPath
                return when {
                    request.method == "POST" && path.endsWith("/negotiate") ->
                        MockResponse.Builder().code(200).body(negotiateWebSocketsOnly).build()
                    path == "/hub" -> MockResponse.Builder().webSocketUpgrade(listener).build()
                    else -> MockResponse.Builder().code(404).build()
                }
            }
        }

    // Acks the SignalR text handshake and forwards every other message to [onAppMessage].
    private inner class HandshakeAckingListener(
        private val onAppMessage: (text: String, socket: WebSocket) -> Unit,
    ) : WebSocketListener() {
        @Volatile
        lateinit var socket: WebSocket

        override fun onOpen(webSocket: WebSocket, response: Response) {
            socket = webSocket
        }

        override fun onMessage(webSocket: WebSocket, text: String) {
            if (text.contains("\"protocol\"")) {
                webSocket.send(wire("{}"))
            } else {
                onAppMessage(text, webSocket)
            }
        }
    }

    // --- negotiate failure ---

    @Test
    fun `start throws NegotiationFailedException when negotiate fails and state is DISCONNECTED`() = runBlocking {
        withTimeout(10_000) {
            val server = MockWebServer()
            server.start()
            try {
                server.dispatcher = negotiateOnlyDispatcher(negotiateBody = "", code = 500)
                val client = newClient(server)

                var thrown: Throwable? = null
                try {
                    client.start()
                } catch (e: Throwable) {
                    thrown = e
                }

                assertTrue(thrown is NegotiationFailedException, "expected NegotiationFailedException, got $thrown")
                assertEquals(HubConnectionState.DISCONNECTED, client.state)
            } finally {
                runCatching { server.close() }
            }
        }
    }

    // --- transport mismatch ---

    @Test
    fun `start throws ConnectionFailedException when server offers no allowed transport`() = runBlocking {
        withTimeout(10_000) {
            val server = MockWebServer()
            server.start()
            try {
                server.dispatcher = negotiateOnlyDispatcher(negotiateLongPollingOnly)
                val client = newClient(server) {
                    allowedTransports = listOf(TransportType.WEB_SOCKETS)
                }

                var thrown: Throwable? = null
                try {
                    client.start()
                } catch (e: Throwable) {
                    thrown = e
                }

                assertTrue(thrown is ConnectionFailedException, "expected ConnectionFailedException, got $thrown")
                assertTrue(
                    thrown!!.message!!.contains("transport", ignoreCase = true),
                    "message should mention transports: ${thrown.message}",
                )
                assertEquals(HubConnectionState.DISCONNECTED, client.state)
            } finally {
                runCatching { server.close() }
            }
        }
    }

    // --- happy path over WebSockets ---

    @Test
    fun `connects over WebSockets, dispatches invocations, invokes, and stops`() = runBlocking {
        withTimeout(10_000) {
            val server = MockWebServer()
            server.start()
            try {
                val helloCompletion = CompletableDeferred<String>()
                var mCallCount = 0
                val invocationIdRegex = Regex("\"invocationId\":\"(.*?)\"")

                val listener = HandshakeAckingListener { text, socket ->
                    when {
                        text.contains("\"invocationId\":\"7\"") && text.contains("\"result\"") ->
                            helloCompletion.complete(text)
                        text.contains("\"target\":\"M\"") -> {
                            val id = invocationIdRegex.find(text)!!.groupValues[1]
                            mCallCount++
                            val reply = if (mCallCount == 1) {
                                wire("{\"type\":3,\"invocationId\":\"$id\",\"result\":42}")
                            } else {
                                wire("{\"type\":3,\"invocationId\":\"$id\",\"error\":\"boom\"}")
                            }
                            socket.send(reply)
                        }
                        else -> Unit
                    }
                }
                server.dispatcher = webSocketDispatcher(listener)

                val closedError = CompletableDeferred<Throwable?>()
                val client = newClient(server)
                client.onClosed { error -> closedError.complete(error) }

                client.start()
                assertEquals(HubConnectionState.CONNECTED, client.state)

                // Server -> client invocation dispatched to a registered handler.
                client.on("Hello") { args ->
                    val arg = (args[0] as JsonPrimitive).content
                    JsonPrimitive("hi $arg")
                }
                listener.socket.send(wire("{\"type\":1,\"invocationId\":\"7\",\"target\":\"Hello\",\"arguments\":[\"x\"]}"))
                val completionText = withTimeout(10_000) { helloCompletion.await() }
                assertTrue(completionText.contains("\"invocationId\":\"7\""))
                assertTrue(completionText.contains("\"result\":\"hi x\""))

                // Client -> server invoke, successful result.
                val result = client.invoke("M", emptyList())
                assertEquals(JsonPrimitive(42), result)

                // Client -> server invoke, error result.
                var invokeError: Throwable? = null
                try {
                    client.invoke("M", emptyList())
                } catch (e: Throwable) {
                    invokeError = e
                }
                assertTrue(invokeError is HubInvocationException, "expected HubInvocationException, got $invokeError")
                assertEquals("boom", invokeError!!.message)

                // Stop.
                client.stop()
                assertEquals(HubConnectionState.DISCONNECTED, client.state)
                assertNull(withTimeout(10_000) { closedError.await() })
            } finally {
                runCatching { server.close() }
            }
        }
    }
}
