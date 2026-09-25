import Foundation

/// Structured error envelope for SignalARRR server exceptions.
///
/// The server serializes exceptions as JSON in the `HubException` message string.
/// Use ``parseHARRRError(_:)`` to extract structured error information.
public struct HARRRError: Error, Sendable, Equatable {
    /// The fully-qualified .NET exception type (e.g. `"System.ArgumentException"`), for diagnostics.
    /// Branch on `normalizedCode` instead.
    public let type: String

    /// The error message.
    public let message: String

    /// Optional stack trace (only available when the server is running in DEBUG mode).
    public let stackTrace: String?

    /// The machine-readable code as sent, including application-defined ones (`"room_full"`);
    /// `nil` from an older server or a non-envelope error.
    public let code: String?

    /// Envelope version; 1 and up carry `code`. 0 for a legacy or non-envelope error.
    public let version: Int

    /// `code` folded to one of `HARRRErrorCodes`; unknown or missing codes become `internal`.
    public var normalizedCode: String { HARRRErrorCodes.normalize(code) }

    public init(type: String, message: String, stackTrace: String? = nil, code: String? = nil, version: Int = 0) {
        self.type = type
        self.message = message
        self.stackTrace = stackTrace
        self.code = code
        self.version = version
    }
}

/// The machine-readable error codes of the wire contract — the same set as `HARRRErrorCodes` in the
/// .NET, TypeScript and Kotlin clients. Branch on these: a code is the only cross-language signal, a
/// .NET type name means nothing here. Application code may put its own codes on the wire
/// (`HARRRException("room_full", ...)` on the server); those travel verbatim in `HARRRError.code`.
public enum HARRRErrorCodes {
    /// Authorization rejected the call.
    public static let unauthorized = "unauthorized"
    /// No method (or interface) is registered under the requested name.
    public static let methodNotFound = "method_not_found"
    /// The name exists, but no registered method accepts this argument count.
    public static let invalidArgumentCount = "invalid_argument_count"
    /// An argument could not be deserialized or coerced to the parameter type.
    public static let argumentBindingFailed = "argument_binding_failed"
    /// The call was cancelled — an expected outcome, not a failure.
    public static let cancelled = "cancelled"
    /// A server-side deadline expired (e.g. waiting for a stream upload).
    public static let timeout = "timeout"
    /// No client answered the invoke (locally or across the backplane).
    public static let noClientResponded = "no_client_responded"
    /// The connection already holds as many unused upload slots as it is allowed to.
    public static let uploadSlotLimitReached = "upload_slot_limit_reached"
    /// The invoked method itself threw — the default bucket.
    public static let `internal` = "internal"

    private static let known: Set<String> = [
        unauthorized, methodNotFound, invalidArgumentCount, argumentBindingFailed,
        cancelled, timeout, noClientResponded, uploadSlotLimitReached, `internal`,
    ]

    /// Maps a wire code to one this client knows; unknown or missing codes become `internal`.
    public static func normalize(_ code: String?) -> String {
        if let code, known.contains(code) { return code }
        return `internal`
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

    // Try JSON format: {"Version":1,"Code":"...","Type":"...","Message":"...","StackTrace":"..."}
    if let data = jsonCandidate.data(using: .utf8) {
        struct RawError: Decodable {
            let version: Int?
            let code: String?
            let type: String?
            let message: String?
            let stackTrace: String?
            enum CodingKeys: String, CodingKey {
                case version = "Version"
                case code = "Code"
                case type = "Type"
                case message = "Message"
                case stackTrace = "StackTrace"
            }
        }
        // Accepted when it carries actual error content — a versioned envelope, a code, or a
        // concrete type — the same test as the .NET and Kotlin clients.
        if let parsed = try? JSONDecoder().decode(RawError.self, from: data) {
            let version = parsed.version ?? 0
            let type = parsed.type ?? "Error"
            let hasCode = !(parsed.code ?? "").isEmpty
            if version >= 1 || hasCode || (!type.isEmpty && type != "Error") {
                return HARRRError(
                    type: type.isEmpty ? "Error" : type,
                    message: parsed.message ?? "",
                    stackTrace: parsed.stackTrace,
                    code: parsed.code,
                    version: version
                )
            }
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
