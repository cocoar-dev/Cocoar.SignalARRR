import Foundation

/// The `__type` marker the server puts on arguments that are handles rather than values.
///
/// Some arguments are not data but references: a cancellation token the server can trip later, a
/// stream the client has to fetch. The client has to recognise them to swap them back, and it used
/// to do that by guessing from the shape. Guessing is wrong on ordinary data that happens to look
/// the same — a payload consisting of a single `Id` string was taken for a cancellation token, and
/// the real argument never reached the handler. The .NET clients never had the problem because they
/// know the parameter types; this one does not.
public enum RemoteReferenceKind: String, Sendable {
    case cancellationToken
    case stream
}

/// The property every remote reference carries.
public let remoteReferencePropertyName = "__type"

/// The marker on `dict`, or `nil` when it carries none.
///
/// A missing marker is not a rejection: it means the sender predates the marker, and the caller
/// falls back to matching on shape.
public func remoteReferenceMarker(of dict: [String: Any]) -> String? {
    dict[remoteReferencePropertyName] as? String
}

/// Whether `dict` is marked as `kind`. `false` when it carries a different marker.
public func isMarked(_ dict: [String: Any], as kind: RemoteReferenceKind) -> Bool {
    remoteReferenceMarker(of: dict) == kind.rawValue
}
