import Foundation

/// Marker in a server request's `Arguments` array indicating a cancellation token slot.
///
/// When processing `ServerRequestMessage.arguments`, entries that decode as
/// `CancellationTokenReference` should be replaced with an actual cancellation
/// handle from the `CancellationManager`.
public struct CancellationTokenReference: Codable, Sendable {
    public var id: String

    public init(id: String) {
        self.id = id
    }

    enum CodingKeys: String, CodingKey {
        case id = "Id"
    }
}

/// Check whether an `AnyCodable` value represents a `CancellationTokenReference`.
///
/// The server marks the reference `{ "__type": "cancellationToken", "Id": "<guid>" }`, which is
/// exact. The bare `{ "Id": "<guid>" }` form below it is the fallback for a server that does not
/// send the marker yet; it additionally requires the value to look like a GUID, because a lone
/// `Id` string is otherwise a shape ordinary payloads have too.
///
/// Note that the key count cannot simply be `1` any more: a marked reference has two keys, and
/// rejecting it on that basis is how adding the marker would have broken cancellation outright.
public func isCancellationTokenReference(_ value: Any) -> CancellationTokenReference? {
    guard let dict = value as? [String: Any],
          let id = dict["Id"] as? String else {
        return nil
    }

    if remoteReferenceMarker(of: dict) != nil {
        return isMarked(dict, as: .cancellationToken) ? CancellationTokenReference(id: id) : nil
    }

    guard dict.count == 1, UUID(uuidString: id) != nil else {
        return nil
    }
    return CancellationTokenReference(id: id)
}
