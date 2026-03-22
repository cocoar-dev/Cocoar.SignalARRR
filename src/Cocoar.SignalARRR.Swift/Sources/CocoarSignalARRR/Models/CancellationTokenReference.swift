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
/// The .NET server serialises the reference as `{ "Id": "<guid>" }`.
public func isCancellationTokenReference(_ value: Any) -> CancellationTokenReference? {
    guard let dict = value as? [String: Any],
          let id = dict["Id"] as? String,
          dict.count == 1 else {
        return nil
    }
    return CancellationTokenReference(id: id)
}
