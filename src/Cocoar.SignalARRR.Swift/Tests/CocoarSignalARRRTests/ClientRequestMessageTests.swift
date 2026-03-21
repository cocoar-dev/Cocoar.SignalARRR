import XCTest
@testable import CocoarSignalARRR

final class ClientRequestMessageTests: XCTestCase {

    func testEncodeWithPascalCaseKeys() throws {
        let msg = ClientRequestMessage(
            method: "IChatHub|sendMessage",
            arguments: [AnyCodable("Alice"), AnyCodable("Hello!")],
            authorization: "Bearer token123"
        )

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        let data = try encoder.encode(msg)
        let json = try XCTUnwrap(String(data: data, encoding: .utf8))

        XCTAssert(json.contains("\"Method\""), "Expected PascalCase key 'Method'")
        XCTAssert(json.contains("\"Arguments\""), "Expected PascalCase key 'Arguments'")
        XCTAssert(json.contains("\"Authorization\""), "Expected PascalCase key 'Authorization'")
        XCTAssert(json.contains("\"GenericArguments\""), "Expected PascalCase key 'GenericArguments'")
    }

    func testRoundTrip() throws {
        let original = ClientRequestMessage(
            method: "IChatHub|getHistory",
            arguments: [AnyCodable(1), AnyCodable("test")],
            authorization: "Bearer abc",
            genericArguments: ["System.String"]
        )

        let data = try JSONEncoder().encode(original)
        let decoded = try JSONDecoder().decode(ClientRequestMessage.self, from: data)

        XCTAssertEqual(decoded.method, original.method)
        XCTAssertEqual(decoded.authorization, original.authorization)
        XCTAssertEqual(decoded.genericArguments, original.genericArguments)
        XCTAssertEqual(decoded.arguments.count, 2)
    }

    func testDecodeFromServer() throws {
        let json = """
        {
            "Method": "IChatHub|sendMessage",
            "Arguments": ["Alice", "Hello!"],
            "Authorization": "",
            "GenericArguments": []
        }
        """.data(using: .utf8)!

        let msg = try JSONDecoder().decode(ClientRequestMessage.self, from: json)
        XCTAssertEqual(msg.method, "IChatHub|sendMessage")
        XCTAssertEqual(msg.arguments.count, 2)
        XCTAssertEqual(msg.arguments[0].value as? String, "Alice")
        XCTAssertEqual(msg.arguments[1].value as? String, "Hello!")
        XCTAssertEqual(msg.authorization, "")
        XCTAssertEqual(msg.genericArguments, [])
    }

    func testDefaultValues() {
        let msg = ClientRequestMessage(method: "test")
        XCTAssertEqual(msg.method, "test")
        XCTAssertEqual(msg.arguments.count, 0)
        XCTAssertEqual(msg.authorization, "")
        XCTAssertEqual(msg.genericArguments.count, 0)
    }
}
