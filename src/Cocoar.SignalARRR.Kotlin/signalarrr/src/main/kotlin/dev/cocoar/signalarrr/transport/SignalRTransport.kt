package dev.cocoar.signalarrr.transport

import dev.cocoar.signalarrr.TransportType
import okhttp3.HttpUrl
import okhttp3.OkHttpClient

/** Abstraction over the wire transport (WebSocket, SSE, Long Polling). */
public interface SignalRTransport {
    /** Opens the transport. */
    public suspend fun connect(url: HttpUrl)

    /** Sends one frame / one HTTP body to the server. */
    public suspend fun send(data: ByteArray)

    /** Suspends until the next chunk of data arrives. Throws when the transport is closed. */
    public suspend fun receive(): ByteArray

    /** Closes the transport; idempotent. */
    public suspend fun close()
}

internal object TransportFactory {
    fun create(type: TransportType, httpClient: OkHttpClient, binary: Boolean, headers: Map<String, String>): SignalRTransport =
        when (type) {
            TransportType.WEB_SOCKETS -> WebSocketTransport(httpClient, binary, headers)
            TransportType.SERVER_SENT_EVENTS -> SseTransport(httpClient, headers)
            TransportType.LONG_POLLING -> LongPollingTransport(httpClient, binary, headers)
        }
}

internal fun okhttp3.Request.Builder.headers(headers: Map<String, String>): okhttp3.Request.Builder = apply {
    headers.forEach { (k, v) -> header(k, v) }
}

/** Builds the URLs of the SignalR connection lifecycle from the hub URL. */
public object TransportUrls {

    /** `{hub}/negotiate?negotiateVersion=1`, inserted into the *path* so an existing query on the hub URL survives. */
    public fun negotiate(hubUrl: HttpUrl): HttpUrl =
        hubUrl.newBuilder()
            .encodedPath(hubUrl.encodedPath.trimEnd('/') + "/negotiate")
            .addQueryParameter("negotiateVersion", "1")
            .build()

    /**
     * The transport URL: the hub URL plus `id=<connectionToken>` and, when given, `access_token=<token>`.
     * The token is only passed here for `TransportCredential.QUERY`; by default it travels as a header.
     * The server side of the query convention is `UseSignalARRRAccessTokenValidation` or JwtBearer's
     * `OnMessageReceived`.
     */
    public fun transport(hubUrl: HttpUrl, connectionToken: String, accessToken: String?): HttpUrl {
        val builder = hubUrl.newBuilder().addQueryParameter("id", connectionToken)
        if (!accessToken.isNullOrEmpty()) builder.addQueryParameter("access_token", accessToken)
        return builder.build()
    }
}
