import Foundation

/// A reference to a remote stream resource, typically a downloadable URI.
///
/// The .NET server serialises this as `{ "Uri": "<url>" }`.
public struct StreamReference: Codable, Sendable {
    public var uri: String

    public init(uri: String) {
        self.uri = uri
    }

    enum CodingKeys: String, CodingKey {
        case uri = "Uri"
    }
}

/// Errors that can occur when resolving a `StreamReference`.
public enum StreamReferenceError: Error, CustomStringConvertible {
    case unsupportedScheme(String)
    case downloadFailed(String)

    public var description: String {
        switch self {
        case .unsupportedScheme(let scheme):
            return "StreamReferenceError: unsupported URI scheme '\(scheme)'"
        case .downloadFailed(let reason):
            return "StreamReferenceError: download failed — \(reason)"
        }
    }
}

/// Resolves `StreamReference` values by downloading their content.
public enum StreamReferenceResolver {
    /// Download the data at the given `StreamReference` URI.
    ///
    /// Supports `http` and `https` schemes.
    public static func resolve(_ ref: StreamReference) async throws -> Data {
        guard let url = URL(string: ref.uri) else {
            throw StreamReferenceError.downloadFailed("invalid URL: \(ref.uri)")
        }
        guard let scheme = url.scheme?.lowercased(),
              scheme == "http" || scheme == "https" else {
            throw StreamReferenceError.unsupportedScheme(url.scheme ?? "nil")
        }
        do {
            let (data, _) = try await URLSession.shared.data(from: url)
            return data
        } catch {
            throw StreamReferenceError.downloadFailed(error.localizedDescription)
        }
    }
}

/// Check whether an `AnyCodable` value represents a `StreamReference`.
///
/// The .NET server serialises the reference as `{ "Uri": "<url>" }`.
public func isStreamReference(_ value: Any) -> StreamReference? {
    guard let dict = value as? [String: Any],
          let uri = dict["Uri"] as? String,
          dict.count == 1 else {
        return nil
    }
    return StreamReference(uri: uri)
}
