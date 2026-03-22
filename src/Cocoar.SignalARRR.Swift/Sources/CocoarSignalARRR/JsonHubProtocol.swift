import Foundation

/// ASCII Record Separator — SignalR message delimiter.
let recordSeparator: UInt8 = 0x1e

// MARK: - Hub Message Types

enum HubMessageType: Int {
    case invocation = 1
    case streamItem = 2
    case completion = 3
    case streamInvocation = 4
    case cancelInvocation = 5
    case ping = 6
    case close = 7
}

/// A parsed SignalR hub protocol message.
enum HubMessage {
    case invocation(target: String, arguments: [Any], invocationId: String?)
    case streamItem(invocationId: String, rawData: Data)
    case completion(invocationId: String, error: String?, rawData: Data?)
    case ping
    case close(error: String?)
}

// MARK: - JSON Hub Protocol

struct JsonHubProtocol {

    // MARK: Writing

    func writeHandshakeRequest() -> Data {
        Data(#"{"protocol":"json","version":1}"#.utf8) + [recordSeparator]
    }

    func writeInvocation(target: String, arguments: [Any], invocationId: String? = nil) throws -> Data {
        var msg: [String: Any] = [
            "type": HubMessageType.invocation.rawValue,
            "target": target,
            "arguments": try arguments.map { try serializeValue($0) }
        ]
        if let id = invocationId { msg["invocationId"] = id }
        return try toWire(msg)
    }

    func writeStreamInvocation(target: String, arguments: [Any], invocationId: String) throws -> Data {
        let msg: [String: Any] = [
            "type": HubMessageType.streamInvocation.rawValue,
            "invocationId": invocationId,
            "target": target,
            "arguments": try arguments.map { try serializeValue($0) }
        ]
        return try toWire(msg)
    }

    func writeCompletion(invocationId: String, result: Any? = nil, error: String? = nil) throws -> Data {
        var msg: [String: Any] = [
            "type": HubMessageType.completion.rawValue,
            "invocationId": invocationId
        ]
        if let error = error {
            msg["error"] = error
        } else if let result = result {
            msg["result"] = try serializeValue(result)
        } else {
            msg["result"] = NSNull()
        }
        return try toWire(msg)
    }

    func writeCancelInvocation(invocationId: String) throws -> Data {
        try toWire([
            "type": HubMessageType.cancelInvocation.rawValue,
            "invocationId": invocationId
        ])
    }

    func writePing() -> Data {
        Data(#"{"type":6}"#.utf8) + [recordSeparator]
    }

    // MARK: Parsing

    /// Parse the handshake response. Returns (error, remainingData).
    func parseHandshake(_ data: Data) throws -> (error: String?, remaining: Data?) {
        guard let sepIndex = data.firstIndex(of: recordSeparator) else {
            throw SignalRError.handshakeFailed("Incomplete handshake response")
        }
        let chunk = data[data.startIndex..<sepIndex]
        let json: [String: Any]
        if chunk.isEmpty {
            json = [:]
        } else {
            json = (try? JSONSerialization.jsonObject(with: Data(chunk))) as? [String: Any] ?? [:]
        }
        let error = json["error"] as? String

        let remainingStart = data.index(after: sepIndex)
        let remaining: Data? = remainingStart < data.endIndex ? Data(data[remainingStart...]) : nil
        return (error, remaining)
    }

    /// Parse one or more messages from wire data (split by record separator).
    func parseMessages(_ data: Data) throws -> [HubMessage] {
        data.split(separator: recordSeparator).compactMap { chunk in
            guard !chunk.isEmpty else { return nil }
            return try? parseMessage(Data(chunk))
        }
    }

    // MARK: Private

    private func parseMessage(_ data: Data) throws -> HubMessage? {
        guard let dict = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let typeRaw = dict["type"] as? Int,
              let type = HubMessageType(rawValue: typeRaw) else {
            return nil
        }
        switch type {
        case .invocation:
            return .invocation(
                target: dict["target"] as? String ?? "",
                arguments: dict["arguments"] as? [Any] ?? [],
                invocationId: dict["invocationId"] as? String
            )
        case .streamItem:
            return .streamItem(
                invocationId: dict["invocationId"] as? String ?? "",
                rawData: data
            )
        case .completion:
            return .completion(
                invocationId: dict["invocationId"] as? String ?? "",
                error: dict["error"] as? String,
                rawData: dict.keys.contains("result") ? data : nil
            )
        case .ping:
            return .ping
        case .close:
            return .close(error: dict["error"] as? String)
        case .streamInvocation, .cancelInvocation:
            return nil
        }
    }

    private func toWire(_ obj: [String: Any]) throws -> Data {
        try JSONSerialization.data(withJSONObject: obj) + [recordSeparator]
    }

    // MARK: Value Serialization

    func serializeValue(_ value: Any) throws -> Any {
        if value is NSNull { return NSNull() }

        // Unwrap Optional wrapped in Any
        let mirror = Mirror(reflecting: value)
        if mirror.displayStyle == .optional {
            guard let child = mirror.children.first else { return NSNull() }
            return try serializeValue(child.value)
        }

        // Primitives — JSONSerialization handles these directly
        if value is String || value is Bool { return value }
        if value is Int || value is Int8 || value is Int16 || value is Int32 || value is Int64 { return value }
        if value is UInt || value is UInt8 || value is UInt16 || value is UInt32 || value is UInt64 { return value }
        if value is Double || value is Float { return value }
        if value is NSNumber { return value }

        // Collections
        if let array = value as? [Any] { return try array.map { try serializeValue($0) } }
        if let dict = value as? [String: Any] { return try dict.mapValues { try serializeValue($0) } }

        // Encodable — encode to JSON then parse back to foundation object
        if let encodable = value as? any Encodable {
            let data = try JSONEncoder().encode(AnyEncodableBox(encodable))
            return try JSONSerialization.jsonObject(with: data, options: .fragmentsAllowed)
        }

        throw SignalRError.serializationFailed("Cannot serialize \(type(of: value))")
    }
}

// MARK: - Type-erased Encodable wrapper

struct AnyEncodableBox: Encodable {
    private let _encode: (Encoder) throws -> Void
    init(_ value: any Encodable) { _encode = value.encode }
    func encode(to encoder: Encoder) throws { try _encode(encoder) }
}
