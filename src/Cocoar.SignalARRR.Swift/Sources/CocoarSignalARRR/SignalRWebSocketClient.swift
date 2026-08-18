import Foundation

// MARK: - Public Types

/// Connection state — replaces `SignalRClient.HubConnectionState`.
public enum HubConnectionState: Sendable {
    case disconnected
    case connecting
    case connected
    case reconnecting
}

/// Errors originating from the SignalR transport or protocol layer.
public enum SignalRError: Error, LocalizedError {
    case connectionFailed(String)
    case negotiationFailed(String)
    case handshakeFailed(String)
    case hubError(String)
    case invocationFailed(String)
    case serializationFailed(String)
    case disconnected

    public var errorDescription: String? {
        switch self {
        case .connectionFailed(let m): return "Connection failed: \(m)"
        case .negotiationFailed(let m): return "Negotiation failed: \(m)"
        case .handshakeFailed(let m): return "Handshake failed: \(m)"
        case .hubError(let m): return m
        case .invocationFailed(let m): return "Invocation failed: \(m)"
        case .serializationFailed(let m): return "Serialization failed: \(m)"
        case .disconnected: return "Connection is not active"
        }
    }
}

/// Error thrown when the server returns an error in a CompletionMessage.
/// `"\(error)"` produces the raw error string so `parseHARRRError` can parse it.
public struct HubInvocationError: Error, CustomStringConvertible {
    public let message: String
    public var description: String { message }
}

// MARK: - Decode Helpers

struct CompletionResultEnvelope<T: Decodable>: Decodable { let result: T }
struct StreamItemEnvelope<T: Decodable>: Decodable { let item: T }

// MARK: - Reconnect Policy

/// Configures automatic reconnection behaviour.
public struct ReconnectPolicy: Sendable {
    /// Delay before each retry attempt. The array index corresponds to the attempt number.
    /// After the last entry is exhausted, the connection is considered permanently lost.
    /// An empty array disables reconnection entirely.
    public let retryDelays: [TimeInterval]

    public init(retryDelays: [TimeInterval] = [0, 2, 10, 30]) {
        self.retryDelays = retryDelays
    }

    /// No automatic reconnection.
    public static let disabled = ReconnectPolicy(retryDelays: [])

    /// Default policy: immediate, 2s, 10s, 30s — then give up.
    public static let `default` = ReconnectPolicy()
}

// MARK: - SignalR Client

/// Lean SignalR client supporting WebSocket, SSE, and Long Polling transports.
///
/// Handlers are dispatched in their own `Task`, so the receive loop never blocks.
/// This fixes the concurrency issues in Microsoft's `signalr-client-swift`.
public final class SignalRWebSocketClient: @unchecked Sendable {

    private let url: String
    /// Authenticates the connection itself — the negotiate request and the transport URL.
    ///
    /// This client used to take no credential at all: negotiate went out as a bare request and the
    /// transport URL carried only the connection token, so the connection was always anonymous. A
    /// hub with `[Authorize]` rejected it at `/negotiate` with 401, and it worked only against hubs
    /// that declare authorization on their methods, where SignalARRR's own per-message credential
    /// carries it. That is a different thing from `HARRRConnection.accessTokenFactory`, which
    /// authenticates each message; pass the same factory to both if they are the same credential.
    private let accessTokenFactory: (@Sendable () async -> String)?
    private let serverTimeout: TimeInterval
    private let keepAliveInterval: TimeInterval
    private let handshakeTimeout: TimeInterval
    private let reconnectPolicy: ReconnectPolicy
    private let allowedTransports: [TransportType]
    let logger: SignalRLogger
    private let lock = NSLock()
    private let hubProtocol: any SignalRHubProtocol
    private let hubProtocolKind: HubProtocolKind
    private let tracker = InvocationTracker()

    private var transport: (any SignalRTransport)?
    private var receiveTask: Task<Void, Never>?
    private var pingTask: Task<Void, Never>?
    private var reconnectTask: Task<Void, Never>?
    private var _state: HubConnectionState = .disconnected

    private var handlers: [String: @Sendable ([Any]) async throws -> Any?] = [:]
    private var closedCallbacks: [@Sendable (Error?) async -> Void] = []
    private var reconnectingCallbacks: [@Sendable (Error?) async -> Void] = []
    private var reconnectedCallbacks: [@Sendable () async -> Void] = []

    public init(
        url: String,
        hubProtocol: HubProtocolKind = .json,
        accessTokenFactory: (@Sendable () async -> String)? = nil,
        serverTimeout: TimeInterval = 30,
        keepAliveInterval: TimeInterval = 15,
        handshakeTimeout: TimeInterval = 15,
        reconnectPolicy: ReconnectPolicy = .default,
        allowedTransports: [TransportType] = [.webSockets, .serverSentEvents, .longPolling],
        logLevel: SignalRLogLevel = .info
    ) {
        self.url = url
        self.accessTokenFactory = accessTokenFactory
        self.hubProtocolKind = hubProtocol
        self.hubProtocol = hubProtocol == .messagepack ? MessagePackHubProtocol() : JsonHubProtocol()
        self.serverTimeout = serverTimeout
        self.keepAliveInterval = keepAliveInterval
        self.handshakeTimeout = handshakeTimeout
        self.reconnectPolicy = reconnectPolicy
        self.allowedTransports = allowedTransports
        self.logger = SignalRLogger(level: logLevel)
    }

    // MARK: - State

    public func state() -> HubConnectionState {
        lock.withLock { _state }
    }

    private func setState(_ newState: HubConnectionState) {
        lock.withLock { _state = newState }
    }

    // MARK: - Lifecycle

    public func start() async throws {
        logger.info("Starting connection to \(url)")
        setState(.connecting)
        do {
            try await connectAndHandshake()
            logger.info("Connection started successfully")
        } catch {
            logger.error("Connection failed: \(error)")
            setState(.disconnected)
            throw error
        }
    }

    /// Shared connect sequence: negotiate → transport → handshake → start loops.
    private func connectAndHandshake() async throws {
        // 1. Negotiate — get connectionToken and available transports
        let (connectionToken, serverTransports) = try await negotiate()
        logger.debug("Negotiate succeeded, server transports: \(serverTransports)")

        // 2. Select and open transport
        let (selectedTransport, transportType) = try await selectAndConnect(
            serverTransports: serverTransports,
            connectionToken: connectionToken
        )
        lock.withLock { transport = selectedTransport }

        // 3. Handshake (with timeout)
        let handshakeData = hubProtocol.writeHandshakeRequest()
        try await selectedTransport.send(handshakeData)

        let (hsError, remaining) = try await withHandshakeTimeout {
            let responseData = try await selectedTransport.receive()
            return try self.hubProtocol.parseHandshake(responseData)
        }
        if let hsError { throw SignalRError.handshakeFailed(hsError) }

        // 4. Start loops
        setState(.connected)
        startReceiveLoop()
        // Only send pings for WebSocket — SSE/LongPolling use HTTP keep-alive
        if transportType == .webSockets {
            startPingLoop()
        }

        // Process any messages bundled with the handshake response
        if let remaining = remaining {
            let messages = try hubProtocol.parseMessages(remaining)
            for msg in messages { await handleMessage(msg) }
        }
    }

    public func stop() async {
        logger.info("Stopping connection")
        let wasActive = lock.withLock {
            let was = _state != .disconnected
            _state = .disconnected
            return was
        }

        reconnectTask?.cancel()
        reconnectTask = nil
        receiveTask?.cancel()
        pingTask?.cancel()
        receiveTask = nil
        pingTask = nil

        await transport?.close()
        transport = nil

        tracker.failAll(error: SignalRError.disconnected)

        if wasActive {
            let callbacks = lock.withLock { closedCallbacks }
            for cb in callbacks { await cb(nil) }
        }
    }

    // MARK: - Client → Server

    public func send(method: String, arguments: [Any]) async throws {
        let data = try hubProtocol.writeInvocation(target: method, arguments: arguments, invocationId: nil)
        try await sendRaw(data)
    }

    public func invoke<T: Decodable>(method: String, arguments: [Any]) async throws -> T {
        let id = tracker.nextInvocationId()
        let pending = PendingInvocation()
        tracker.registerInvocation(id: id, pending: pending)

        do {
            let data = try hubProtocol.writeInvocation(target: method, arguments: arguments, invocationId: id)
            try await sendRaw(data)
        } catch {
            tracker.removeInvocation(id: id)
            throw error
        }

        let (rawData, errorMessage) = try await pending.wait()

        if let errorMessage {
            throw HubInvocationError(message: errorMessage)
        }

        guard let rawData else {
            let nullData = Data("null".utf8)
            return try JSONDecoder().decode(T.self, from: nullData)
        }

        return try JSONDecoder().decode(CompletionResultEnvelope<T>.self, from: rawData).result
    }

    public func stream<T: Decodable>(method: String, arguments: [Any]) async throws -> AsyncThrowingStream<T, Error> {
        let id = tracker.nextInvocationId()

        var rawContinuation: AsyncThrowingStream<Data, Error>.Continuation!
        let rawStream = AsyncThrowingStream<Data, Error> { rawContinuation = $0 }
        tracker.registerStream(id: id, continuation: rawContinuation)

        do {
            let data = try hubProtocol.writeStreamInvocation(target: method, arguments: arguments, invocationId: id)
            try await sendRaw(data)
        } catch {
            tracker.removeStream(id: id)
            throw error
        }

        return AsyncThrowingStream<T, Error> { [weak self] continuation in
            // Shadow the var-optional `self` as a `let` before any concurrent boundary.
            // This is required for Swift 6: a `var` captured by [weak self] cannot be
            // referenced from @Sendable closures or Tasks.
            guard let self else { return }

            continuation.onTermination = { @Sendable _ in
                self.tracker.removeStream(id: id)
                if let cancelData = try? self.hubProtocol.writeCancelInvocation(invocationId: id) {
                    Task { [weak self] in try? await self?.sendRaw(cancelData) }
                }
            }
            Task {
                do {
                    for try await itemData in rawStream {
                        let item = try JSONDecoder().decode(StreamItemEnvelope<T>.self, from: itemData)
                        continuation.yield(item.item)
                    }
                    continuation.finish()
                } catch {
                    continuation.finish(throwing: error)
                }
            }
        }
    }

    // MARK: - Server → Client Handlers

    public func on(_ method: String, handler: @escaping @Sendable ([Any]) async throws -> Any?) {
        lock.withLock { handlers[method] = handler }
    }

    public func off(_ method: String) {
        lock.withLock { handlers[method] = nil }
    }

    // MARK: - Connection Events

    public func onClosed(_ callback: @escaping @Sendable (Error?) async -> Void) {
        lock.withLock { closedCallbacks.append(callback) }
    }

    public func onReconnecting(_ callback: @escaping @Sendable (Error?) async -> Void) {
        lock.withLock { reconnectingCallbacks.append(callback) }
    }

    public func onReconnected(_ callback: @escaping @Sendable () async -> Void) {
        lock.withLock { reconnectedCallbacks.append(callback) }
    }

    // MARK: - Private: Networking

    /// Negotiate with the server. Returns (connectionToken, availableTransportNames).
    private func negotiate() async throws -> (String, [String]) {
        // Build the negotiate URL via URLComponents so an existing query string on the hub URL
        // (e.g. ".../hub/sync?user=x") is preserved and "/negotiate" is inserted into the *path*,
        // not concatenated after the query. Plain string concatenation produced a broken URL
        // (".../hub/sync?user=x/negotiate?...") and the server replied HTTP 400.
        guard var components = URLComponents(string: url) else {
            throw SignalRError.negotiationFailed("Invalid URL: \(url)")
        }
        components.path += "/negotiate"
        var queryItems = components.queryItems ?? []
        queryItems.append(URLQueryItem(name: "negotiateVersion", value: "1"))
        components.queryItems = queryItems
        guard let negotiateUrl = components.url else {
            throw SignalRError.negotiationFailed("Invalid URL: \(url)")
        }
        var request = URLRequest(url: negotiateUrl)
        request.httpMethod = "POST"
        // Negotiate is an ordinary HTTP request, so the credential travels as a header here. The
        // transport URL below cannot do that portably and uses the `access_token` query instead,
        // which is the convention SignalR itself uses for WebSocket and SSE.
        if let credential = await accessTokenFactory?(), !credential.isEmpty {
            let value = credential.contains(" ") ? credential : "Bearer \(credential)"
            request.setValue(value, forHTTPHeaderField: "Authorization")
        }
        // Fast-fail instead of hanging on the OS-level connect timeout (~30s). This surfaces
        // unreachable endpoints quickly — notably `localhost` resolving to IPv6 (::1) against an
        // IPv4-only server, where there is no Happy-Eyeballs fallback for the WebSocket upgrade.
        // Prefer an explicit `127.0.0.1` host over `localhost` when the server binds IPv4-only.
        request.timeoutInterval = handshakeTimeout

        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            throw SignalRError.negotiationFailed("HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)")
        }
        guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let token = json["connectionToken"] as? String else {
            throw SignalRError.negotiationFailed("Missing connectionToken in response")
        }

        // Parse available transports
        var transportNames: [String] = []
        if let transports = json["availableTransports"] as? [[String: Any]] {
            for t in transports {
                if let name = t["transport"] as? String {
                    transportNames.append(name)
                }
            }
        }

        return (token, transportNames)
    }

    /// Select the best transport, connect it, and return it.
    private func selectAndConnect(
        serverTransports: [String],
        connectionToken: String
    ) async throws -> (any SignalRTransport, TransportType) {
        let accessToken = await accessTokenFactory?()
        for preferred in allowedTransports {
            if serverTransports.isEmpty || serverTransports.contains(preferred.rawValue) {
                guard let transportURL = TransportFactory.transportURL(
                    base: url, connectionToken: connectionToken, type: preferred, accessToken: accessToken
                ) else { continue }

                guard let transport = TransportFactory.create(
                    for: preferred,
                    useBinaryFrames: hubProtocolKind == .messagepack
                ) else { continue }
                logger.info("Connecting via \(preferred.rawValue)")
                try await transport.connect(url: transportURL)
                logger.info("Transport \(preferred.rawValue) connected")
                return (transport, preferred)
            }
        }
        throw SignalRError.connectionFailed(
            "No compatible transport. Client allows: \(allowedTransports.map(\.rawValue)). "
            + "Server supports: \(serverTransports)"
        )
    }

    /// Run a block with the configured handshake timeout.
    private func withHandshakeTimeout<T>(_ body: @escaping () async throws -> T) async throws -> T {
        try await withThrowingTaskGroup(of: T.self) { group in
            group.addTask { try await body() }
            group.addTask {
                try await Task.sleep(nanoseconds: UInt64(self.handshakeTimeout * 1_000_000_000))
                throw SignalRError.handshakeFailed("Handshake timed out after \(self.handshakeTimeout)s")
            }
            let result = try await group.next()!
            group.cancelAll()
            return result
        }
    }

    private func sendRaw(_ data: Data) async throws {
        guard let transport, state() == .connected || state() == .connecting else {
            throw SignalRError.disconnected
        }
        try await transport.send(data)
    }

    // MARK: - Private: Receive Loop

    private func startReceiveLoop() {
        receiveTask = Task { [weak self] in
            guard let self else { return }
            do {
                while !Task.isCancelled {
                    guard let transport = self.transport else { break }
                    let data = try await transport.receive()
                    let parsed = try self.hubProtocol.parseMessages(data)
                    for msg in parsed {
                        await self.handleMessage(msg)
                    }
                }
            } catch {
                guard !Task.isCancelled else { return }
                await self.handleDisconnect(error: error)
            }
        }
    }

    private func startPingLoop() {
        pingTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: UInt64((self?.keepAliveInterval ?? 15) * 1_000_000_000))
                guard let self, !Task.isCancelled, self.state() == .connected else { break }
                let pingData = self.hubProtocol.writePing()
                try? await self.sendRaw(pingData)
            }
        }
    }

    // MARK: - Private: Message Dispatch

    private func handleMessage(_ message: HubMessage) async {
        switch message {
        case .invocation(let target, let args, let invocationId):
            logger.debug("Received invocation: \(target) (id: \(invocationId ?? "none"))")
            let handler = lock.withLock { handlers[target] }

            if let handler = handler {
                Task { [weak self] in
                    await self?.dispatchHandler(handler, arguments: args, invocationId: invocationId)
                }
            } else if let invocationId = invocationId {
                logger.warning("No handler for '\(target)' (id: \(invocationId)), returning null")
                let data = try? hubProtocol.writeCompletion(invocationId: invocationId, result: nil, error: nil)
                if let data { try? await sendRaw(data) }
            }

        case .completion(let id, let error, let rawData):
            tracker.completeInvocationOrStream(id: id, error: error, rawData: rawData)

        case .streamItem(let id, let rawData):
            tracker.yieldStreamItem(id: id, rawData: rawData)

        case .ping:
            break

        case .close(let error):
            await handleDisconnect(error: error.map { SignalRError.hubError($0) })
        }
    }

    private func dispatchHandler(
        _ handler: @escaping @Sendable ([Any]) async throws -> Any?,
        arguments: [Any],
        invocationId: String?
    ) async {
        do {
            let result = try await handler(arguments)
            if let invocationId = invocationId {
                let data = try hubProtocol.writeCompletion(invocationId: invocationId, result: result, error: nil)
                try await sendRaw(data)
            }
        } catch {
            logger.error("Handler error for invocation \(invocationId ?? "fire-and-forget"): \(error)")
            if let invocationId = invocationId {
                let data = try? hubProtocol.writeCompletion(invocationId: invocationId, result: nil, error: "\(error)")
                if let data { try? await sendRaw(data) }
            }
        }
    }

    // MARK: - Private: Disconnect & Reconnection

    private func handleDisconnect(error: Error?) async {
        if let error {
            logger.warning("Connection lost: \(error)")
        } else {
            logger.info("Connection closed by server")
        }

        let (shouldReconnect, previousState) = lock.withLock {
            let prev = _state
            let should = prev == .connected && !reconnectPolicy.retryDelays.isEmpty
            _state = should ? .reconnecting : .disconnected
            return (should, prev)
        }

        receiveTask?.cancel()
        pingTask?.cancel()
        await transport?.close()
        transport = nil

        if shouldReconnect {
            tracker.failAll(error: error ?? SignalRError.disconnected)

            let rcCallbacks = lock.withLock { reconnectingCallbacks }
            for cb in rcCallbacks { await cb(error) }

            reconnectTask = Task { [weak self] in
                await self?.reconnectLoop(originalError: error)
            }
        } else {
            tracker.failAll(error: error ?? SignalRError.disconnected)

            if previousState == .connected || previousState == .connecting {
                let callbacks = lock.withLock { closedCallbacks }
                for cb in callbacks { await cb(error) }
            }
        }
    }

    private func reconnectLoop(originalError: Error?) async {
        logger.info("Reconnecting (\(reconnectPolicy.retryDelays.count) attempts configured)")

        for (attempt, delay) in reconnectPolicy.retryDelays.enumerated() {
            guard !Task.isCancelled, state() == .reconnecting else { return }

            if delay > 0 {
                logger.debug("Reconnect attempt \(attempt + 1): waiting \(delay)s")
                try? await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000))
                guard !Task.isCancelled, state() == .reconnecting else { return }
            }

            do {
                logger.info("Reconnect attempt \(attempt + 1)/\(reconnectPolicy.retryDelays.count)")
                try await connectAndHandshake()
                logger.info("Reconnected successfully")

                let callbacks = lock.withLock { reconnectedCallbacks }
                for cb in callbacks { await cb() }
                return

            } catch {
                logger.warning("Reconnect attempt \(attempt + 1) failed: \(error)")
                let isLast = attempt == reconnectPolicy.retryDelays.count - 1
                if isLast { break }
            }
        }

        logger.error("All reconnect attempts exhausted, giving up")
        guard !Task.isCancelled else { return }
        setState(.disconnected)

        let callbacks = lock.withLock { closedCallbacks }
        for cb in callbacks { await cb(originalError) }
    }
}

// MARK: - Stream completion routing

extension InvocationTracker {
    func completeInvocationOrStream(id: String, error: String?, rawData: Data?) {
        completeInvocation(id: id, error: error, rawData: rawData)
        finishStream(id: id, error: error)
    }
}
