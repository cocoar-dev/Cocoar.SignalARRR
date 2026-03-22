import Foundation
import XCTest
@testable import CocoarSignalARRR

final class ErrorHandlingTests: IntegrationTestBase {

    func testStructuredError_ArgumentException() async throws {
        do {
            let _: String = try await connection.invoke(
                "ExtraMethods.ThrowArgumentException",
                arguments: "testParam"
            )
            XCTFail("Expected an error to be thrown")
        } catch {
            let harrrError = parseHARRRError(fromMessage: "\(error)")
            XCTAssertEqual(harrrError.type, "System.ArgumentException")
            XCTAssert(harrrError.message.contains("Invalid value provided"),
                      "Expected message to contain 'Invalid value provided', got: \(harrrError.message)")
        }
    }

    func testStructuredError_InvalidOperationException() async throws {
        do {
            let _: String = try await connection.invoke(
                "ExtraMethods.ThrowInvalidOperation"
            )
            XCTFail("Expected an error to be thrown")
        } catch {
            let harrrError = parseHARRRError(fromMessage: "\(error)")
            XCTAssertEqual(harrrError.type, "System.InvalidOperationException")
            XCTAssertEqual(harrrError.message, "This operation is not allowed")
        }
    }

    func testInvokeNonExistentMethod_Throws() async throws {
        do {
            let _: String = try await connection.invoke("NonExistentMethod")
            XCTFail("Expected an error to be thrown")
        } catch {
            // Any error is acceptable — the key is it doesn't succeed
            XCTAssertFalse("\(error)".isEmpty)
        }
    }
}
