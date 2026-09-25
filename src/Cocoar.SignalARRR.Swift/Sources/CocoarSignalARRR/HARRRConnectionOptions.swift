import Foundation

/// A credential: called whenever the credential is needed, so a refreshed token is picked up.
public typealias HARRRCredential = @Sendable () async -> String

/// Configuration options for `HARRRConnection`.
///
/// A connection has two credentials, checked by different things. The **connection credential**
/// authenticates negotiate and the transport and is what `[Authorize]` on the hub class checks. The
/// **message credential** travels with every message, answers token challenges and authorises file
/// transfers, and is what `[Authorize]` on a method or a `ServerMethods` class checks. The options
/// are named the same in every SignalARRR client, and nothing is coupled implicitly: setting one of
/// the two sets only that one.
public struct HARRRConnectionOptions: Sendable {

    /// One credential for the connection and for every message — the common case. A
    /// `connectionCredential` or `messageCredential` set alongside it takes its part over.
    public var credential: HARRRCredential?

    /// Authenticates the connection: the negotiate request and the transport. Called on every
    /// connect and reconnect. Only available through `HARRRConnection.create(url:)`; a
    /// `SignalRWebSocketClient` that is already built has its own.
    public var connectionCredential: HARRRCredential?

    /// Authenticates each message: travels as `ClientRequestMessage.Authorization`, answers token
    /// challenges, and authorises file-transfer requests. Called per use.
    public var messageCredential: HARRRCredential?

    public init(
        credential: HARRRCredential? = nil,
        connectionCredential: HARRRCredential? = nil,
        messageCredential: HARRRCredential? = nil
    ) {
        self.credential = credential
        self.connectionCredential = connectionCredential
        self.messageCredential = messageCredential
    }

    var usesCredentials: Bool {
        credential != nil || connectionCredential != nil || messageCredential != nil
    }

    var resolvedConnectionCredential: HARRRCredential? { connectionCredential ?? credential }
    var resolvedMessageCredential: HARRRCredential? { messageCredential ?? credential }
}

/// A connection was configured in a way that cannot work, typically a credential set in two places.
/// `HARRRConnection.create` does not throw, so this surfaces from `start()`.
public struct HARRRConfigurationError: Error, LocalizedError, CustomStringConvertible {
    public let message: String
    public var errorDescription: String? { message }
    public var description: String { message }
}
