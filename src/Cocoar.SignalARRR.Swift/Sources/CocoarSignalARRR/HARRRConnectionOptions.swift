import Foundation

/// Configuration options for `HARRRConnection`.
public struct HARRRConnectionOptions: Sendable {
    /// When `true`, server request replies are sent via HTTP POST instead of SignalR.
    ///
    /// The response is posted to `{baseURL}/response/{requestId}`.
    public var useHttpResponse: Bool

    /// Base URL for HTTP response mode.
    ///
    /// Required when `useHttpResponse` is `true`. Ignored otherwise.
    public var baseURL: URL?

    public init(useHttpResponse: Bool = false, baseURL: URL? = nil) {
        self.useHttpResponse = useHttpResponse
        self.baseURL = baseURL
    }
}
