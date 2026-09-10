package dev.cocoar.signalarrr.transport

import dev.cocoar.signalarrr.ConnectionFailedException
import dev.cocoar.signalarrr.DisconnectedException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.ClosedReceiveChannelException
import okhttp3.HttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import okio.ByteString
import okio.ByteString.Companion.toByteString

/** WebSocket transport on OkHttp. Text frames for JSON, binary frames for MessagePack. */
internal class WebSocketTransport(
    private val httpClient: OkHttpClient,
    private val binary: Boolean,
    private val headers: Map<String, String>,
) : SignalRTransport {

    private val incoming = Channel<ByteArray>(Channel.UNLIMITED)
    private val opened = CompletableDeferred<Unit>()

    @Volatile
    private var webSocket: WebSocket? = null

    override suspend fun connect(url: HttpUrl) {
        val request = Request.Builder().url(url).headers(headers).build()
        webSocket = httpClient.newWebSocket(request, Listener())
        opened.await()
    }

    override suspend fun send(data: ByteArray) {
        val ws = webSocket ?: throw DisconnectedException()
        val queued = if (binary) ws.send(data.toByteString()) else ws.send(String(data, Charsets.UTF_8))
        if (!queued) throw DisconnectedException("WebSocket is closed")
    }

    override suspend fun receive(): ByteArray {
        try {
            return incoming.receive()
        } catch (e: ClosedReceiveChannelException) {
            throw DisconnectedException("WebSocket closed", e)
        }
    }

    override suspend fun close() {
        webSocket?.close(NORMAL_CLOSURE, null)
        webSocket = null
        incoming.close()
    }

    private inner class Listener : WebSocketListener() {
        override fun onOpen(webSocket: WebSocket, response: Response) {
            opened.complete(Unit)
        }

        override fun onMessage(webSocket: WebSocket, text: String) {
            incoming.trySend(text.toByteArray(Charsets.UTF_8))
        }

        override fun onMessage(webSocket: WebSocket, bytes: ByteString) {
            incoming.trySend(bytes.toByteArray())
        }

        override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
            webSocket.close(NORMAL_CLOSURE, null)
        }

        override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
            incoming.close(
                if (code == NORMAL_CLOSURE) null else DisconnectedException("WebSocket closed ($code): $reason"),
            )
        }

        override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
            val error = ConnectionFailedException(
                response?.let { "WebSocket HTTP ${it.code}: ${t.message}" } ?: (t.message ?: t.toString()),
                t,
            )
            if (!opened.isCompleted) opened.completeExceptionally(error)
            incoming.close(error)
        }
    }

    private companion object {
        const val NORMAL_CLOSURE = 1000
    }
}
