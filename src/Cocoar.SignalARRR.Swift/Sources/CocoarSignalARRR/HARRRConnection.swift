import Foundation

/// SignalARRR client built on a custom `SignalRWebSocketClient`.
///
/// Provides typed bidirectional RPC: `invoke`, `send`, `stream` for client-to-server
/// calls, and `onServerMethod` for server-to-client calls. Handles the SignalARRR wire
/// protocol (authentication challenges, server request dispatch, cancellation propagation).
public final class HARRRConnection: @unchecked Sendable {
    private let client: SignalRWebSocketClient
    private let accessTokenFactory: @Sendable () async -> String
    let cancellationManager = CancellationManager()
    private let serverRequestHandlers = ServerRequestHandlerStore()
    private let options: HARRRConnectionOptions

    // MARK: - Feature 1: Connection State

    /// The current state of the underlying SignalR connection.
    public var state: HubConnectionState {
        get async { client.state() }
    }

    // MARK: - Feature 2: ConnectionId

    /// The connection ID reported by the server.
    public private(set) var connectionId: String?

    // MARK: - Feature 3: Timeout Configuration

    public let serverTimeoutInterval: TimeInterval
    public let keepAliveIntervalValue: TimeInterval

    /// The handshake timeout interval (in seconds) configured for this connection.
    public let handshakeTimeoutValue: TimeInterval

    // MARK: - Feature 6: OnServerRequestMessage Callback

    public var onServerRequestMessageReceived: (@Sendable (ServerRequestMessage) -> Void)?

    // MARK: - Initialization

    private init(
        client: SignalRWebSocketClient,
        accessTokenFactory: @escaping @Sendable () async -> String,
        options: HARRRConnectionOptions,
        serverTimeout: TimeInterval,
        keepAliveInterval: TimeInterval,
        handshakeTimeout: TimeInterval
    ) {
        self.client = client
        self.accessTokenFactory = accessTokenFactory
        self.options = options
        self.serverTimeoutInterval = serverTimeout
        self.keepAliveIntervalValue = keepAliveInterval
        self.handshakeTimeoutValue = handshakeTimeout
    }

    /// Register the built-in SignalARRR protocol handlers on the client.
    private func registerBuiltInHandlers() {
        // Authentication challenge — returns token directly
        client.on(MethodNames.challengeAuthentication) { [weak self] args in
            guard let self else { return nil }
            let token = await self.accessTokenFactory()
            return token
        }

        // Server request (expects a reply) — returns result directly
        client.on(MethodNames.invokeServerRequest) { [weak self] args in
            guard let self else { return nil }
            let req = try self.decodeServerRequest(from: args)
            self.onServerRequestMessageReceived?(req)

            do {
                let result = try await self.dispatchServerMethod(req)

                // If the result contains Data, upload via HTTP and return StreamReference
                if let data = result.value as? Data {
                    let ref = try await self.uploadAndReturnReference(data)
                    return ref
                }

                return result.value
            } catch {
                return nil
            }
        }

        // Server message (fire-and-forget) — also handles streaming (StreamId)
        client.on(MethodNames.invokeServerMessage) { [weak self] args in
            guard let self else { return nil }
            let req = try self.decodeServerRequest(from: args)
            self.onServerRequestMessageReceived?(req)

            // Feature 8: If streamId is present, route to stream handling in background.
            if let streamId = req.streamId {
                Task { [self] in
                    await self.handleStreamBackToServer(req: req, streamId: streamId)
                }
                return nil
            }

            do {
                _ = try await self.dispatchServerMethod(req)
            } catch {
                print("[SignalARRR] Failed to handle server message '\(req.method)': \(error)")
            }
            return nil
        }

        // Cancellation from server
        client.on(MethodNames.cancelTokenFromServer) { [weak self] args in
            guard let self else { return nil }
            let req = try self.decodeServerRequest(from: args)
            if let guid = req.cancellationGuid {
                await self.cancellationManager.cancel(id: guid)
            }
            return nil
        }
    }

    // MARK: - Dispatch

    private func dispatchServerMethod(_ req: ServerRequestMessage) async throws -> AnyCodable {
        guard let handler = await serverRequestHandlers.handler(for: req.method) else {
            return AnyCodable(Optional<String>.none as Any)
        }

        let args = try await buildHandlerArgs(req)
        return try await handler(args)
    }

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

    // MARK: - Upload (Client → Server File Transfer)

    private func uploadAndReturnReference(_ data: Data) async throws -> Any {
        let uploadUrl: String = try await client.invoke(method: "RequestUploadSlot", arguments: [])

        guard let url = URL(string: uploadUrl) else {
            throw StreamReferenceError.downloadFailed("Invalid upload URL: \(uploadUrl)")
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/octet-stream", forHTTPHeaderField: "Content-Type")
        request.httpBody = data
        let (_, response) = try await URLSession.shared.data(for: request)

        guard let httpResponse = response as? HTTPURLResponse, httpResponse.statusCode == 200 else {
            throw StreamReferenceError.downloadFailed("Upload failed")
        }

        return ["Uri": uploadUrl] as [String: Any]
    }

    // MARK: - Feature 8: Client-to-Server Streaming

    private func handleStreamBackToServer(req: ServerRequestMessage, streamId: String) async {
        if let streamHandler = await serverRequestHandlers.streamHandler(for: req.method) {
            do {
                let args = try await buildHandlerArgs(req)
                let stream = try await streamHandler(args)
                for try await item in stream {
                    try? await client.send(
                        method: MethodNames.streamItemToServer,
                        arguments: [streamId, item.value]
                    )
                }
                try? await client.send(
                    method: MethodNames.streamCompleteToServer,
                    arguments: [streamId, NSNull()]
                )
            } catch {
                try? await client.send(
                    method: MethodNames.streamCompleteToServer,
                    arguments: [streamId, String(describing: error)]
                )
            }
            return
        }

        if let handler = await serverRequestHandlers.handler(for: req.method) {
            do {
                let args = try await buildHandlerArgs(req)
                let result = try await handler(args)
                try? await client.send(
                    method: MethodNames.streamItemToServer,
                    arguments: [streamId, result.value]
                )
                try? await client.send(
                    method: MethodNames.streamCompleteToServer,
                    arguments: [streamId, NSNull()]
                )
            } catch {
                try? await client.send(
                    method: MethodNames.streamCompleteToServer,
                    arguments: [streamId, String(describing: error)]
                )
            }
            return
        }

        try? await client.send(
            method: MethodNames.streamCompleteToServer,
            arguments: [streamId, NSNull()]
        )
    }

    // MARK: - Factory Methods

    /// Create a connection with a hub URL.
    public static func create(
        url: String,
        hubProtocol: HubProtocolKind = .json,
        accessTokenFactory: @escaping @Sendable () async -> String = { "" },
        options: HARRRConnectionOptions = HARRRConnectionOptions(),
        serverTimeout: TimeInterval = 30,
        keepAliveInterval: TimeInterval = 15,
        handshakeTimeout: TimeInterval = 15,
        reconnectPolicy: ReconnectPolicy = .default,
        allowedTransports: [TransportType] = [.webSockets, .serverSentEvents, .longPolling],
        logLevel: SignalRLogLevel = .info
    ) async -> HARRRConnection {
        let client = SignalRWebSocketClient(
            url: url,
            hubProtocol: hubProtocol,
            serverTimeout: serverTimeout,
            keepAliveInterval: keepAliveInterval,
            handshakeTimeout: handshakeTimeout,
            reconnectPolicy: reconnectPolicy,
            allowedTransports: allowedTransports,
            logLevel: logLevel
        )
        let connection = HARRRConnection(
            client: client,
            accessTokenFactory: accessTokenFactory,
            options: options,
            serverTimeout: serverTimeout,
            keepAliveInterval: keepAliveInterval,
            handshakeTimeout: handshakeTimeout
        )
        connection.registerBuiltInHandlers()
        return connection
    }

    /// Create a connection wrapping an existing `SignalRWebSocketClient`.
    public static func create(
        client: SignalRWebSocketClient,
        accessTokenFactory: @escaping @Sendable () async -> String = { "" },
        options: HARRRConnectionOptions = HARRRConnectionOptions(),
        serverTimeout: TimeInterval = 30,
        keepAliveInterval: TimeInterval = 15,
        handshakeTimeout: TimeInterval = 15
    ) async -> HARRRConnection {
        let connection = HARRRConnection(
            client: client,
            accessTokenFactory: accessTokenFactory,
            options: options,
            serverTimeout: serverTimeout,
            keepAliveInterval: keepAliveInterval,
            handshakeTimeout: handshakeTimeout
        )
        connection.registerBuiltInHandlers()
        return connection
    }

    // MARK: - Lifecycle

    public func start() async throws {
        try await client.start()
    }

    public func stop() async {
        await client.stop()
    }

    // MARK: - Client → Server RPC (Feature 4: Generic Arguments)

    public func invoke<T: Decodable>(_ method: String, arguments: Any..., genericArguments: [String] = []) async throws -> T {
        let msg = await buildClientRequest(method: method, arguments: arguments, genericArguments: genericArguments)
        return try await client.invoke(
            method: MethodNames.invokeMessageResultOnServer,
            arguments: [msg]
        )
    }

    public func send(_ method: String, arguments: Any..., genericArguments: [String] = []) async throws {
        let msg = await buildClientRequest(method: method, arguments: arguments, genericArguments: genericArguments)
        try await client.send(
            method: MethodNames.sendMessageToServer,
            arguments: [msg]
        )
    }

    public func stream<T: Decodable>(_ method: String, arguments: Any..., genericArguments: [String] = []) async throws -> AsyncThrowingStream<T, Error> {
        let msg = await buildClientRequest(method: method, arguments: arguments, genericArguments: genericArguments)
        return try await client.stream(
            method: MethodNames.streamMessageFromServer,
            arguments: [msg]
        )
    }

    // MARK: - Server → Client Handlers

    public func onServerMethod(
        _ name: String,
        handler: @escaping @Sendable ([Any]) async throws -> AnyCodable
    ) async {
        await serverRequestHandlers.register(name: name, handler: handler)
    }

    public func onServerStreamMethod(
        _ name: String,
        handler: @escaping @Sendable ([Any]) async throws -> AsyncThrowingStream<AnyCodable, Error>
    ) async {
        await serverRequestHandlers.registerStream(name: name, handler: handler)
    }

    public func removeServerMethod(_ name: String) async {
        await serverRequestHandlers.remove(name: name)
    }

    // MARK: - Feature 5: On/Off (Raw SignalR Events)

    // Void return, 0–8 params

    public func on(_ methodName: String, handler: @escaping () async -> Void) async {
        client.on(methodName) { _ in await handler(); return nil }
    }

    public func on<T1: Decodable>(_ methodName: String, handler: @escaping (T1) async -> Void) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            await handler(v1)
            return nil
        }
    }

    public func on<T1: Decodable, T2: Decodable>(_ methodName: String, handler: @escaping (T1, T2) async -> Void) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            await handler(v1, v2)
            return nil
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable>(_ methodName: String, handler: @escaping (T1, T2, T3) async -> Void) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            await handler(v1, v2, v3)
            return nil
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4) async -> Void) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            await handler(v1, v2, v3, v4)
            return nil
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, T5: Decodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5) async -> Void) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let v5: T5 = try decodeArgument(args, at: 4)
            await handler(v1, v2, v3, v4, v5)
            return nil
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, T5: Decodable, T6: Decodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6) async -> Void) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let v5: T5 = try decodeArgument(args, at: 4)
            let v6: T6 = try decodeArgument(args, at: 5)
            await handler(v1, v2, v3, v4, v5, v6)
            return nil
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, T5: Decodable, T6: Decodable, T7: Decodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6, T7) async -> Void) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let v5: T5 = try decodeArgument(args, at: 4)
            let v6: T6 = try decodeArgument(args, at: 5)
            let v7: T7 = try decodeArgument(args, at: 6)
            await handler(v1, v2, v3, v4, v5, v6, v7)
            return nil
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, T5: Decodable, T6: Decodable, T7: Decodable, T8: Decodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6, T7, T8) async -> Void) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let v5: T5 = try decodeArgument(args, at: 4)
            let v6: T6 = try decodeArgument(args, at: 5)
            let v7: T7 = try decodeArgument(args, at: 6)
            let v8: T8 = try decodeArgument(args, at: 7)
            await handler(v1, v2, v3, v4, v5, v6, v7, v8)
            return nil
        }
    }

    // With Result return, 0–8 params

    public func on<Result: Encodable>(_ methodName: String, handler: @escaping () async -> Result) async {
        client.on(methodName) { _ in
            let result = await handler()
            return try encodeResult(result)
        }
    }

    public func on<T1: Decodable, Result: Encodable>(_ methodName: String, handler: @escaping (T1) async -> Result) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let result = await handler(v1)
            return try encodeResult(result)
        }
    }

    public func on<T1: Decodable, T2: Decodable, Result: Encodable>(_ methodName: String, handler: @escaping (T1, T2) async -> Result) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let result = await handler(v1, v2)
            return try encodeResult(result)
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, Result: Encodable>(_ methodName: String, handler: @escaping (T1, T2, T3) async -> Result) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let result = await handler(v1, v2, v3)
            return try encodeResult(result)
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, Result: Encodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4) async -> Result) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let result = await handler(v1, v2, v3, v4)
            return try encodeResult(result)
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, T5: Decodable, Result: Encodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5) async -> Result) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let v5: T5 = try decodeArgument(args, at: 4)
            let result = await handler(v1, v2, v3, v4, v5)
            return try encodeResult(result)
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, T5: Decodable, T6: Decodable, Result: Encodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6) async -> Result) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let v5: T5 = try decodeArgument(args, at: 4)
            let v6: T6 = try decodeArgument(args, at: 5)
            let result = await handler(v1, v2, v3, v4, v5, v6)
            return try encodeResult(result)
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, T5: Decodable, T6: Decodable, T7: Decodable, Result: Encodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6, T7) async -> Result) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let v5: T5 = try decodeArgument(args, at: 4)
            let v6: T6 = try decodeArgument(args, at: 5)
            let v7: T7 = try decodeArgument(args, at: 6)
            let result = await handler(v1, v2, v3, v4, v5, v6, v7)
            return try encodeResult(result)
        }
    }

    public func on<T1: Decodable, T2: Decodable, T3: Decodable, T4: Decodable, T5: Decodable, T6: Decodable, T7: Decodable, T8: Decodable, Result: Encodable>(_ methodName: String, handler: @escaping (T1, T2, T3, T4, T5, T6, T7, T8) async -> Result) async {
        client.on(methodName) { args in
            let v1: T1 = try decodeArgument(args, at: 0)
            let v2: T2 = try decodeArgument(args, at: 1)
            let v3: T3 = try decodeArgument(args, at: 2)
            let v4: T4 = try decodeArgument(args, at: 3)
            let v5: T5 = try decodeArgument(args, at: 4)
            let v6: T6 = try decodeArgument(args, at: 5)
            let v7: T7 = try decodeArgument(args, at: 6)
            let v8: T8 = try decodeArgument(args, at: 7)
            let result = await handler(v1, v2, v3, v4, v5, v6, v7, v8)
            return try encodeResult(result)
        }
    }

    /// Remove all raw SignalR handlers for a method.
    public func off(_ methodName: String) async {
        client.off(methodName)
    }

    // MARK: - Feature 7: Interface Registration

    public func registerHandlers(
        prefix: String,
        handlers: [String: @Sendable ([Any]) async throws -> AnyCodable]
    ) async {
        for (name, handler) in handlers {
            await serverRequestHandlers.register(name: "\(prefix)|\(name)", handler: handler)
        }
    }

    public func registerInterface(_ handler: ServerInterfaceHandler) async {
        let prefix = type(of: handler).interfaceName
        let handlerMap = handler.handlers()
        for (name, fn) in handlerMap {
            await serverRequestHandlers.register(name: "\(prefix)|\(name)", handler: fn)
        }
    }

    // MARK: - Typed Proxy

    public func getTypedMethods<T: HubProxyProtocol>(_ type: T.Type) -> T {
        T(connection: self)
    }

    // MARK: - Connection Events

    public func onClosed(_ callback: @escaping @Sendable (Error?) async -> Void) async {
        client.onClosed(callback)
    }

    public func onReconnecting(_ callback: @escaping @Sendable (Error?) async -> Void) async {
        client.onReconnecting(callback)
    }

    public func onReconnected(_ callback: @escaping @Sendable () async -> Void) async {
        client.onReconnected(callback)
    }

    // MARK: - Private Helpers

    /// Decode a `ServerRequestMessage` from raw SignalR invocation arguments.
    private func decodeServerRequest(from args: [Any]) throws -> ServerRequestMessage {
        guard !args.isEmpty else {
            throw SignalRError.invocationFailed("No arguments in server request")
        }
        let data = try JSONSerialization.data(withJSONObject: args[0])
        return try JSONDecoder().decode(ServerRequestMessage.self, from: data)
    }

    private func buildClientRequest(method: String, arguments: [Any], genericArguments: [String] = []) async -> ClientRequestMessage {
        let token = await accessTokenFactory()

        var preparedArgs: [AnyCodable] = []
        for arg in arguments {
            if let data = arg as? Data {
                if let ref = try? await uploadAndReturnReference(data) {
                    preparedArgs.append(AnyCodable(ref))
                } else {
                    preparedArgs.append(AnyCodable(arg))
                }
            } else {
                preparedArgs.append(AnyCodable(arg))
            }
        }

        return ClientRequestMessage(
            method: method,
            arguments: preparedArgs,
            authorization: token,
            genericArguments: genericArguments
        )
    }
}

// MARK: - Server Interface Handler Protocol

public protocol ServerInterfaceHandler {
    static var interfaceName: String { get }
    func handlers() -> [String: @Sendable ([Any]) async throws -> AnyCodable]
}

// MARK: - Server Request Handler Store

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

// MARK: - Argument Decode / Result Encode Helpers

/// Decode a single argument from a JSONSerialization-parsed array at the given index.
private func decodeArgument<T: Decodable>(_ args: [Any], at index: Int) throws -> T {
    guard index < args.count else {
        throw SignalRError.invocationFailed("Missing argument at index \(index)")
    }
    // Wrap in array so JSONSerialization can handle primitives
    let data = try JSONSerialization.data(withJSONObject: [args[index]])
    return try JSONDecoder().decode(SingleElementArray<T>.self, from: data).value
}

/// Encode a result value to a JSON-serializable `Any` for the wire.
private func encodeResult<R: Encodable>(_ result: R) throws -> Any {
    let data = try JSONEncoder().encode(result)
    return try JSONSerialization.jsonObject(with: data, options: .fragmentsAllowed)
}

/// Helper for decoding a single element from a JSON array.
private struct SingleElementArray<T: Decodable>: Decodable {
    let value: T
    init(from decoder: Decoder) throws {
        var container = try decoder.unkeyedContainer()
        value = try container.decode(T.self)
    }
}
