import Foundation
import XCTest
@testable import CocoarSignalARRR

final class ClientToServerTests: IntegrationTestBase {

    func testInvokeReturnsString() async throws {
        let result: String = try await connection.invoke("GetNameAsync")
        XCTAssertEqual(result, "MyNameAsync")
    }

    func testInvokeReturnsGuid() async throws {
        let result: String = try await connection.invoke("GetGuidAsync")
        XCTAssertFalse(result.isEmpty, "Expected a non-empty GUID string")
        XCTAssertNotNil(UUID(uuidString: result), "Expected a valid UUID, got: \(result)")
    }

    func testSendVoidMethod() async throws {
        try await connection.send("NothingAsync")
    }

    func testEcho() async throws {
        let result: String = try await connection.invoke("Echo", arguments: "hello")
        XCTAssertEqual(result, "hello")
    }

    func testInvokeSyncMethod() async throws {
        let result: String = try await connection.invoke("GetName")
        XCTAssertEqual(result, "MyName")
    }
}
