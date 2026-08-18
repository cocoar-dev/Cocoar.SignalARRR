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

    /// Builds the request for a file-transfer URL, carrying the connection's credential.
    ///
    /// `/download/{id}` and `/upload/{id}` are ordinary HTTP endpoints: they carry the hub's
    /// authorization requirements but not its connection, so nothing authenticates them unless the
    /// request does. A bare request meant a hub with `[Authorize]` answered 401 to every stream
    /// argument and every stream return value.
    ///
    /// The `Bearer` convention matches the server's: a credential without a space is a bearer token,
    /// one with a space carries its own scheme.
    public static func authorizedRequest(url: URL, authorization: String?) -> URLRequest {
        var request = URLRequest(url: url)
        guard let credential = authorization, !credential.isEmpty else {
            return request
        }
        let value = credential.contains(" ") ? credential : "Bearer \(credential)"
        request.setValue(value, forHTTPHeaderField: "Authorization")
        return request
    }

    /// Download the full content buffered in memory.
    public static func resolve(_ ref: StreamReference, authorization: String? = nil) async throws -> Data {
        let url = try validatedURL(ref)
        do {
            let (data, response) = try await URLSession.shared.data(
                for: authorizedRequest(url: url, authorization: authorization))
            try check(response)
            return data
        } catch let error as StreamReferenceError {
            throw error
        } catch {
            throw StreamReferenceError.downloadFailed(error.localizedDescription)
        }
    }

    /// Download as an async byte stream — for large files, avoids buffering in memory.
    @available(macOS 12.0, iOS 15.0, tvOS 15.0, watchOS 8.0, *)
    public static func resolveAsStream(
        _ ref: StreamReference, authorization: String? = nil
    ) async throws -> URLSession.AsyncBytes {
        let url = try validatedURL(ref)
        do {
            let (bytes, response) = try await URLSession.shared.bytes(
                for: authorizedRequest(url: url, authorization: authorization))
            try check(response)
            return bytes
        } catch let error as StreamReferenceError {
            throw error
        } catch {
            throw StreamReferenceError.downloadFailed(error.localizedDescription)
        }
    }

    /// A rejected download used to be indistinguishable from an empty one — the status was never read.
    private static func check(_ response: URLResponse) throws {
        guard let http = response as? HTTPURLResponse else { return }
        guard (200..<300).contains(http.statusCode) else {
            throw StreamReferenceError.downloadFailed("HTTP \(http.statusCode)")
        }
    }

    private static func validatedURL(_ ref: StreamReference) throws -> URL {
        guard let url = URL(string: ref.uri) else {
            throw StreamReferenceError.downloadFailed("invalid URL: \(ref.uri)")
        }
        guard let scheme = url.scheme?.lowercased(),
              scheme == "http" || scheme == "https" else {
            throw StreamReferenceError.unsupportedScheme(url.scheme ?? "nil")
        }
        return url
    }
}

/// Check whether an `AnyCodable` value represents a `StreamReference`.
///
/// The .NET server serialises the reference as `{ "Uri": "<url>" }`.
public func isStreamReference(_ value: Any) -> StreamReference? {
    guard let dict = value as? [String: Any],
          let uri = dict["Uri"] as? String else {
        return nil
    }

    // The marker is exact; the lone-`Uri` form below it is the fallback for a server that predates
    // it. The key count cannot simply be `1` any more — a marked reference has two.
    if remoteReferenceMarker(of: dict) != nil {
        return isMarked(dict, as: .stream) ? StreamReference(uri: uri) : nil
    }

    guard dict.count == 1 else {
        return nil
    }
    return StreamReference(uri: uri)
}
