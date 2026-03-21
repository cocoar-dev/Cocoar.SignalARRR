import Foundation

/// Protocol that all `@HubProxy`-generated proxy classes conform to.
///
/// Provides the required `init(connection:)` initialiser so that
/// `HARRRConnection.getTypedMethods(_:)` can construct proxies generically.
public protocol HubProxyProtocol {
    init(connection: HARRRConnection)
}
