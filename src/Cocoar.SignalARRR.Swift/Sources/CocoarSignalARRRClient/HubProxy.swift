@_exported import CocoarSignalARRR

/// Generates a proxy class that implements the annotated protocol by
/// delegating each method to `HARRRConnection.invoke` / `.send` / `.stream`.
///
/// Apply to a protocol whose methods are all `async throws`:
///
/// ```swift
/// @HubProxy
/// protocol IChatHub {
///     func sendMessage(user: String, message: String) async throws
///     func getHistory() async throws -> [String]
///     func streamMessages() async throws -> AsyncThrowingStream<String, Error>
/// }
/// ```
///
/// The macro generates `IChatHubProxy`, a `final class` conforming to both
/// `IChatHub` and `HubProxyProtocol`.
@attached(peer, names: suffixed(Proxy))
public macro HubProxy() = #externalMacro(module: "CocoarSignalARRRMacroPlugin", type: "HubProxyMacro")
