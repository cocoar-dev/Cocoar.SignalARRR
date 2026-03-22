import Foundation

/// Structured error envelope for SignalARRR server exceptions.
///
/// The server serializes exceptions as JSON in the `HubException` message string.
/// Use ``parseHARRRError(_:)`` to extract structured error information.
public struct HARRRError: Error, Sendable, Equatable {
    /// The fully-qualified .NET exception type (e.g. `"System.ArgumentException"`).
    public let type: String

    /// The error message.
    public let message: String

    /// Optional stack trace (only available when the server is running in DEBUG mode).
    public let stackTrace: String?

    public init(type: String, message: String, stackTrace: String? = nil) {
        self.type = type
        self.message = message
        self.stackTrace = stackTrace
    }
}

extension HARRRError: LocalizedError {
    public var errorDescription: String? { "[\(type)] \(message)" }
}

/// Parse an error (typically from a SignalR `HubException`) into a structured ``HARRRError``.
///
/// Supports both the JSON format and the legacy `[Type] Message` format.
///
/// SignalR wraps `HubException` messages with prefix text like:
/// `"An unexpected error occurred invoking '...' on the server. HARRRException: {json}"`
public func parseHARRRError(_ error: Error) -> HARRRError {
    return parseHARRRError(fromMessage: error.localizedDescription)
}

/// Parse a raw error message string into a structured ``HARRRError``.
public func parseHARRRError(fromMessage message: String) -> HARRRError {
    // Extract JSON after "HARRRException: " marker (SignalR wrapping)
    let marker = "HARRRException: "
    let jsonCandidate: String
    if let range = message.range(of: marker) {
        jsonCandidate = String(message[range.upperBound...])
    } else {
        jsonCandidate = message
    }

    // Try JSON format: {"Type":"...","Message":"...","StackTrace":"..."}
    if let data = jsonCandidate.data(using: .utf8) {
        struct RawError: Decodable {
            let type: String
            let message: String
            let stackTrace: String?
            enum CodingKeys: String, CodingKey {
                case type = "Type"
                case message = "Message"
                case stackTrace = "StackTrace"
            }
        }
        if let parsed = try? JSONDecoder().decode(RawError.self, from: data),
           !parsed.type.isEmpty, parsed.type != "Error" {
            return HARRRError(type: parsed.type, message: parsed.message, stackTrace: parsed.stackTrace)
        }
    }

    // Legacy format: [Type] Message
    let pattern = #"\[([\w.]+)\]\s*(.*)"#
    if let regex = try? NSRegularExpression(pattern: pattern),
       let match = regex.firstMatch(in: message, range: NSRange(message.startIndex..., in: message)),
       match.numberOfRanges >= 3,
       let typeRange = Range(match.range(at: 1), in: message),
       let msgRange = Range(match.range(at: 2), in: message) {
        return HARRRError(type: String(message[typeRange]), message: String(message[msgRange]))
    }

    // Fallback
    return HARRRError(type: "Error", message: message)
}

/// Thrown when the server rejects authentication (challenge failed or token invalid).
public struct UnauthorizedException: Error, LocalizedError, Sendable {
    public let message: String

    public init(_ message: String = "Unauthorized") {
        self.message = message
    }

    public var errorDescription: String? { message }
}
