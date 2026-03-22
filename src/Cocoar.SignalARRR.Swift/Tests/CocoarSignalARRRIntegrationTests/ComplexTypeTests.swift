import Foundation
import XCTest
@testable import CocoarSignalARRR

final class ComplexTypeTests: IntegrationTestBase {

    // MARK: - DateTime

    func testDateTimeSerializesCorrectly() async throws {
        // Server's ExtraMethods.FormatDate formats as "yyyy-MM-dd"
        let result: String = try await connection.invoke(
            "ExtraMethods.FormatDate",
            arguments: "2025-06-15T00:00:00Z"
        )
        XCTAssertEqual(result, "2025-06-15")
    }

    // MARK: - Guid

    func testGuidParameterPassesCorrectly() async throws {
        let guid = UUID().uuidString.lowercased()
        let result: String = try await connection.invoke(
            "ExtraMethods.GuidToString",
            arguments: guid
        )
        XCTAssertEqual(result.lowercased(), guid)
    }

    // MARK: - List return

    func testListReturnedCorrectly() async throws {
        let result: [String] = try await connection.invoke(
            "ExtraMethods.GenerateItems",
            arguments: 4
        )
        XCTAssertEqual(result.count, 4)
        XCTAssertEqual(result[0], "item-0")
        XCTAssertEqual(result[3], "item-3")
    }

    // MARK: - Dictionary return

    func testDictionaryReturnedCorrectly() async throws {
        let result: [String: Int] = try await connection.invoke(
            "ExtraMethods.WordLengths",
            arguments: "hello world"
        )
        XCTAssertEqual(result["hello"], 5)
        XCTAssertEqual(result["world"], 5)
    }

    // MARK: - Multiple parameter types

    func testMultipleParameterTypesWorkTogether() async throws {
        let result: String = try await connection.invoke(
            "ExtraMethods.Combine",
            arguments: "test", 42, true
        )
        XCTAssertEqual(result, "test-42-True")
    }

    // MARK: - Client → Server File Transfer

    func testClientSendsDataArgument_AutomaticUpload() async throws {
        let content = "AutoUploadFromSwift"
        let data = content.data(using: .utf8)!

        // Client sends Data as argument → buildClientRequest auto-uploads via HTTP
        // and replaces with StreamReference → server resolves to Stream
        let result: String = try await connection.invoke(
            "ExtraMethods.ReadStreamContent",
            arguments: data
        )
        XCTAssertEqual(result, content)
    }
}
