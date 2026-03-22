import Foundation
import XCTest
@testable import CocoarSignalARRR

/// Integration tests for the MessagePack hub protocol.
/// Uses `SignalRWebSocketClient` directly to test the raw SignalR protocol layer.
final class MessagePackTests: XCTestCase {
    var client: SignalRWebSocketClient!

    override func setUp() async throws {
        guard let url = ProcessInfo.processInfo.environment["SIGNALARRR_TEST_SERVER_URL"] else {
            throw XCTSkip("SIGNALARRR_TEST_SERVER_URL not set — skipping integration tests")
        }
        client = SignalRWebSocketClient(url: "\(url)/signalr/testhub", hubProtocol: .messagepack)
        try await client.start()
    }

    override func tearDown() async throws {
        await client?.stop()
    }

    func testInvokeReturnsString() async throws {
        let result: String = try await client.invoke(method: "GetName", arguments: [])
        XCTAssertEqual(result, "MyName")
    }

    func testInvokeReturnsGuid() async throws {
        let result: String = try await client.invoke(method: "GetGuid", arguments: [])
        XCTAssertFalse(result.isEmpty, "Expected a non-empty GUID string")
        XCTAssertNotNil(UUID(uuidString: result), "Expected a valid UUID, got: \(result)")
    }

    func testSendVoidMethod() async throws {
        // Fire-and-forget — should not throw
        try await client.send(method: "Nothing", arguments: [])
    }

    func testEcho() async throws {
        let result: String = try await client.invoke(method: "Echo", arguments: ["hello msgpack"])
        XCTAssertEqual(result, "hello msgpack")
    }

    func testMultipleParameterTypes() async throws {
        // Counter(int count, int delay) — two integer parameters — tests int MessagePack encoding
        let stream: AsyncThrowingStream<Int, Error> = try await client.stream(method: "Counter", arguments: [3, 0])
        var items: [Int] = []
        for try await n in stream { items.append(n) }
        XCTAssertEqual(items, [0, 1, 2])
    }
}
