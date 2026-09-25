package dev.cocoar.signalarrr

import dev.cocoar.signalarrr.serialization.JsonValues
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineName
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonPrimitive
import okhttp3.OkHttpClient
import java.util.concurrent.ConcurrentHashMap
import kotlin.time.Duration

/** Handles a server-to-client call; the returned value is the result (ignored for fire-and-forget calls). */
public typealias ServerMethodHandler = suspend (args: ServerMethodArgs) -> Any?

/** Handles a server-to-client streaming call; each emitted item is sent back to the server. */
public typealias ServerStreamMethodHandler = suspend (args: ServerMethodArgs) -> Flow<Any?>

/**
 * Options of a [HARRRConnection]; the [SignalRClientOptions] part configures the connection underneath.
 *
 * A connection has two credentials, checked by different things. The **connection credential**
 * authenticates negotiate and the transport and is what `[Authorize]` on the hub class checks. The
 * **message credential** travels with every message, answers token challenges and authorises file
 * transfers, and is what `[Authorize]` on a method or a `ServerMethods` class checks. The options
 * are named the same in every SignalARRR client, and nothing is coupled implicitly: setting one of
 * the two sets only that one.
 */
public class HARRRConnectionOptions : SignalRClientOptions() {
    /**
     * One credential for the connection and for every message — the common case. A
     * [connectionCredential] or [messageCredential] set alongside it takes its part over.
     */
    public var credential: (suspend () -> String?)? = null

    /**
     * Authenticates the connection: the negotiate request and the transport. Called on every
     * connect and reconnect. Takes the place of [SignalRClientOptions.accessTokenProvider]; set
     * only one of the two.
     */
    public var connectionCredential: (suspend () -> String?)? = null

    /**
     * Authenticates each message: travels as `ClientRequestMessage.Authorization`, answers token
     * challenges, and authorises file-transfer requests. Called per use, so a refreshed token is
     * picked up.
     */
    public var messageCredential: (suspend () -> String?)? = null

    /**
     * The message credential under its former name. Unlike [messageCredential], it also
     * authenticates the connection when [SignalRClientOptions.accessTokenProvider] is left `null` —
     * the implicit coupling the new options no longer have.
     */
    @Deprecated("Use messageCredential, or credential for one credential covering the connection and every message.")
    public var messageAccessTokenProvider: (suspend () -> String?)? = null

    /** The JSON configuration used to encode arguments and decode results. */
    public var json: Json = JsonValues.defaultJson
}

/**
 * The SignalARRR client: typed bidirectional RPC over a [SignalRClient].
 *
 * `invoke`, `send` and `stream` call the server; [onServerMethod] and friends answer calls from
 * the server. Token challenges, cancellation propagation and HTTP stream references are handled
 * underneath.
 */
public class HARRRConnection private constructor(
    /** The SignalR connection underneath — for raw hub methods and connection events. */
    public val client: SignalRClient,
    private val messageAccessTokenProvider: suspend () -> String?,
    /** The JSON configuration of this connection. */
    public val json: Json,
    httpClient: OkHttpClient,
    public val serverTimeout: Duration,
    public val keepAliveInterval: Duration,
    public val handshakeTimeout: Duration,
) {
    /** Cancellation ids of running server-to-client handlers. */
    public val cancellationManager: CancellationManager = CancellationManager()

    private val streamResolver = StreamReferenceResolver(httpClient)
    private val serverHandlers = ConcurrentHashMap<String, ServerMethodHandler>()
    private val serverStreamHandlers = ConcurrentHashMap<String, ServerStreamMethodHandler>()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO + CoroutineName("HARRRConnection"))

    /** Observes every server request envelope before it is dispatched. */
    public var onServerRequestMessageReceived: ((ServerRequestMessage) -> Unit)? = null

    public val state: HubConnectionState get() = client.state
    public val connectionId: String? get() = client.connectionId

    public companion object {
        /**
         * Creates a connection to [url]. Credentials come from [HARRRConnectionOptions.credential],
         * [HARRRConnectionOptions.connectionCredential] and [HARRRConnectionOptions.messageCredential].
         *
         * @throws IllegalArgumentException when a credential is set in two places — the former
         *   `messageAccessTokenProvider` next to the new options, or `accessTokenProvider` next to
         *   `connectionCredential`/`credential`.
         */
        public fun create(url: String, configure: HARRRConnectionOptions.() -> Unit = {}): HARRRConnection {
            val options = HARRRConnectionOptions().apply(configure)
            @Suppress("DEPRECATION")
            val legacyMessage = options.messageAccessTokenProvider
            val usesNewOptions = options.credential != null || options.connectionCredential != null || options.messageCredential != null
            val messageProvider = if (usesNewOptions) {
                require(legacyMessage == null) {
                    "messageAccessTokenProvider is the former name of messageCredential; set only messageCredential (or credential)."
                }
                val connection = options.connectionCredential ?: options.credential
                if (connection != null) {
                    require(options.accessTokenProvider == null) {
                        "The connection credential is set twice: through accessTokenProvider and through connectionCredential/credential. Set only one of them."
                    }
                    options.accessTokenProvider = connection
                }
                options.messageCredential ?: options.credential
            } else {
                // The former option keeps its former behaviour: it also covers the connection.
                if (options.accessTokenProvider == null) options.accessTokenProvider = legacyMessage
                legacyMessage
            }
            val client = SignalRClient(url, options)
            return create(client, messageProvider, options.json, options)
        }

        /** Wraps an existing [SignalRClient]. */
        public fun create(
            client: SignalRClient,
            messageAccessTokenProvider: (suspend () -> String?)? = null,
            json: Json = JsonValues.defaultJson,
            options: SignalRClientOptions = SignalRClientOptions(),
        ): HARRRConnection {
            val connection = HARRRConnection(
                client = client,
                messageAccessTokenProvider = messageAccessTokenProvider ?: { null },
                json = json,
                httpClient = options.httpClient ?: OkHttpClient(),
                serverTimeout = options.serverTimeout,
                keepAliveInterval = options.keepAliveInterval,
                handshakeTimeout = options.handshakeTimeout,
            )
            connection.registerBuiltInHandlers()
            return connection
        }
    }

    // ---------------------------------------------------------------- lifecycle

    public suspend fun start(): Unit = client.start()

    public suspend fun stop(): Unit = client.stop()

    public fun onClosed(callback: suspend (error: Throwable?) -> Unit): Unit = client.onClosed(callback)
    public fun onReconnecting(callback: suspend (error: Throwable?) -> Unit): Unit = client.onReconnecting(callback)
    public fun onReconnected(callback: suspend (connectionId: String?) -> Unit): Unit = client.onReconnected(callback)

    // ---------------------------------------------------------------- client → server

    /** Calls [method] and decodes its result as [T]. */
    public suspend inline fun <reified T> invoke(method: String, vararg arguments: Any?, genericArguments: List<String> = emptyList()): T =
        JsonValues.decode(invokeCore(method, arguments.toList(), genericArguments), json)

    /** Calls [method] and returns the raw result. `ByteArray`/`File`/`InputStream` arguments are uploaded first. */
    public suspend fun invokeCore(method: String, arguments: List<Any?>, genericArguments: List<String> = emptyList()): JsonElement? {
        val request = buildClientRequest(method, arguments, genericArguments)
        return translateErrors {
            client.invoke(MethodNames.INVOKE_MESSAGE_RESULT, listOf(encodeRequest(request)))
        }
    }

    /** Fire-and-forget call of [method]. */
    public suspend fun send(method: String, vararg arguments: Any?, genericArguments: List<String> = emptyList()) {
        val request = buildClientRequest(method, arguments.toList(), genericArguments)
        translateErrors {
            client.send(MethodNames.SEND_MESSAGE, listOf(encodeRequest(request)))
        }
    }

    /** Opens a stream from [method] and decodes each item as [T]. Cancelling the collector cancels the stream. */
    public inline fun <reified T> stream(method: String, vararg arguments: Any?, genericArguments: List<String> = emptyList()): Flow<T> =
        streamCore(method, arguments.toList(), genericArguments).map { JsonValues.decode<T>(it, json) }

    public fun streamCore(method: String, arguments: List<Any?>, genericArguments: List<String> = emptyList()): Flow<JsonElement> = flow {
        val request = buildClientRequest(method, arguments, genericArguments)
        try {
            client.stream(MethodNames.STREAM_MESSAGE, listOf(encodeRequest(request))).collect { emit(it) }
        } catch (e: HubInvocationException) {
            throw translate(e)
        }
    }

    /** The typed proxy of a contract — pass the generated proxy's companion, e.g. `getTypedMethods(IChatHubProxy)`. */
    public fun <T : Any> getTypedMethods(factory: HubProxyFactory<T>): T = factory.create(this)

    // ---------------------------------------------------------------- server → client

    /**
     * Registers the handler of a SignalARRR server-to-client contract method. [name] is the wire
     * name, `Interface|Method` (see the contract wire names guide); it is matched exactly. Use
     * [on] instead for raw SignalR events sent with `Clients.X.SendAsync("name", ...)`.
     *
     * The handler runs in a coroutine that the server can cancel when the contract declares a
     * `CancellationToken`. Return the result, or `Unit` for void methods; a `ByteArray`, `File`
     * or `InputStream` result is uploaded and returned to the server as a stream reference.
     */
    public fun onServerMethod(name: String, handler: ServerMethodHandler) {
        serverHandlers[name] = handler
    }

    /** Registers the handler of a server-to-client streaming method (`IAsyncEnumerable<T>` on the contract). */
    public fun onServerStreamMethod(name: String, handler: ServerStreamMethodHandler) {
        serverStreamHandlers[name] = handler
    }

    public fun removeServerMethod(name: String) {
        serverHandlers.remove(name)
        serverStreamHandlers.remove(name)
    }

    /** Registers [handlers] under `prefix|name`. */
    public fun registerHandlers(prefix: String, handlers: Map<String, ServerMethodHandler>) {
        handlers.forEach { (name, handler) -> serverHandlers["$prefix|$name"] = handler }
    }

    /** Registers every handler of [handler] under its interface name. */
    public fun registerInterface(handler: ServerInterfaceHandler) {
        registerHandlers(handler.interfaceName, handler.handlers())
    }

    /**
     * Registers the handler of a raw SignalR event. The returned value becomes the client result.
     * SignalARRR contract calls do not arrive here — use [onServerMethod] for those.
     */
    public fun on(method: String, handler: suspend (args: ServerMethodArgs) -> Any?) {
        client.on(method) { arguments ->
            val result = handler(ServerMethodArgs(arguments, json))
            if (result == null || result == Unit) null else JsonValues.encode(result, json)
        }
    }

    public fun off(method: String): Unit = client.off(method)

    // ---------------------------------------------------------------- built-in wire handlers

    private fun registerBuiltInHandlers() {
        client.on(MethodNames.CHALLENGE_AUTHENTICATION) {
            val token = messageAccessTokenProvider() ?: ""
            JsonPrimitive(token)
        }

        client.on(MethodNames.INVOKE_SERVER_REQUEST) { arguments ->
            val request = decodeServerRequest(arguments)
            onServerRequestMessageReceived?.invoke(request)
            val result = dispatchServerMethod(request)
            encodeResult(result)
        }

        client.on(MethodNames.INVOKE_SERVER_MESSAGE) { arguments ->
            val request = decodeServerRequest(arguments)
            onServerRequestMessageReceived?.invoke(request)
            val streamId = request.streamId
            if (streamId != null) {
                scope.launch { streamBackToServer(request, streamId) }
            } else {
                try {
                    dispatchServerMethod(request)
                } catch (e: CancellationException) {
                    if (!currentCoroutineContext().isActive) throw e
                    client.logger.warning { "Server message '${request.method}' was cancelled: ${e.message}" }
                } catch (e: Throwable) {
                    client.logger.error(e) { "Failed to handle server message '${request.method}': ${e.message}" }
                }
            }
            null
        }

        client.on(MethodNames.CANCEL_TOKEN_FROM_SERVER) { arguments ->
            val request = decodeServerRequest(arguments)
            request.cancellationGuid?.let { cancellationManager.cancel(it) }
            null
        }

        client.onClosed { cancellationManager.cancelAll("Connection closed") }
        client.onReconnecting { cancellationManager.cancelAll("Connection lost") }
    }

    private fun decodeServerRequest(arguments: List<JsonElement>): ServerRequestMessage {
        val first = arguments.firstOrNull() ?: throw InvocationFailedException("No arguments in server request")
        return JsonValues.decode(first, ServerRequestMessage.serializer(), json)
    }

    /**
     * Runs the handler of [request] in a child coroutine registered with the cancellation
     * manager, so `CancelTokenFromServer` cancels exactly this call. The cancellation surfaces
     * here as a [ServerCancelledException] rather than cancelling the dispatching coroutine, so
     * the error can still be reported back to the server.
     */
    private suspend fun dispatchServerMethod(request: ServerRequestMessage): Any? {
        val handler = serverHandlers[request.method]
        if (handler == null) {
            client.logger.warning {
                "No server-method handler registered for '${request.method}'. Register it with " +
                    "onServerMethod(\"${request.method}\") { ... } — on(...) only handles raw SignalR events, " +
                    "not SignalARRR server→client contracts."
            }
            return null
        }
        return runHandler(request) { args -> handler(args) }
    }

    private suspend fun <R> runHandler(request: ServerRequestMessage, block: suspend (ServerMethodArgs) -> R): R = coroutineScope {
        val ids = mutableListOf<String>()
        val deferred = async {
            val args = buildHandlerArgs(request, ids, currentCoroutineContext()[kotlinx.coroutines.Job]!!)
            block(args)
        }
        request.cancellationGuid?.let { ids.add(it) }
        // The handler may not have registered its ids yet: register the guid now, the argument
        // ids as soon as they are known (buildHandlerArgs adds them).
        request.cancellationGuid?.let { cancellationManager.register(it, deferred) }
        try {
            deferred.await()
        } catch (e: CancellationException) {
            if (deferred.isCancelled && currentCoroutineContext().isActive) {
                val cause = generateSequence<Throwable>(e) { it.cause }.firstOrNull { it is ServerCancelledException }
                throw OperationCancelledException(cause?.message ?: e.message ?: "The operation was cancelled", e)
            }
            throw e
        } finally {
            ids.forEach { cancellationManager.remove(it) }
        }
    }

    private suspend fun buildHandlerArgs(request: ServerRequestMessage, ids: MutableList<String>, job: kotlinx.coroutines.Job): ServerMethodArgs {
        val values = mutableListOf<Any?>()
        for (element in request.arguments ?: emptyList()) {
            val token = CancellationTokenReference.from(element)
            if (token != null) {
                // Keyed on the reference's own id, not on the request's cancellationGuid: the guid
                // cancels the call, the reference cancels this parameter, and two token parameters
                // must be cancellable apart.
                ids.add(token.id)
                cancellationManager.register(token.id, job)
                values.add(ServerCancellationToken(token.id, job))
                continue
            }
            val streamRef = StreamReference.from(element)
            if (streamRef != null) {
                values.add(streamResolver.resolve(streamRef, messageAccessTokenProvider()))
                continue
            }
            values.add(element)
        }
        return ServerMethodArgs(values, json)
    }

    private suspend fun streamBackToServer(request: ServerRequestMessage, streamId: String) {
        val streamHandler = serverStreamHandlers[request.method]
        val handler = serverHandlers[request.method]
        if (streamHandler == null && handler == null) {
            client.logger.warning {
                "No server-method handler registered for streaming request '${request.method}'. " +
                    "Register it with onServerStreamMethod(\"${request.method}\") { ... } or onServerMethod(...)."
            }
            runCatching { client.send(MethodNames.STREAM_COMPLETE_TO_SERVER, listOf(JsonPrimitive(streamId), JsonNull)) }
            return
        }
        try {
            runHandler(request) { args ->
                if (streamHandler != null) {
                    streamHandler(args).collect { item ->
                        client.send(MethodNames.STREAM_ITEM_TO_SERVER, listOf(JsonPrimitive(streamId), JsonValues.encode(item, json)))
                    }
                } else {
                    val result = handler!!(args)
                    client.send(MethodNames.STREAM_ITEM_TO_SERVER, listOf(JsonPrimitive(streamId), encodeResult(result) ?: JsonNull))
                }
            }
            runCatching { client.send(MethodNames.STREAM_COMPLETE_TO_SERVER, listOf(JsonPrimitive(streamId), JsonNull)) }
        } catch (e: CancellationException) {
            if (!currentCoroutineContext().isActive) throw e
            runCatching { client.send(MethodNames.STREAM_COMPLETE_TO_SERVER, listOf(JsonPrimitive(streamId), JsonPrimitive(e.message ?: "cancelled"))) }
        } catch (e: Throwable) {
            client.logger.error(e) { "Streaming handler '${request.method}' failed: ${e.message}" }
            runCatching { client.send(MethodNames.STREAM_COMPLETE_TO_SERVER, listOf(JsonPrimitive(streamId), JsonPrimitive(e.message ?: e.toString()))) }
        }
    }

    // ---------------------------------------------------------------- encoding

    private suspend fun buildClientRequest(method: String, arguments: List<Any?>, genericArguments: List<String>): ClientRequestMessage {
        val token = messageAccessTokenProvider() ?: ""
        val encoded = arguments.map { encodeArgument(it) }
        return ClientRequestMessage(method, encoded, token, genericArguments)
    }

    private fun encodeRequest(request: ClientRequestMessage): JsonElement =
        JsonValues.encode(request, ClientRequestMessage.serializer(), json)

    /** Values with upload bodies are uploaded and replaced by a stream reference; everything else is encoded. */
    private suspend fun encodeArgument(value: Any?): JsonElement {
        val body = StreamReferenceResolver.bodyOf(value)
        if (body != null) return uploadAndReference(body).toJson()
        return JsonValues.encode(value, json)
    }

    private suspend fun encodeResult(result: Any?): JsonElement? {
        if (result == null || result == Unit) return null
        val body = StreamReferenceResolver.bodyOf(result)
        if (body != null) return uploadAndReference(body).toJson()
        return JsonValues.encode(result, json)
    }

    private suspend fun uploadAndReference(body: okhttp3.RequestBody): StreamReference {
        val slot = client.invoke(MethodNames.REQUEST_UPLOAD_SLOT, emptyList())
        val uploadUrl = (slot as? JsonPrimitive)?.takeIf { it.isString }?.content
            ?: throw StreamTransferException("RequestUploadSlot returned no URL")
        streamResolver.upload(uploadUrl, body, messageAccessTokenProvider())
        return StreamReference(uploadUrl)
    }

    private inline fun <R> translateErrors(block: () -> R): R {
        try {
            return block()
        } catch (e: HubInvocationException) {
            throw translate(e)
        }
    }

    private fun translate(e: HubInvocationException): HARRRException {
        if (e is HARRRException) return e
        return HARRRException(HARRRError.parse(e.message ?: ""), e.message ?: "")
    }
}

/** The error reported to the server when it cancels a handler; also thrown by fire-and-forget dispatch. */
public class OperationCancelledException(message: String, cause: Throwable? = null) : SignalRException(message, cause)
