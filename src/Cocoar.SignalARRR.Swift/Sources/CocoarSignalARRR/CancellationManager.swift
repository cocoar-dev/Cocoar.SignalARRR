import Foundation

/// Manages cancellation tokens for server-initiated cancellation.
///
/// When the server sends a `CancelTokenFromServer` message, the corresponding
/// continuation stored here is cancelled (resumed with `CancellationError`).
public actor CancellationManager {
    private var continuations: [String: CheckedContinuation<Void, Error>] = [:]

    /// Create a cancellation handle for the given ID.
    ///
    /// Returns an `async throws` call that suspends until `cancel(id:)` is
    /// called, at which point it throws `CancellationError`.
    /// The caller should race this against actual work using a `TaskGroup`.
    public func register(id: String) async throws {
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            continuations[id] = continuation
        }
    }

    /// Cancel the operation associated with the given ID.
    public func cancel(id: String) {
        if let continuation = continuations.removeValue(forKey: id) {
            continuation.resume(throwing: CancellationError())
        }
    }

    /// Remove a registration without cancelling.
    public func remove(id: String) {
        continuations.removeValue(forKey: id)
    }
}
