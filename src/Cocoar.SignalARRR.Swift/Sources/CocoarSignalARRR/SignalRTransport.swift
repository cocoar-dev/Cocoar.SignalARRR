import Foundation

// MARK: - Transport Type

/// Available SignalR transport types, in order of preference.
public enum TransportType: String, Sendable, CaseIterable {
    case webSockets = "WebSockets"
    case serverSentEvents = "ServerSentEvents"
    case longPolling = "LongPolling"
}

// MARK: - Transport Protocol

/// Abstraction over the wire transport (WebSocket, SSE, Long Polling).
protocol SignalRTransport: AnyObject, Sendable {
    /// Open the transport connection.
    func connect(url: URL) async throws
    /// Send data to the server.
    func send(_ data: Data) async throws
    /// Block until the next chunk of data arrives from the server.
    func receive() async throws -> Data
    /// Close the transport.
    func close() async
}

// MARK: - WebSocket Transport

final class WebSocketTransport: SignalRTransport, @unchecked Sendable {
    private var task: URLSessionWebSocketTask?
    private var session: URLSession?
    private let useBinaryFrames: Bool

    init(useBinaryFrames: Bool = false) {
        self.useBinaryFrames = useBinaryFrames
    }

    func connect(url: URL) async throws {
        let session = URLSession(configuration: .default)
        self.session = session
        let task = session.webSocketTask(with: url)
        self.task = task
        task.resume()
    }

    func send(_ data: Data) async throws {
        guard let task else { throw SignalRError.disconnected }
        if useBinaryFrames {
            try await task.send(.data(data))
        } else {
            let text = String(data: data, encoding: .utf8) ?? ""
            try await task.send(.string(text))
        }
    }

    func receive() async throws -> Data {
        guard let task else { throw SignalRError.disconnected }
        let message = try await task.receive()
        switch message {
        case .string(let text): return Data(text.utf8)
        case .data(let data): return data
        @unknown default: throw SignalRError.connectionFailed("Unknown WebSocket frame type")
        }
    }

    func close() async {
        task?.cancel(with: .normalClosure, reason: nil)
        task = nil
        session?.invalidateAndCancel()
        session = nil
    }
}

// MARK: - Server-Sent Events Transport

/// SSE: server→client via streaming GET (`text/event-stream`), client→server via HTTP POST.
@available(macOS 12.0, iOS 15.0, tvOS 15.0, watchOS 8.0, *)
final class SSETransport: SignalRTransport, @unchecked Sendable {
    private var url: URL?
    private var session: URLSession?
    private var byteIterator: URLSession.AsyncBytes.AsyncIterator?

    func connect(url: URL) async throws {
        self.url = url
        let session = URLSession(configuration: .default)
        self.session = session

        var request = URLRequest(url: url)
        request.setValue("text/event-stream", forHTTPHeaderField: "Accept")

        let (bytes, response) = try await session.bytes(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            throw SignalRError.connectionFailed(
                "SSE: HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)")
        }
        byteIterator = bytes.makeAsyncIterator()
    }

    func send(_ data: Data) async throws {
        guard let url else { throw SignalRError.disconnected }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("text/plain;charset=UTF-8", forHTTPHeaderField: "Content-Type")
        request.httpBody = data
        let (_, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            throw SignalRError.connectionFailed(
                "SSE send failed: HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)")
        }
    }

    func receive() async throws -> Data {
        var eventData = ""
        while true {
            guard let line = try await readLine() else { throw SignalRError.disconnected }

            if line.isEmpty {
                // Blank line = end of SSE event
                if !eventData.isEmpty {
                    defer { eventData = "" }
                    return Data(eventData.utf8)
                }
                continue
            }
            if line.hasPrefix("data: ") {
                eventData += String(line.dropFirst(6))
            } else if line.hasPrefix("data:") {
                eventData += String(line.dropFirst(5))
            }
            // Ignore other SSE fields (event:, id:, retry:, comments)
        }
    }

    func close() async {
        session?.invalidateAndCancel()
        session = nil
        byteIterator = nil
    }

    // MARK: Private

    /// Read one line from the byte stream (up to `\n`).
    private func readLine() async throws -> String? {
        var buffer: [UInt8] = []
        while let byte = try await byteIterator?.next() {
            if byte == 0x0A { // \n
                return String(bytes: buffer, encoding: .utf8) ?? ""
            }
            if byte != 0x0D { // skip \r
                buffer.append(byte)
            }
        }
        return buffer.isEmpty ? nil : (String(bytes: buffer, encoding: .utf8) ?? "")
    }
}

// MARK: - Long Polling Transport

/// Long Polling: client→server via HTTP POST, server→client via repeated HTTP GET.
/// The server holds each GET until data is available or a timeout occurs.
final class LongPollingTransport: SignalRTransport, @unchecked Sendable {
    private var url: URL?
    private var active = true
    private var pollSession: URLSession?

    func connect(url: URL) async throws {
        self.url = url
        self.active = true
        self.pollSession = URLSession(configuration: .default)
    }

    func send(_ data: Data) async throws {
        guard let url, active else { throw SignalRError.disconnected }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("text/plain;charset=UTF-8", forHTTPHeaderField: "Content-Type")
        request.httpBody = data
        let (_, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            throw SignalRError.connectionFailed(
                "Long polling send failed: HTTP \((response as? HTTPURLResponse)?.statusCode ?? 0)")
        }
    }

    func receive() async throws -> Data {
        guard let url, let session = pollSession, active else { throw SignalRError.disconnected }
        let (data, response) = try await session.data(from: url)
        guard let http = response as? HTTPURLResponse else { throw SignalRError.disconnected }

        if http.statusCode == 204 {
            // 204 No Content = server closed the connection
            active = false
            throw SignalRError.disconnected
        }
        guard http.statusCode == 200 else {
            throw SignalRError.connectionFailed("Long polling: HTTP \(http.statusCode)")
        }
        return data
    }

    func close() async {
        active = false
        // DELETE signals the server to release the connection
        if let url {
            var request = URLRequest(url: url)
            request.httpMethod = "DELETE"
            _ = try? await URLSession.shared.data(for: request)
        }
        pollSession?.invalidateAndCancel()
        pollSession = nil
        url = nil
    }
}

// MARK: - Factory

enum TransportFactory {
    static func create(for type: TransportType, useBinaryFrames: Bool = false) -> (any SignalRTransport)? {
        switch type {
        case .webSockets:
            return WebSocketTransport(useBinaryFrames: useBinaryFrames)
        case .serverSentEvents:
            if #available(macOS 12.0, iOS 15.0, tvOS 15.0, watchOS 8.0, *) {
                return SSETransport()
            }
            return nil
        case .longPolling:
            return LongPollingTransport()
        }
    }

    /// Build the transport URL from the base hub URL and connection token.
    /// - Parameter accessToken: travels as the `access_token` query item, the convention SignalR
    ///   uses for WebSocket and SSE because neither can carry a header portably. The server side of
    ///   that convention is `UseSignalARRRAccessTokenValidation`, or JwtBearer's `OnMessageReceived`.
    static func transportURL(
        base: String, connectionToken: String, type: TransportType, accessToken: String? = nil
    ) -> URL? {
        // Build via URLComponents so an existing query string on the hub URL is preserved and the
        // `id` connection token is appended as a proper query item (string concatenation produced
        // ".../hub?user=x?id=..." for a hub URL that already carried a query). URLComponents also
        // percent-encodes the token value for us.
        guard var components = URLComponents(string: base) else { return nil }
        if type == .webSockets {
            if components.scheme == "http" { components.scheme = "ws" }
            else if components.scheme == "https" { components.scheme = "wss" }
        }
        var queryItems = components.queryItems ?? []
        queryItems.append(URLQueryItem(name: "id", value: connectionToken))
        if let accessToken, !accessToken.isEmpty {
            queryItems.append(URLQueryItem(name: "access_token", value: accessToken))
        }
        components.queryItems = queryItems
        return components.url
    }
}
