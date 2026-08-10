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

    /// Contrast with the ArgumentException case above, which still arrives verbatim: that one names
    /// a pipeline stage the server controls, this one is whatever the hub method threw and could
    /// say anything about the server's insides. Since 5.0 it is withheld and logged server-side
    /// under the correlation id the client is shown.
    func testStructuredError_UnexpectedException_WithholdsTheDetail() async throws {
        do {
            let _: String = try await connection.invoke(
                "ExtraMethods.ThrowInvalidOperation"
            )
            XCTFail("Expected an error to be thrown")
        } catch {
            let harrrError = parseHARRRError(fromMessage: "\(error)")
            XCTAssertNotEqual(harrrError.type, "System.InvalidOperationException")
            XCTAssertFalse(harrrError.message.contains("This operation is not allowed"),
                           "The method's own message must not reach the client, got: \(harrrError.message)")
            XCTAssert(harrrError.message.contains("Correlation id:"),
                      "Expected a correlation id to trace this call by, got: \(harrrError.message)")
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
