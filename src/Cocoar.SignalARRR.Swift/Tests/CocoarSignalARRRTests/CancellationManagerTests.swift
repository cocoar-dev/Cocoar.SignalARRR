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

    private static let guid = "3F2504E0-4F89-11D3-9A0C-0305E82C3301"

    func testIsCancellationTokenReferenceWithMarker() {
        let dict: [String: Any] = ["__type": "cancellationToken", "Id": Self.guid]
        let ref = isCancellationTokenReference(dict)
        XCTAssertNotNil(ref, "The marker is exact and does not depend on the shape")
        XCTAssertEqual(ref?.id, Self.guid)
    }

    func testIsCancellationTokenReferenceWithMarkerIgnoresExtraKeys() {
        let dict: [String: Any] = ["__type": "cancellationToken", "Id": Self.guid, "Extra": "data"]
        XCTAssertNotNil(isCancellationTokenReference(dict))
    }

    func testIsCancellationTokenReferenceRejectsOtherMarker() {
        // Marked as something else: not a token, however much the rest looks like one.
        let dict: [String: Any] = ["__type": "stream", "Id": Self.guid]
        XCTAssertNil(isCancellationTokenReference(dict))
    }

    func testIsCancellationTokenReferenceWithValidDict() {
        // Unmarked: accepted, for a server that predates the marker.
        let dict: [String: Any] = ["Id": Self.guid]
        let ref = isCancellationTokenReference(dict)
        XCTAssertNotNil(ref)
        XCTAssertEqual(ref?.id, Self.guid)
    }

    func testIsCancellationTokenReferenceWithExtraKeys() {
        let dict: [String: Any] = ["Id": Self.guid, "Extra": "data"]
        let ref = isCancellationTokenReference(dict)
        XCTAssertNil(ref, "Should not match when extra keys are present and nothing marks it")
    }

    func testIsCancellationTokenReferenceRequiresGuidWhenUnmarked() {
        // Without a marker the id has to look like a GUID. A lone Id string is a shape ordinary
        // payloads have too, and mistaking one for a token loses the real argument.
        let dict: [String: Any] = ["Id": "order-7"]
        XCTAssertNil(isCancellationTokenReference(dict))
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
