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
import java.io.IOException
import java.util.concurrent.TimeUnit

/**
 * Long Polling: client→server over HTTP POST, server→client over repeated HTTP GET. The server
 * holds each GET until data is available or its own poll timeout elapses (200 with an empty body).
 * A 204 means the server closed the connection.
 */
internal class LongPollingTransport(
    httpClient: OkHttpClient,
    private val binary: Boolean,
    private val headers: Map<String, String>,
) : SignalRTransport {

    /** The server holds a poll for a long time; the regular read timeout would cut it off. */
    private val pollClient = httpClient.newBuilder().readTimeout(0, TimeUnit.MILLISECONDS).build()
    private val postClient = httpClient

    @Volatile
    private var url: HttpUrl? = null

    @Volatile
    private var active = false

    @Volatile
    private var currentPoll: Call? = null

    override suspend fun connect(url: HttpUrl) {
        this.url = url
        active = true
        // The first poll establishes the transport on the server; it answers immediately.
        val response = withContext(Dispatchers.IO) { pollClient.newCall(Request.Builder().url(url).headers(headers).get().build()).execute() }
        response.use {
            if (it.code != 200) {
                active = false
                throw ConnectionFailedException("Long polling: HTTP ${it.code}")
            }
        }
    }

    override suspend fun send(data: ByteArray) {
        val url = url ?: throw DisconnectedException()
        if (!active) throw DisconnectedException()
        val body = data.toRequestBody(if (binary) OCTET_STREAM else TEXT)
        val request = Request.Builder().url(url).headers(headers).post(body).build()
        val response = withContext(Dispatchers.IO) { postClient.newCall(request).execute() }
        response.use {
            if (!it.isSuccessful) throw ConnectionFailedException("Long polling send failed: HTTP ${it.code}")
        }
    }

    override suspend fun receive(): ByteArray = withContext(Dispatchers.IO) {
        while (true) {
            val url = url ?: throw DisconnectedException()
            if (!active) throw DisconnectedException()
            val call = pollClient.newCall(Request.Builder().url(url).headers(headers).get().build())
            currentPoll = call
            val response = try {
                call.execute()
            } catch (e: IOException) {
                if (!active) throw DisconnectedException()
                throw DisconnectedException("Long polling failed: ${e.message}", e)
            } finally {
                currentPoll = null
            }
            response.use {
                when (it.code) {
                    204 -> {
                        active = false
                        throw DisconnectedException("Server closed the connection")
                    }
                    200 -> {
                        val bytes = it.body.bytes()
                        if (bytes.isNotEmpty()) return@withContext bytes
                        // Empty body: the server's poll timeout elapsed, poll again.
                    }
                    else -> throw ConnectionFailedException("Long polling: HTTP ${it.code}")
                }
            }
        }
        @Suppress("UNREACHABLE_CODE")
        throw DisconnectedException()
    }

    override suspend fun close() {
        active = false
        currentPoll?.cancel()
        currentPoll = null
        val url = url ?: return
        this.url = null
        // DELETE tells the server to release the connection instead of waiting for its timeout.
        withContext(Dispatchers.IO) {
            runCatching { postClient.newCall(Request.Builder().url(url).headers(headers).delete().build()).execute().close() }
        }
    }

    private companion object {
        val TEXT = "text/plain;charset=UTF-8".toMediaType()
        val OCTET_STREAM = "application/octet-stream".toMediaType()
    }
}
