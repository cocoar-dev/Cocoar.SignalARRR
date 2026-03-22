import Foundation

// MARK: - Pending Invocation

/// Holds a continuation for a single invoke() call, safe against the race
/// where the completion message arrives before `wait()` is called.
final class PendingInvocation: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<(Data?, String?), Error>?
    private var storedResult: Result<(Data?, String?), Error>?

    /// Suspend until the invocation completes. Returns (rawMessageData, errorString).
    func wait() async throws -> (Data?, String?) {
        try await withCheckedThrowingContinuation { cont in
            lock.lock()
            if let result = storedResult {
                lock.unlock()
                cont.resume(with: result)
            } else {
                continuation = cont
                lock.unlock()
            }
        }
    }

    /// Complete with a result from a CompletionMessage.
    func complete(rawData: Data?, error: String?) {
        lock.lock()
        if let cont = continuation {
            continuation = nil
            lock.unlock()
            cont.resume(returning: (rawData, error))
        } else {
            storedResult = .success((rawData, error))
            lock.unlock()
        }
    }

    /// Fail with a transport-level error (e.g. disconnect).
    func fail(_ error: Error) {
        lock.lock()
        if let cont = continuation {
            continuation = nil
            lock.unlock()
            cont.resume(throwing: error)
        } else {
            storedResult = .failure(error)
            lock.unlock()
        }
    }
}

// MARK: - Invocation Tracker

/// Thread-safe tracker for pending invocations and active streams.
final class InvocationTracker: @unchecked Sendable {
    private let lock = NSLock()
    private var nextId: Int = 0
    private var invocations: [String: PendingInvocation] = [:]
    private var streams: [String: AsyncThrowingStream<Data, Error>.Continuation] = [:]

    /// Generate a unique invocation ID.
    func nextInvocationId() -> String {
        lock.lock()
        let id = nextId
        nextId += 1
        lock.unlock()
        return String(id)
    }

    // MARK: Invocations

    func registerInvocation(id: String, pending: PendingInvocation) {
        lock.lock()
        invocations[id] = pending
        lock.unlock()
    }

    func completeInvocation(id: String, error: String?, rawData: Data?) {
        lock.lock()
        let pending = invocations.removeValue(forKey: id)
        lock.unlock()
        pending?.complete(rawData: rawData, error: error)
    }

    func removeInvocation(id: String) {
        lock.lock()
        invocations.removeValue(forKey: id)
        lock.unlock()
    }

    // MARK: Streams

    func registerStream(id: String, continuation: AsyncThrowingStream<Data, Error>.Continuation) {
        lock.lock()
        streams[id] = continuation
        lock.unlock()
    }

    func yieldStreamItem(id: String, rawData: Data) {
        lock.lock()
        let cont = streams[id]
        lock.unlock()
        cont?.yield(rawData)
    }

    func finishStream(id: String, error: String?) {
        lock.lock()
        let cont = streams.removeValue(forKey: id)
        lock.unlock()
        if let error = error {
            cont?.finish(throwing: HubInvocationError(message: error))
        } else {
            cont?.finish()
        }
    }

    func removeStream(id: String) {
        lock.lock()
        let cont = streams.removeValue(forKey: id)
        lock.unlock()
        cont?.finish()
    }

    // MARK: Teardown

    /// Fail all pending invocations and finish all streams (on disconnect).
    func failAll(error: Error) {
        lock.lock()
        let allInvocations = invocations
        let allStreams = streams
        invocations.removeAll()
        streams.removeAll()
        lock.unlock()

        for (_, pending) in allInvocations {
            pending.fail(error)
        }
        for (_, cont) in allStreams {
            cont.finish(throwing: error)
        }
    }
}
