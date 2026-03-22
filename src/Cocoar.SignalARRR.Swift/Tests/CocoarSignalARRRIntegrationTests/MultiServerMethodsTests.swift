import Foundation
import XCTest
@testable import CocoarSignalARRR

final class MultiServerMethodsTests: IntegrationTestBase {

    func testSecondServerMethodsClass_Greet() async throws {
        let result: String = try await connection.invoke(
            "ExtraMethods.Greet",
            arguments: "World"
        )
        XCTAssertEqual(result, "Hello, World!")
    }

    func testSecondServerMethodsClass_Add() async throws {
        let result: Int = try await connection.invoke(
            "ExtraMethods.Add",
            arguments: 3, 4
        )
        XCTAssertEqual(result, 7)
    }

    func testMessageNameAttribute_CustomEcho() async throws {
        // Server method is EchoWithCustomName but decorated with [MessageName("CustomEcho")]
        let result: String = try await connection.invoke(
            "ExtraMethods.CustomEcho",
            arguments: "test-value"
        )
        XCTAssertEqual(result, "test-value")
    }

    func testOriginalHubStillWorksAfterAddingSecondClass() async throws {
        let result: String = try await connection.invoke("GetNameAsync")
        XCTAssertEqual(result, "MyNameAsync")
    }
}
