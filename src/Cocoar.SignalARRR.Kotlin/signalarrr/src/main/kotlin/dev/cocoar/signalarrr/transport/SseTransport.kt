package dev.cocoar.signalarrr.transport

import dev.cocoar.signalarrr.ConnectionFailedException
import dev.cocoar.signalarrr.DisconnectedException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Call
import okhttp3.HttpUrl
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okio.BufferedSource
import java.io.IOException
import java.util.concurrent.TimeUnit

/**
 * Server-Sent Events: server→client over a streaming GET (`text/event-stream`), client→server
 * over HTTP POST. Text protocols only — SignalR does not carry binary over SSE.
 */
internal class SseTransport(httpClient: OkHttpClient, private val headers: Map<String, String>) : SignalRTransport {

    /** The event stream stays open indefinitely, so it must not have a read timeout. */
    private val streamClient = httpClient.newBuilder().readTimeout(0, TimeUnit.MILLISECONDS).build()
    private val postClient = httpClient

    @Volatile
    private var url: HttpUrl? = null

    @Volatile
    private var call: Call? = null

    @Volatile
    private var response: Response? = null

    @Volatile
    private var source: BufferedSource? = null

    override suspend fun connect(url: HttpUrl) {
        this.url = url
        val request = Request.Builder().url(url).headers(headers).header("Accept", "text/event-stream").get().build()
        val call = streamClient.newCall(request)
        this.call = call
        val response = withContext(Dispatchers.IO) { call.execute() }
        if (response.code != 200) {
            response.close()
            throw ConnectionFailedException("SSE: HTTP ${response.code}")
        }
        this.response = response
        this.source = response.body.source()
    }

    override suspend fun send(data: ByteArray) {
        val url = url ?: throw DisconnectedException()
        val request = Request.Builder().url(url).headers(headers).post(data.toRequestBody(TEXT)).build()
        val response = withContext(Dispatchers.IO) { postClient.newCall(request).execute() }
        response.use {
            if (!it.isSuccessful) throw ConnectionFailedException("SSE send failed: HTTP ${it.code}")
        }
    }

    override suspend fun receive(): ByteArray = withContext(Dispatchers.IO) {
        val source = source ?: throw DisconnectedException()
        val event = StringBuilder()
        while (true) {
            val line = try {
                source.readUtf8Line()
            } catch (e: IOException) {
                throw DisconnectedException("SSE stream closed: ${e.message}", e)
            } ?: throw DisconnectedException("SSE stream ended")

            if (line.isEmpty()) {
                if (event.isNotEmpty()) return@withContext event.toString().toByteArray(Charsets.UTF_8)
                continue
            }
            if (line.startsWith("data:")) {
                var payload = line.substring(5)
                if (payload.startsWith(" ")) payload = payload.substring(1)
                if (event.isNotEmpty()) event.append('\n')
                event.append(payload)
            }
            // event:, id:, retry: and comments are ignored.
        }
        @Suppress("UNREACHABLE_CODE")
        throw DisconnectedException()
    }

    override suspend fun close() {
        call?.cancel()
        call = null
        runCatching { response?.close() }
        response = null
        source = null
        url = null
    }

    private companion object {
        val TEXT = "text/plain;charset=UTF-8".toMediaType()
    }
}
