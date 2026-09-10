package dev.cocoar.signalarrr

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okio.BufferedSink
import okio.source
import java.io.File
import java.io.IOException
import java.io.InputStream

/** A download or upload of a stream reference failed. */
public class StreamTransferException(message: String, cause: Throwable? = null) : SignalRException(message, cause)

/**
 * The bytes the client sends for a `Stream` parameter or returns from a handler declared with a
 * `Stream` result. `ByteArray`, [File] and [InputStream] arguments are accepted directly; wrap an
 * [InputStream] in [UploadStream] to name its length (lets the server reject oversized uploads early).
 */
public class UploadStream(
    public val open: () -> InputStream,
    public val contentLength: Long = -1,
)

/** Resolves [StreamReference] values by downloading them, and uploads bytes to upload slots. */
public class StreamReferenceResolver(private val httpClient: OkHttpClient) {

    /** Downloads the referenced content, fully buffered. */
    public suspend fun resolve(ref: StreamReference, authorization: String?): ByteArray = withContext(Dispatchers.IO) {
        val request = authorizedRequest(ref.uri, authorization).get().build()
        try {
            httpClient.newCall(request).execute().use { response ->
                if (!response.isSuccessful) throw StreamTransferException("Download failed: HTTP ${response.code}")
                response.body.bytes()
            }
        } catch (e: IOException) {
            throw StreamTransferException("Download failed: ${e.message}", e)
        }
    }

    /** POSTs [body] to an upload slot URL. */
    public suspend fun upload(uploadUrl: String, body: RequestBody, authorization: String?): Unit = withContext(Dispatchers.IO) {
        val request = authorizedRequest(uploadUrl, authorization).post(body).build()
        try {
            httpClient.newCall(request).execute().use { response ->
                if (!response.isSuccessful) throw StreamTransferException("Upload failed: HTTP ${response.code}")
            }
        } catch (e: IOException) {
            throw StreamTransferException("Upload failed: ${e.message}", e)
        }
    }

    /**
     * The request for a file-transfer URL, carrying the connection's credential. `/download/{id}`
     * and `/upload/{id}` are ordinary HTTP endpoints: they carry the hub's authorization
     * requirements but not its connection, so nothing authenticates them unless the request does.
     */
    private fun authorizedRequest(url: String, authorization: String?): Request.Builder {
        val parsed = url.toHttpUrlOrNull() ?: throw StreamTransferException("Invalid stream URL: $url")
        val builder = Request.Builder().url(parsed)
        if (!authorization.isNullOrEmpty()) {
            builder.header("Authorization", if (authorization.contains(' ')) authorization else "Bearer $authorization")
        }
        return builder
    }

    public companion object {
        private val OCTET_STREAM = "application/octet-stream".toMediaType()

        /** Turns an upload-capable value into a request body, or `null` if [value] is not one. */
        public fun bodyOf(value: Any?): RequestBody? = when (value) {
            is ByteArray -> value.toRequestBody(OCTET_STREAM)
            is File -> value.asRequestBody(OCTET_STREAM)
            is InputStream -> streamBody({ value }, -1)
            is UploadStream -> streamBody(value.open, value.contentLength)
            else -> null
        }

        private fun streamBody(open: () -> InputStream, length: Long): RequestBody = object : RequestBody() {
            override fun contentType() = OCTET_STREAM
            override fun contentLength(): Long = length
            override fun isOneShot(): Boolean = true
            override fun writeTo(sink: BufferedSink) {
                open().source().use { sink.writeAll(it) }
            }
        }
    }
}
