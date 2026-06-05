import XCTest
@testable import CocoarSignalARRR

/// Regression tests for the transport URL construction (Finding 4: a hub URL carrying a
/// query string must keep that query and still receive the appended `id` connection token).
final class TransportURLTests: XCTestCase {

    func testWebSocketURLConvertsScheme() throws {
        let url = try XCTUnwrap(TransportFactory.transportURL(
            base: "http://host:5005/hub/sync", connectionToken: "abc", type: .webSockets))
        XCTAssertEqual(url.scheme, "ws")
        XCTAssertEqual(url.path, "/hub/sync")
    }

    func testHttpsConvertsToWss() throws {
        let url = try XCTUnwrap(TransportFactory.transportURL(
            base: "https://host/hub", connectionToken: "abc", type: .webSockets))
        XCTAssertEqual(url.scheme, "wss")
    }

    func testNonWebSocketKeepsScheme() throws {
        let url = try XCTUnwrap(TransportFactory.transportURL(
            base: "http://host/hub", connectionToken: "abc", type: .longPolling))
        XCTAssertEqual(url.scheme, "http")
    }

    func testConnectionTokenAppendedAsQueryItem() throws {
        let url = try XCTUnwrap(TransportFactory.transportURL(
            base: "http://host/hub", connectionToken: "tok123", type: .webSockets))
        let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
        XCTAssertTrue(items.contains(URLQueryItem(name: "id", value: "tok123")))
    }

    func testExistingQueryStringIsPreserved() throws {
        // The bug: string concatenation produced ".../hub?user=x?id=..." — a broken URL.
        let url = try XCTUnwrap(TransportFactory.transportURL(
            base: "http://host/hub/sync?user=alice", connectionToken: "tok", type: .webSockets))
        let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
        XCTAssertTrue(items.contains(URLQueryItem(name: "user", value: "alice")))
        XCTAssertTrue(items.contains(URLQueryItem(name: "id", value: "tok")))
        XCTAssertEqual(url.path, "/hub/sync")
    }

    func testConnectionTokenIsPercentEncoded() throws {
        // Tokens contain characters that must be percent-encoded in a query value.
        let token = "a+b/c=d"
        let url = try XCTUnwrap(TransportFactory.transportURL(
            base: "http://host/hub", connectionToken: token, type: .webSockets))
        let items = URLComponents(url: url, resolvingAgainstBaseURL: false)?.queryItems ?? []
        // URLComponents decodes back to the original value when parsed.
        XCTAssertEqual(items.first(where: { $0.name == "id" })?.value, token)
    }
}
