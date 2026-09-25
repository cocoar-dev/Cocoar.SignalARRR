package dev.cocoar.signalarrr

import dev.cocoar.signalarrr.protocol.HubMessage
import dev.cocoar.signalarrr.protocol.HubProtocol
import dev.cocoar.signalarrr.transport.SignalRTransport
import dev.cocoar.signalarrr.transport.TransportFactory
import dev.cocoar.signalarrr.transport.TransportUrls
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineName
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.buffer
import kotlinx.coroutines.flow.channelFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.HttpUrl
import okhttp3.HttpUrl.Companion.toHttpUrlOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/** Raw SignalR event handler: receives the invocation arguments, returns the client result (or `null`). */
public typealias RawHandler = suspend (arguments: List<JsonElement>) -> JsonElement?

/** Options of a [SignalRClient]. */
public open class SignalRClientOptions {
    public var hubProtocol: HubProtocolKind = HubProtocolKind.JSON

    /**
     * Authenticates the connection itself: the negotiate request and every request of the transport.
     * That is what `[Authorize]` on the hub class checks. Called on every connect and reconnect, so
     * a renewed token is picked up. A value without a space is sent as a bearer token; one with a
     * space carries its own scheme. Where the token travels on the transport is [transportCredential].
     */
    public var accessTokenProvider: (suspend () -> String?)? = null

    /**
     * Where the transport carries the connection token. [TransportCredential.HEADER], the default,
     * sends it as `Authorization` header — on the WebSocket upgrade and on every SSE and Long Polling
     * request — so it never appears in a URL, where proxy logs and error reports would keep it.
     * [TransportCredential.QUERY] puts it into the transport URL as `access_token`, for a server
     * that reads the token only from there.
     */
    public var transportCredential: TransportCredential = TransportCredential.HEADER

    /** Extra headers for the negotiate request and the transport connection. */
    public var headers: Map<String, String> = emptyMap()

    /** No message (ping included) for this long, and the connection is considered lost. */
    public var serverTimeout: Duration = 30.seconds

    /** Interval of the pings the client sends to keep the server from timing it out. */
    public var keepAliveInterval: Duration = 15.seconds

    /** Applies to negotiate, the transport connect, and the handshake. */
    public var handshakeTimeout: Duration = 15.seconds

    public var reconnectPolicy: ReconnectPolicy = ReconnectPolicy.Default

    /** Tried in this order; the first one the server offers wins. SSE is skipped for MessagePack. */
    public var allowedTransports: List<TransportType> = listOf(
        TransportType.WEB_SOCKETS,
        TransportType.SERVER_SENT_EVENTS,
        TransportType.LONG_POLLING,
    )

    public var logger: SignalARRRLogger = ConsoleLogger()

    /** The OkHttp client to build on; an app usually shares one. A default is created if absent. */
    public var httpClient: OkHttpClient? = null
}

/**
 * A lean SignalR client: negotiate, transport selection with fallback, handshake, JSON or
 * MessagePack, keep-alive pings, server timeout, and automatic reconnection. Handlers run in their
 * own coroutine so the receive loop never blocks.
 *
 * This is the plain SignalR layer. [HARRRConnection] builds the SignalARRR protocol on top of it;
 * use this class directly to talk to any SignalR hub.
 */
public class SignalRClient(
    url: String,
    private val options: SignalRClientOptions = SignalRClientOptions(),
) {
    public constructor(url: String, configure: SignalRClientOptions.() -> Unit) : this(url, SignalRClientOptions().apply(configure))

    private val hubUrl: HttpUrl = url.toHttpUrlOrNull()
        ?: throw IllegalArgumentException("Invalid hub URL: $url")

    /** The logger of this connection; shared with [HARRRConnection]. */
    public val logger: SignalARRRLogger = options.logger

    /** The protocol the connection speaks. */
    public val hubProtocolKind: HubProtocolKind get() = options.hubProtocol

    private val protocol: HubProtocol = HubProtocol.create(options.hubProtocol)
    private val tracker = InvocationTracker()
    private val httpClient: OkHttpClient = options.httpClient ?: OkHttpClient()
    private val negotiateClient: OkHttpClient =
        httpClient.newBuilder().callTimeout(options.handshakeTimeout.inWholeMilliseconds, TimeUnit.MILLISECONDS).build()

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO + CoroutineName("SignalRClient"))
    private val lock = Any()
    private val _state = MutableStateFlow(HubConnectionState.DISCONNECTED)

    /** The connection state as a flow — handy for UI binding. */
    public val stateFlow: StateFlow<HubConnectionState> = _state.asStateFlow()

    public val state: HubConnectionState get() = _state.value

    /** The connection id the server assigned, once negotiated. */
    @Volatile
    public var connectionId: String? = null
        private set

    /** The transport in use once connected. */
    @Volatile
    public var transportType: TransportType? = null
        private set

    private var transport: SignalRTransport? = null
    private var receiveJob: Job? = null
    private var pingJob: Job? = null
    private var watchdogJob: Job? = null
    private var reconnectJob: Job? = null
    private var generation = 0

    @Volatile
    private var lastReceivedNanos = System.nanoTime()

    private val handlers = ConcurrentHashMap<String, RawHandler>()
    private val closedCallbacks = CopyOnWriteArrayList<suspend (Throwable?) -> Unit>()
    private val reconnectingCallbacks = CopyOnWriteArrayList<suspend (Throwable?) -> Unit>()
    private val reconnectedCallbacks = CopyOnWriteArrayList<suspend (String?) -> Unit>()

    // ---------------------------------------------------------------- lifecycle

    /** Connects. Throws if the connection cannot be established; the state is then `DISCONNECTED`. */
    public suspend fun start() {
        synchronized(lock) {
            check(_state.value == HubConnectionState.DISCONNECTED) { "Connection is already ${_state.value}" }
            _state.value = HubConnectionState.CONNECTING
        }
        logger.info { "Starting connection to $hubUrl" }
        try {
            connectAndHandshake()
            logger.info { "Connection started (${transportType?.wireName}, $connectionId)" }
        } catch (e: Throwable) {
            logger.error(e) { "Connection failed: ${e.message}" }
            val t = synchronized(lock) {
                _state.value = HubConnectionState.DISCONNECTED
                transport.also { transport = null }
            }
            t?.let { runCatching { it.close() } }
            throw e
        }
    }

    /** Disconnects. Pending invocations fail with [DisconnectedException]; `onClosed` fires without an error. */
    public suspend fun stop() {
        logger.info { "Stopping connection" }
        val (wasActive, t, jobs) = synchronized(lock) {
            val was = _state.value != HubConnectionState.DISCONNECTED
            _state.value = HubConnectionState.DISCONNECTED
            generation++
            val jobs = listOfNotNull(reconnectJob, receiveJob, pingJob, watchdogJob)
            reconnectJob = null; receiveJob = null; pingJob = null; watchdogJob = null
            Triple(was, transport.also { transport = null }, jobs)
        }
        jobs.forEach { it.cancel() }
        t?.let { runCatching { it.close() } }
        tracker.failAll(DisconnectedException())
        if (wasActive) closedCallbacks.forEach { runCatching { it(null) } }
    }

    private suspend fun connectAndHandshake() {
        val negotiated = negotiate()
        connectionId = negotiated.connectionId
        val (transport, type) = selectAndConnect(negotiated)

        val gen = synchronized(lock) {
            if (_state.value != HubConnectionState.CONNECTING && _state.value != HubConnectionState.RECONNECTING) {
                null
            } else {
                this.transport = transport
                ++generation
            }
        }
        if (gen == null) {
            runCatching { transport.close() }
            throw DisconnectedException("Connection was stopped while connecting")
        }

        try {
            transport.send(protocol.writeHandshakeRequest())
            val handshake = withTimeoutOrNull(options.handshakeTimeout) {
                protocol.parseHandshake(transport.receive())
            } ?: throw HandshakeFailedException("Handshake timed out after ${options.handshakeTimeout}")
            handshake.error?.let { throw HandshakeFailedException(it) }

            val connected = synchronized(lock) {
                if (generation != gen || this.transport !== transport) {
                    false
                } else {
                    _state.value = HubConnectionState.CONNECTED
                    true
                }
            }
            if (!connected) throw DisconnectedException("Connection was stopped while connecting")

            transportType = type
            lastReceivedNanos = System.nanoTime()
            startLoops(gen, transport)

            handshake.remaining?.let { bundled ->
                protocol.parseMessages(bundled).forEach { handleMessage(gen, it) }
            }
        } catch (e: Throwable) {
            synchronized(lock) { if (this.transport === transport) this.transport = null }
            runCatching { transport.close() }
            throw e
        }
    }

    private class Negotiated(val hubUrl: HttpUrl, val connectionToken: String, val connectionId: String?, val transports: List<String>, val accessToken: String?)

    private suspend fun negotiate(): Negotiated {
        var url = hubUrl
        var accessToken = resolveAccessToken()
        repeat(MAX_NEGOTIATE_REDIRECTS + 1) {
            val request = Request.Builder()
                .url(TransportUrls.negotiate(url))
                .post(ByteArray(0).toRequestBody(null))
                .apply {
                    accessToken?.takeIf { it.isNotEmpty() }?.let { header("Authorization", bearer(it)) }
                    options.headers.forEach { (k, v) -> header(k, v) }
                }
                .build()
            val body = withContext(Dispatchers.IO) {
                try {
                    negotiateClient.newCall(request).execute().use { response ->
                        if (response.code != 200) throw NegotiationFailedException("HTTP ${response.code}", statusCode = response.code)
                        response.body.string()
                    }
                } catch (e: java.io.IOException) {
                    throw NegotiationFailedException(e.message ?: e.toString(), e)
                }
            }
            val json = runCatching { Json.parseToJsonElement(body) as? JsonObject }.getOrNull()
                ?: throw NegotiationFailedException("Invalid negotiate response")
            json.string("error")?.let { throw NegotiationFailedException(it) }

            // Redirect (e.g. Azure SignalR Service): negotiate again against the new URL.
            val redirect = json.string("url")
            if (redirect != null) {
                url = redirect.toHttpUrlOrNull() ?: throw NegotiationFailedException("Invalid redirect URL: $redirect")
                accessToken = json.string("accessToken") ?: accessToken
                return@repeat
            }

            val token = json.string("connectionToken") ?: json.string("connectionId")
                ?: throw NegotiationFailedException("Missing connectionToken in response")
            val transports = (json["availableTransports"] as? JsonArray)
                ?.mapNotNull { (it as? JsonObject)?.string("transport") }
                ?: emptyList()
            return Negotiated(url, token, json.string("connectionId"), transports, accessToken)
        }
        throw NegotiationFailedException("Too many negotiate redirects")
    }

    private suspend fun selectAndConnect(negotiated: Negotiated): Pair<SignalRTransport, TransportType> {
        var lastError: Throwable? = null
        for (candidate in options.allowedTransports) {
            if (candidate == TransportType.SERVER_SENT_EVENTS && protocol.isBinary) continue
            if (negotiated.transports.isNotEmpty() && candidate.wireName !in negotiated.transports) continue

            val accessToken = negotiated.accessToken?.takeIf { it.isNotEmpty() }
            val inQuery = options.transportCredential == TransportCredential.QUERY
            val url = TransportUrls.transport(negotiated.hubUrl, negotiated.connectionToken, if (inQuery) accessToken else null)
            // Same precedence as negotiate: an explicit Authorization in options.headers wins.
            val headers = if (!inQuery && accessToken != null) mapOf("Authorization" to bearer(accessToken)) + options.headers else options.headers
            val transport = TransportFactory.create(candidate, httpClient, protocol.isBinary, headers)
            logger.info { "Connecting via ${candidate.wireName}" }
            try {
                withTimeoutOrNull(options.handshakeTimeout) { transport.connect(url) }
                    ?: throw ConnectionFailedException("${candidate.wireName} connect timed out")
                logger.debug { "Transport ${candidate.wireName} connected" }
                return transport to candidate
            } catch (e: CancellationException) {
                runCatching { transport.close() }
                throw e
            } catch (e: Throwable) {
                runCatching { transport.close() }
                logger.warning { "Transport ${candidate.wireName} failed: ${e.message}" }
                lastError = e
            }
        }
        throw ConnectionFailedException(
            "No compatible transport. Client allows: ${options.allowedTransports.map { it.wireName }}, " +
                "server offers: ${negotiated.transports}" + (lastError?.let { ". Last error: ${it.message}" } ?: ""),
            lastError,
        )
    }

    private suspend fun resolveAccessToken(): String? = options.accessTokenProvider?.invoke()

    // ---------------------------------------------------------------- client → server

    /** Fire-and-forget invocation of a hub method. */
    public suspend fun send(method: String, arguments: List<JsonElement>) {
        sendRaw(protocol.writeInvocation(method, arguments, null))
    }

    /** Invokes a hub method and returns its result (`null` for void or `null` results). */
    public suspend fun invoke(method: String, arguments: List<JsonElement>): JsonElement? {
        val id = tracker.nextInvocationId()
        val pending = tracker.registerInvocation(id)
        try {
            sendRaw(protocol.writeInvocation(method, arguments, id))
        } catch (e: Throwable) {
            tracker.removeInvocation(id)
            throw e
        }
        val outcome = try {
            pending.await()
        } catch (e: CancellationException) {
            tracker.removeInvocation(id)
            throw e
        }
        outcome.error?.let { throw HubInvocationException(it) }
        return outcome.result
    }

    /**
     * Opens a server-to-client stream. Cancelling the collector sends `CancelInvocation` so the
     * server stops producing.
     */
    public fun stream(method: String, arguments: List<JsonElement>): Flow<JsonElement> = channelFlow {
        val id = tracker.nextInvocationId()
        tracker.registerStream(id, channel)
        try {
            sendRaw(protocol.writeStreamInvocation(method, arguments, id))
        } catch (e: Throwable) {
            tracker.removeStream(id)
            throw e
        }
        awaitClose {
            if (tracker.removeStream(id)) {
                val cancel = protocol.writeCancelInvocation(id)
                scope.launch { runCatching { sendRaw(cancel) } }
            }
        }
    }.buffer(Channel.UNLIMITED)

    // ---------------------------------------------------------------- server → client

    /**
     * Registers the handler of a raw SignalR hub method (`Clients.X.SendAsync("name", ...)`).
     * The returned value becomes the client result when the server awaits one. SignalARRR
     * contracts do not arrive here — see [HARRRConnection.onServerMethod].
     */
    public fun on(method: String, handler: RawHandler) {
        handlers[method] = handler
    }

    public fun off(method: String) {
        handlers.remove(method)
    }

    public fun onClosed(callback: suspend (error: Throwable?) -> Unit) {
        closedCallbacks.add(callback)
    }

    public fun onReconnecting(callback: suspend (error: Throwable?) -> Unit) {
        reconnectingCallbacks.add(callback)
    }

    public fun onReconnected(callback: suspend (connectionId: String?) -> Unit) {
        reconnectedCallbacks.add(callback)
    }

    // ---------------------------------------------------------------- internals

    private suspend fun sendRaw(data: ByteArray) {
        val t = synchronized(lock) {
            if (_state.value != HubConnectionState.CONNECTED) null else transport
        } ?: throw DisconnectedException()
        t.send(data)
    }

    private fun startLoops(gen: Int, transport: SignalRTransport) {
        val receive = scope.launch {
            try {
                while (isActive) {
                    val data = transport.receive()
                    lastReceivedNanos = System.nanoTime()
                    for (message in protocol.parseMessages(data)) handleMessage(gen, message)
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Throwable) {
                handleDisconnect(gen, e)
            }
        }
        val ping = scope.launch {
            val data = protocol.writePing()
            while (isActive) {
                delay(options.keepAliveInterval)
                if (_state.value != HubConnectionState.CONNECTED) break
                runCatching { sendRaw(data) }
            }
        }
        val watchdog = scope.launch {
            val timeoutNanos = options.serverTimeout.inWholeNanoseconds
            while (isActive) {
                delay(options.serverTimeout / 2)
                if (System.nanoTime() - lastReceivedNanos > timeoutNanos) {
                    handleDisconnect(gen, ConnectionFailedException("Server timeout: no message in ${options.serverTimeout}"))
                    break
                }
            }
        }
        synchronized(lock) {
            receiveJob = receive
            pingJob = ping
            watchdogJob = watchdog
        }
    }

    private suspend fun handleMessage(gen: Int, message: HubMessage) {
        when (message) {
            is HubMessage.Invocation -> {
                logger.debug { "Received invocation '${message.target}' (id ${message.invocationId ?: "none"})" }
                val handler = handlers[message.target]
                if (handler != null) {
                    scope.launch { dispatchHandler(handler, message) }
                } else if (message.invocationId != null) {
                    logger.warning { "No handler for '${message.target}', returning null" }
                    runCatching { sendRaw(protocol.writeCompletion(message.invocationId, null, null)) }
                } else {
                    logger.debug { "No handler for '${message.target}'" }
                }
            }
            is HubMessage.Completion -> tracker.complete(message.invocationId, message.error, message.result)
            is HubMessage.StreamItem -> tracker.yieldStreamItem(message.invocationId, message.item)
            HubMessage.Ping -> Unit
            is HubMessage.Close -> {
                val error = message.error?.let { HubClosedException(it) }
                handleDisconnect(gen, error, allowReconnect = message.allowReconnect)
            }
        }
    }

    private suspend fun dispatchHandler(handler: RawHandler, message: HubMessage.Invocation) {
        val id = message.invocationId
        try {
            val result = handler(message.arguments)
            if (id != null) sendRaw(protocol.writeCompletion(id, result, null))
        } catch (e: CancellationException) {
            if (!currentCoroutineContext().isActive) throw e
            reportHandlerError(id, message.target, e)
        } catch (e: Throwable) {
            reportHandlerError(id, message.target, e)
        }
    }

    private suspend fun reportHandlerError(invocationId: String?, target: String, e: Throwable) {
        logger.error(e) { "Handler for '$target' (${invocationId ?: "fire-and-forget"}) failed: ${e.message}" }
        if (invocationId != null) {
            runCatching { sendRaw(protocol.writeCompletion(invocationId, null, e.message ?: e.toString())) }
        }
    }

    /**
     * The transport died or the server closed. Runs in its own coroutine so the loop that noticed
     * can be cancelled along with the others.
     */
    private fun handleDisconnect(gen: Int, error: Throwable?, allowReconnect: Boolean = true) {
        scope.launch { disconnect(gen, error, allowReconnect) }
    }

    private suspend fun disconnect(gen: Int, error: Throwable?, allowReconnect: Boolean) {
        val (previous, shouldReconnect, t, jobs) = synchronized(lock) {
            if (gen != generation || _state.value == HubConnectionState.DISCONNECTED) return
            val previous = _state.value
            val should = previous == HubConnectionState.CONNECTED &&
                allowReconnect &&
                options.reconnectPolicy.retryDelays.isNotEmpty()
            _state.value = if (should) HubConnectionState.RECONNECTING else HubConnectionState.DISCONNECTED
            generation++
            val jobs = listOfNotNull(receiveJob, pingJob, watchdogJob)
            receiveJob = null; pingJob = null; watchdogJob = null
            Disconnect(previous, should, transport.also { transport = null }, jobs)
        }

        if (error != null) logger.warning { "Connection lost: ${error.message}" } else logger.info { "Connection closed by server" }

        jobs.forEach { it.cancel() }
        t?.let { runCatching { it.close() } }
        tracker.failAll(error ?: DisconnectedException())

        if (shouldReconnect) {
            reconnectingCallbacks.forEach { runCatching { it(error) } }
            val job = scope.launch { reconnectLoop(error) }
            synchronized(lock) { reconnectJob = job }
        } else if (previous == HubConnectionState.CONNECTED || previous == HubConnectionState.CONNECTING) {
            closedCallbacks.forEach { runCatching { it(error) } }
        }
    }

    private data class Disconnect(
        val previous: HubConnectionState,
        val shouldReconnect: Boolean,
        val transport: SignalRTransport?,
        val jobs: List<Job>,
    )

    private suspend fun reconnectLoop(originalError: Throwable?) {
        val delays = options.reconnectPolicy.retryDelays
        logger.info { "Reconnecting (${delays.size} attempts configured)" }
        for ((index, wait) in delays.withIndex()) {
            if (_state.value != HubConnectionState.RECONNECTING) return
            if (wait > Duration.ZERO) {
                logger.debug { "Reconnect attempt ${index + 1}: waiting $wait" }
                delay(wait)
                if (_state.value != HubConnectionState.RECONNECTING) return
            }
            try {
                logger.info { "Reconnect attempt ${index + 1}/${delays.size}" }
                connectAndHandshake()
                logger.info { "Reconnected ($connectionId)" }
                reconnectedCallbacks.forEach { runCatching { it(connectionId) } }
                return
            } catch (e: CancellationException) {
                throw e
            } catch (e: Throwable) {
                logger.warning { "Reconnect attempt ${index + 1} failed: ${e.message}" }
            }
        }
        logger.error { "All reconnect attempts exhausted, giving up" }
        val gaveUp = synchronized(lock) {
            if (_state.value == HubConnectionState.RECONNECTING) {
                _state.value = HubConnectionState.DISCONNECTED
                true
            } else {
                false
            }
        }
        if (gaveUp) closedCallbacks.forEach { runCatching { it(originalError) } }
    }

    private fun JsonObject.string(key: String): String? =
        (this[key] as? JsonPrimitive)?.takeIf { it.isString }?.content

    private companion object {
        const val MAX_NEGOTIATE_REDIRECTS = 5

        /** A credential without a space is a bearer token; one with a space carries its own scheme. */
        fun bearer(credential: String): String = if (credential.contains(' ')) credential else "Bearer $credential"
    }
}
