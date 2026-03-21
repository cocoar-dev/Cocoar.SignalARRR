import Foundation
import SignalRClient

/// SignalARRR client wrapping Microsoft's SignalR `HubConnection`.
///
/// Provides typed bidirectional RPC: `invoke`, `send`, `stream` for client-to-server
/// calls, and `onServerMethod` for server-to-client calls. Handles the SignalARRR wire
/// protocol (authentication challenges, server request dispatch, cancellation propagation).
public final class HARRRConnection: @unchecked Sendable {
    private let hubConnection: HubConnection
    private let accessTokenFactory: @Sendable () async -> String
    private let cancellationManager = CancellationManager()
    private let serverRequestHandlers = ServerRequestHandlerStore()
    private let options: HARRRConnectionOptions

    // MARK: - Feature 1: Connection State

    /// The current state of the underlying SignalR connection.
    public var state: HubConnectionState {
        get async { await hubConnection.state() }
    }

    // MARK: - Feature 2: ConnectionId

    /// The connection ID reported by the server.
    ///
    /// `HubConnection` does not expose `connectionId` publicly — it is private on the
    /// internal `HttpConnection`. This property is `nil` by default. You can set it
    /// manually after connecting, or access the underlying connection via
    /// `asSignalRHubConnection()`.
    public private(set) var connectionId: String?

    // MARK: - Feature 3: Timeout Configuration

    /// The server timeout interval (in seconds) configured for this connection.
    public let serverTimeoutInterval: TimeInterval

    /// The keep-alive interval (in seconds) configured for this connection.
    public let keepAliveIntervalValue: TimeInterval

    // MARK: - Feature 6: OnServerRequestMessage Callback

    /// Callback fired before dispatching any `InvokeServerRequest` or `InvokeServerMessage`.
    ///
    /// Use this to inspect or log incoming server request messages before they are handled.
    public var onServerRequestMessageReceived: (@Sendable (ServerRequestMessage) -> Void)?

    // MARK: - Initialization

    private init(
        hubConnection: HubConnection,
        accessTokenFactory: @escaping @Sendable () async -> String,
        options: HARRRConnectionOptions,
        serverTimeout: TimeInterval,
        keepAliveInterval: TimeInterval
    ) {
        self.hubConnection = hubConnection
        self.accessTokenFactory = accessTokenFactory
        self.options = options
        self.serverTimeoutInterval = serverTimeout
        self.keepAliveIntervalValue = keepAliveInterval
    }

    /// Register the built-in SignalARRR protocol handlers on the hub connection.
    ///
    /// Must be called after init and before start. Separated because `HubConnection`
    /// is an actor and `on()` requires `await`.
    private func registerBuiltInHandlers() async {
        // Authentication challenge
        await hubConnection.on(MethodNames.challengeAuthentication) { [weak self] (req: ServerRequestMessage) async in
            guard let self else { return }
            let token = await self.accessTokenFactory()
            try? await self.hubConnection.send(
                method: MethodNames.replyServerRequest,
                arguments: req.id, token, NSNull()
            )
        }

        // Server request (expects a reply)
        await hubConnection.on(MethodNames.invokeServerRequest) { [weak self] (req: ServerRequestMessage) async in
            guard let self else { return }
            self.onServerRequestMessageReceived?(req)

            // Feature 8: If streamId is present, route to stream handling
            if let streamId = req.streamId {
                await self.handleStreamBackToServer(req: req, streamId: streamId)
                return
            }

            var payload: AnyCodable = AnyCodable(NSNull())
            var errorMessage: String? = nil
            do {
                payload = try await self.dispatchServerMethod(req)
            } catch let err {
                errorMessage = String(describing: err)
            }
            await self.sendResponse(requestId: req.id, payload: payload, errorMessage: errorMessage)
        }

        // Server message (fire-and-forget)
        await hubConnection.on(MethodNames.invokeServerMessage) { [weak self] (req: ServerRequestMessage) async in
            guard let self else { return }
            self.onServerRequestMessageReceived?(req)

            // Feature 8: If streamId is present, route to stream handling
            if let streamId = req.streamId {
                await self.handleStreamBackToServer(req: req, streamId: streamId)
                return
            }

            _ = try? await self.dispatchServerMethod(req)
        }

        // Cancellation from server
        await hubConnection.on(MethodNames.cancelTokenFromServer) { [weak self] (req: ServerRequestMessage) async in
            guard let self else { return }
            if let guid = req.cancellationGuid {
                await self.cancellationManager.cancel(id: guid)
            }
        }
    }

    // MARK: - Dispatch

    private func dispatchServerMethod(_ req: ServerRequestMessage) async throws -> AnyCodable {
        guard let handler = await serverRequestHandlers.handler(for: req.method) else {
            return AnyCodable(NSNull())
        }

        let args = try await buildHandlerArgs(req)
        return try await handler(args)
    }

    /// Build the argument array for a server request handler.
    ///
    /// Replaces cancellation token references with GUIDs and resolves stream references to `Data`.
    private func buildHandlerArgs(_ req: ServerRequestMessage) async throws -> [Any] {
        var args: [Any] = []
        for anyCodable in (req.arguments ?? []) {
            if isCancellationTokenReference(anyCodable.value) != nil,
               let guid = req.cancellationGuid {
                args.append(guid)
            } else if let streamRef = isStreamReference(anyCodable.value) {
                let data = try await StreamReferenceResolver.resolve(streamRef)
                args.append(data)
            } else {
                args.append(anyCodable.value)
            }
        }
        return args
    }

    // MARK: - Feature 9: Response Sending

    /// Send a response to a server request, using either SignalR or HTTP depending on options.
    private func sendResponse(requestId: String, payload: AnyCodable, errorMessage: String?) async {
        if options.useHttpResponse, let baseURL = options.baseURL {
            await sendHttpResponse(baseURL: baseURL, requestId: requestId, payload: payload, errorMessage: errorMessage)
        } else {
            try? await hubConnection.send(
                method: MethodNames.replyServerRequest,
                arguments: requestId, payload, errorMessage as Any
            )
        }
    }

    /// Send a response via HTTP POST to `{baseURL}/response/{requestId}`.
    private func sendHttpResponse(baseURL: URL, requestId: String, payload: AnyCodable, errorMessage: String?) async {
        var url = baseURL.appendingPathComponent("response").appendingPathComponent(requestId)
        if let errorMessage {
            var components = URLComponents(url: url, resolvingAgainstBaseURL: false)
            components?.queryItems = [URLQueryItem(name: "error", value: errorMessage)]
            if let errorURL = components?.url {
                url = errorURL
            }
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.httpBody = try? JSONEncoder().encode(payload)
        _ = try? await URLSession.shared.data(for: request)
    }

    // MARK: - Feature 8: Client-to-Server Streaming

    /// Handle a server request that expects streaming results back.
    private func handleStreamBackToServer(req: ServerRequestMessage, streamId: String) async {
        // Try stream handler first
        if let streamHandler = await serverRequestHandlers.streamHandler(for: req.method) {
            do {
                let args = try await buildHandlerArgs(req)
                let stream = try await streamHandler(args)
                for try await item in stream {
                    try? await hubConnection.send(
                        method: MethodNames.streamItemToServer,
                        arguments: streamId, item
                    )
                }
                // Success completion
                try? await hubConnection.send(
                    method: MethodNames.streamCompleteToServer,
                    arguments: streamId, NSNull()
                )
            } catch {
                // Error completion
                try? await hubConnection.send(
                    method: MethodNames.streamCompleteToServer,
                    arguments: streamId, String(describing: error)
                )
            }
            return
        }

        // Fall back to regular handler for single-item result
        if let handler = await serverRequestHandlers.handler(for: req.method) {
            do {
                let args = try await buildHandlerArgs(req)
                let result = try await handler(args)
                try? await hubConnection.send(
                    method: MethodNames.streamItemToServer,
                    arguments: streamId, result
                )
                try? await hubConnection.send(
                    method: MethodNames.streamCompleteToServer,
                    arguments: streamId, NSNull()
                )
            } catch {
                try? await hubConnection.send(
                    method: MethodNames.streamCompleteToServer,
                    arguments: streamId, String(describing: error)
                )
            }
            return
        }

        // No handler found — send empty completion
        try? await hubConnection.send(
            method: MethodNames.streamCompleteToServer,
            arguments: streamId, NSNull()
        )
    }

    // MARK: - Factory Methods

    /// Create a connection using a `HubConnectionBuilder` configuration closure.
    public static func create(
        _ configure: (HubConnectionBuilder) -> Void,
        accessTokenFactory: @escaping @Sendable () async -> String = { "" },
        options: HARRRConnectionOptions = HARRRConnectionOptions(),
        serverTimeout: TimeInterval = 30,
        keepAliveInterval: TimeInterval = 15
    ) async -> HARRRConnection {
        let builder = HubConnectionBuilder()
        configure(builder)
        _ = builder.withServerTimeout(serverTimeout: serverTimeout)
        _ = builder.withKeepAliveInterval(keepAliveInterval: keepAliveInterval)
        let hubConnection = builder.build()
        let connection = HARRRConnection(
            hubConnection: hubConnection,
            accessTokenFactory: accessTokenFactory,
            options: options,
            serverTimeout: serverTimeout,
            keepAliveInterval: keepAliveInterval
        )
        await connection.registerBuiltInHandlers()
        return connection
    }

    /// Create a connection wrapping an existing `HubConnection`.
    public static func create(
        hubConnection: HubConnection,
        accessTokenFactory: @escaping @Sendable () async -> String = { "" },
        options: HARRRConnectionOptions = HARRRConnectionOptions(),
        serverTimeout: TimeInterval = 30,
        keepAliveInterval: TimeInterval = 15
    ) async -> HARRRConnection {
        let connection = HARRRConnection(
            hubConnection: hubConnection,
            accessTokenFactory: accessTokenFactory,
            options: options,
            serverTimeout: serverTimeout,
            keepAliveInterval: keepAliveInterval
        )
        await connection.registerBuiltInHandlers()
        return connection
    }

    // MARK: - Lifecycle

    /// Start the underlying SignalR connection.
    public func start() async throws {
        try await hubConnection.start()
    }

    /// Stop the underlying SignalR connection.
    public func stop() async {
        await hubConnection.stop()
    }

    // MARK: - Client → Server RPC (Feature 4: Generic Arguments)

    /// Invoke a server method and return the result.
    public func invoke<T>(_ method: String, arguments: Any..., genericArguments: [String] = []) async throws -> T {
        let msg = await buildClientRequest(method: method, arguments: arguments, genericArguments: genericArguments)
        return try await hubConnection.invoke(
            method: MethodNames.invokeMessageResultOnServer,
            arguments: msg
        )
    }

    /// Send a fire-and-forget message to the server.
    public func send(_ method: String, arguments: Any..., genericArguments: [String] = []) async throws {
        let msg = await buildClientRequest(method: method, arguments: arguments, genericArguments: genericArguments)
        try await hubConnection.send(
            method: MethodNames.invokeMessageOnServer,
            arguments: msg
        )
    }

    /// Open a server-to-client stream.
    public func stream<T>(_ method: String, arguments: Any..., genericArguments: [String] = []) async throws -> AsyncThrowingStream<T, Error> {
        let msg = await buildClientRequest(method: method, arguments: arguments, genericArguments: genericArguments)
        let result: any StreamResult<T> = try await hubConnection.stream(
            method: MethodNames.streamMessageFromServer,
            arguments: msg
        )
        return result.stream
    }

    // MARK: - Server → Client Handlers

    /// Register a handler for server-to-client RPC methods.
    ///
    /// The handler receives an array of arguments and should return a result
    /// (or `AnyCodable(NSNull())` for void methods).
    public func onServerMethod(
        _ name: String,
        handler: @escaping @Sendable ([Any]) async throws -> AnyCodable
    ) async {
        await serverRequestHandlers.register(name: name, handler: handler)
    }

    /// Register a streaming handler for server-to-client RPC methods.
    ///
    /// When the server sends a request with a `streamId`, the handler is called
    /// and each item from the returned stream is sent back to the server.
    public func onServerStreamMethod(
        _ name: String,
        handler: @escaping @Sendable ([Any]) async throws -> AsyncThrowingStream<AnyCodable, Error>
    ) async {
        await serverRequestHandlers.registerStream(name: name, handler: handler)
    }

    /// Remove a registered server method handler.
    public func removeServerMethod(_ name: String) async {
        await serverRequestHandlers.remove(name: name)
    }

    // MARK: - Feature 5: On/Off (Raw SignalR Events)

    /// Register a raw SignalR handler for a method (0 params, void return).
    public func on(_ methodName: String, handler: @escaping () async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (1 param, void return).
    public func on<T>(_ methodName: String, handler: @escaping (T) async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (2 params, void return).
    public func on<T1, T2>(_ methodName: String, handler: @escaping (T1, T2) async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (3 params, void return).
    public func on<T1, T2, T3>(_ methodName: String, handler: @escaping (T1, T2, T3) async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (4 params, void return).
    public func on<T1, T2, T3, T4>(_ methodName: String, handler: @escaping (T1, T2, T3, T4) async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (5 params, void return).
    public func on<T1, T2, T3, T4, T5>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5) async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (6 params, void return).
    public func on<T1, T2, T3, T4, T5, T6>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6) async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (7 params, void return).
    public func on<T1, T2, T3, T4, T5, T6, T7>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6, T7) async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (8 params, void return).
    public func on<T1, T2, T3, T4, T5, T6, T7, T8>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6, T7, T8) async -> Void) async {
        await hubConnection.on(methodName, handler: handler)
    }

    // MARK: On with Result

    /// Register a raw SignalR handler for a method (0 params, with result).
    public func on<Result>(_ methodName: String, handler: @escaping () async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (1 param, with result).
    public func on<T, Result>(_ methodName: String, handler: @escaping (T) async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (2 params, with result).
    public func on<T1, T2, Result>(_ methodName: String, handler: @escaping (T1, T2) async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (3 params, with result).
    public func on<T1, T2, T3, Result>(_ methodName: String, handler: @escaping (T1, T2, T3) async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (4 params, with result).
    public func on<T1, T2, T3, T4, Result>(_ methodName: String, handler: @escaping (T1, T2, T3, T4) async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (5 params, with result).
    public func on<T1, T2, T3, T4, T5, Result>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5) async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (6 params, with result).
    public func on<T1, T2, T3, T4, T5, T6, Result>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6) async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (7 params, with result).
    public func on<T1, T2, T3, T4, T5, T6, T7, Result>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6, T7) async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Register a raw SignalR handler for a method (8 params, with result).
    public func on<T1, T2, T3, T4, T5, T6, T7, T8, Result>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6, T7, T8) async -> Result) async {
        await hubConnection.on(methodName, handler: handler)
    }

    /// Remove all raw SignalR handlers for a method.
    public func off(_ methodName: String) async {
        await hubConnection.off(method: methodName)
    }

    // MARK: - Feature 7: Interface Registration

    /// Register a dictionary of handlers under a shared prefix.
    ///
    /// Each handler is registered as `"prefix|methodName"`, matching the .NET
    /// `"TypeName|MethodName"` wire format.
    public func registerHandlers(
        prefix: String,
        handlers: [String: @Sendable ([Any]) async throws -> AnyCodable]
    ) async {
        for (name, handler) in handlers {
            await serverRequestHandlers.register(name: "\(prefix)|\(name)", handler: handler)
        }
    }

    /// Register a `ServerInterfaceHandler` implementation.
    ///
    /// This is the structured alternative to `registerHandlers(prefix:handlers:)`.
    public func registerInterface(_ handler: ServerInterfaceHandler) async {
        let prefix = type(of: handler).interfaceName
        let handlerMap = handler.handlers()
        for (name, fn) in handlerMap {
            await serverRequestHandlers.register(name: "\(prefix)|\(name)", handler: fn)
        }
    }

    // MARK: - Typed Proxy

    /// Create a typed proxy instance for the given `HubProxyProtocol` type.
    ///
    /// Works with classes generated by the `@HubProxy` macro.
    public func getTypedMethods<T: HubProxyProtocol>(_ type: T.Type) -> T {
        T(connection: self)
    }

    // MARK: - Connection Events

    /// Register a callback invoked when the connection closes.
    public func onClosed(_ callback: @escaping @Sendable (Error?) async -> Void) async {
        await hubConnection.onClosed(handler: callback)
    }

    /// Register a callback invoked when the connection starts reconnecting.
    public func onReconnecting(_ callback: @escaping @Sendable (Error?) async -> Void) async {
        await hubConnection.onReconnecting(handler: callback)
    }

    /// Register a callback invoked when the connection successfully reconnects.
    public func onReconnected(_ callback: @escaping @Sendable () async -> Void) async {
        await hubConnection.onReconnected(handler: callback)
    }

    // MARK: - Escape Hatch

    /// Access the underlying SignalR `HubConnection` directly.
    public func asSignalRHubConnection() -> HubConnection {
        hubConnection
    }

    // MARK: - Private

    private func buildClientRequest(method: String, arguments: [Any], genericArguments: [String] = []) async -> ClientRequestMessage {
        let token = await accessTokenFactory()
        let encoded = arguments.map { AnyCodable($0) }
        return ClientRequestMessage(
            method: method,
            arguments: encoded,
            authorization: token,
            genericArguments: genericArguments
        )
    }
}

// MARK: - Server Interface Handler Protocol

/// Protocol for structured server interface registration.
///
/// Implement this protocol to provide a named set of server method handlers
/// that can be registered with `HARRRConnection.registerInterface(_:)`.
public protocol ServerInterfaceHandler {
    /// The interface name used as the prefix in the `"TypeName|MethodName"` wire format.
    static var interfaceName: String { get }

    /// Return a dictionary of method name → handler mappings.
    func handlers() -> [String: @Sendable ([Any]) async throws -> AnyCodable]
}

// MARK: - Server Request Handler Store

/// Actor-isolated storage for server request handlers.
private actor ServerRequestHandlerStore {
    private var handlers: [String: @Sendable ([Any]) async throws -> AnyCodable] = [:]
    private var streamHandlers: [String: @Sendable ([Any]) async throws -> AsyncThrowingStream<AnyCodable, Error>] = [:]

    func register(name: String, handler: @escaping @Sendable ([Any]) async throws -> AnyCodable) {
        handlers[name] = handler
    }

    func registerStream(name: String, handler: @escaping @Sendable ([Any]) async throws -> AsyncThrowingStream<AnyCodable, Error>) {
        streamHandlers[name] = handler
    }

    func handler(for name: String) -> (@Sendable ([Any]) async throws -> AnyCodable)? {
        handlers[name]
    }

    func streamHandler(for name: String) -> (@Sendable ([Any]) async throws -> AsyncThrowingStream<AnyCodable, Error>)? {
        streamHandlers[name]
    }

    func remove(name: String) {
        handlers.removeValue(forKey: name)
        streamHandlers.removeValue(forKey: name)
    }
}
