import XCTest
@testable import CocoarSignalARRR

final class CancellationManagerTests: XCTestCase {

    func testCancelResumesWithError() async {
        let manager = CancellationManager()
        let id = "test-cancel-id"

        let expectation = XCTestExpectation(description: "cancellation throws")

        let task = Task {
            do {
                try await manager.register(id: id)
                XCTFail("Expected CancellationError to be thrown")
            } catch is CancellationError {
                expectation.fulfill()
            } catch {
                XCTFail("Unexpected error: \(error)")
            }
        }

        // Give the register call time to suspend
        try? await Task.sleep(nanoseconds: 100_000_000)

        await manager.cancel(id: id)
        await fulfillment(of: [expectation], timeout: 2.0)

        task.cancel()
    }

    func testCancelUnknownIdIsNoOp() async {
        let manager = CancellationManager()
        // Should not crash
        await manager.cancel(id: "nonexistent")
    }

    func testRemoveWithoutCancel() async {
        let manager = CancellationManager()
        // Should not crash
        await manager.remove(id: "nonexistent")
    }
}

final class CancellationTokenReferenceTests: XCTestCase {

    func testIsCancellationTokenReferenceWithValidDict() {
        let dict: [String: Any] = ["Id": "abc-123"]
        let ref = isCancellationTokenReference(dict)
        XCTAssertNotNil(ref)
        XCTAssertEqual(ref?.id, "abc-123")
    }

    func testIsCancellationTokenReferenceWithExtraKeys() {
        let dict: [String: Any] = ["Id": "abc-123", "Extra": "data"]
        let ref = isCancellationTokenReference(dict)
        XCTAssertNil(ref, "Should not match when extra keys are present")
    }

    func testIsCancellationTokenReferenceWithNonDict() {
        let ref = isCancellationTokenReference("not a dict")
        XCTAssertNil(ref)
    }

    func testIsCancellationTokenReferenceWithWrongKeyType() {
        let dict: [String: Any] = ["Id": 42]
        let ref = isCancellationTokenReference(dict)
        XCTAssertNil(ref, "Id must be a String")
    }

    func testCodableRoundTrip() throws {
        let original = CancellationTokenReference(id: "test-guid")
        let data = try JSONEncoder().encode(original)
        let decoded = try JSONDecoder().decode(CancellationTokenReference.self, from: data)
        XCTAssertEqual(decoded.id, "test-guid")

        let json = try XCTUnwrap(String(data: data, encoding: .utf8))
        XCTAssert(json.contains("\"Id\""), "Expected PascalCase key 'Id'")
    }
}
