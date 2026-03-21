import XCTest
@testable import CocoarSignalARRR

final class ServerRequestMessageTests: XCTestCase {

    func testEncodeWithPascalCaseKeys() throws {
        let msg = ServerRequestMessage(
            id: "550e8400-e29b-41d4-a716-446655440000",
            method: "ReceiveMessage",
            arguments: [AnyCodable("Hello")],
            cancellationGuid: "abc-123"
        )

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        let data = try encoder.encode(msg)
        let json = try XCTUnwrap(String(data: data, encoding: .utf8))

        XCTAssert(json.contains("\"Id\""), "Expected PascalCase key 'Id'")
        XCTAssert(json.contains("\"Method\""), "Expected PascalCase key 'Method'")
        XCTAssert(json.contains("\"Arguments\""), "Expected PascalCase key 'Arguments'")
        XCTAssert(json.contains("\"CancellationGuid\""), "Expected PascalCase key 'CancellationGuid'")
    }

    func testRoundTrip() throws {
        let original = ServerRequestMessage(
            id: "test-id",
            method: "ReceiveMessage",
            arguments: [AnyCodable("Alice"), AnyCodable("Hi")],
            genericArguments: ["System.String"],
            cancellationGuid: "cancel-guid",
            streamId: "stream-guid"
        )

        let data = try JSONEncoder().encode(original)
        let decoded = try JSONDecoder().decode(ServerRequestMessage.self, from: data)

        XCTAssertEqual(decoded.id, original.id)
        XCTAssertEqual(decoded.method, original.method)
        XCTAssertEqual(decoded.cancellationGuid, original.cancellationGuid)
        XCTAssertEqual(decoded.streamId, original.streamId)
        XCTAssertEqual(decoded.genericArguments, original.genericArguments)
        XCTAssertEqual(decoded.arguments?.count, 2)
    }

    func testDecodeFromServer() throws {
        let json = """
        {
            "Id": "550e8400-e29b-41d4-a716-446655440000",
            "Method": "ReceiveMessage",
            "Arguments": [{"Id": "cancel-ref"}],
            "CancellationGuid": "cancel-ref"
        }
        """.data(using: .utf8)!

        let msg = try JSONDecoder().decode(ServerRequestMessage.self, from: json)
        XCTAssertEqual(msg.id, "550e8400-e29b-41d4-a716-446655440000")
        XCTAssertEqual(msg.method, "ReceiveMessage")
        XCTAssertEqual(msg.cancellationGuid, "cancel-ref")
        XCTAssertNil(msg.streamId)
        XCTAssertNil(msg.genericArguments)
        XCTAssertEqual(msg.arguments?.count, 1)
    }

    func testDecodeWithNullOptionals() throws {
        let json = """
        {
            "Id": "test-id",
            "Method": "Test"
        }
        """.data(using: .utf8)!

        let msg = try JSONDecoder().decode(ServerRequestMessage.self, from: json)
        XCTAssertEqual(msg.id, "test-id")
        XCTAssertEqual(msg.method, "Test")
        XCTAssertNil(msg.arguments)
        XCTAssertNil(msg.genericArguments)
        XCTAssertNil(msg.cancellationGuid)
        XCTAssertNil(msg.streamId)
    }
}
