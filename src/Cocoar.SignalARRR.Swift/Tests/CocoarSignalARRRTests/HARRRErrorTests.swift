import Foundation
import XCTest
@testable import CocoarSignalARRR

final class HARRRErrorTests: XCTestCase {

    // MARK: - JSON format

    func testParsesJsonFormat() {
        let json = #"{"Type":"System.ArgumentException","Message":"Invalid value provided","StackTrace":null}"#
        let error = parseHARRRError(fromMessage: json)
        XCTAssertEqual(error.type, "System.ArgumentException")
        XCTAssertEqual(error.message, "Invalid value provided")
        XCTAssertNil(error.stackTrace)
    }

    func testParsesJsonFormatWithStackTrace() {
        let json = #"{"Type":"System.InvalidOperationException","Message":"Not allowed","StackTrace":"at Foo.Bar()"}"#
        let error = parseHARRRError(fromMessage: json)
        XCTAssertEqual(error.type, "System.InvalidOperationException")
        XCTAssertEqual(error.message, "Not allowed")
        XCTAssertEqual(error.stackTrace, "at Foo.Bar()")
    }

    // MARK: - SignalR wrapped format

    func testParsesJsonAfterHARRRExceptionMarker() {
        let msg = "An unexpected error occurred invoking 'InvokeMessage' on the server. HARRRException: {\"Type\":\"System.ArgumentException\",\"Message\":\"Bad param\"}"
        let error = parseHARRRError(fromMessage: msg)
        XCTAssertEqual(error.type, "System.ArgumentException")
        XCTAssertEqual(error.message, "Bad param")
    }

    // MARK: - Legacy format

    func testParsesLegacyBracketFormat() {
        let msg = "[System.InvalidOperationException] This operation is not allowed"
        let error = parseHARRRError(fromMessage: msg)
        XCTAssertEqual(error.type, "System.InvalidOperationException")
        XCTAssertEqual(error.message, "This operation is not allowed")
    }

    // MARK: - Fallback

    func testFallbackForUnknownFormat() {
        let msg = "Something went wrong"
        let error = parseHARRRError(fromMessage: msg)
        XCTAssertEqual(error.type, "Error")
        XCTAssertEqual(error.message, "Something went wrong")
    }

    // MARK: - Error conformance

    func testConformsToError() {
        let error = HARRRError(type: "System.Exception", message: "test")
        XCTAssertEqual(error.errorDescription, "[System.Exception] test")
    }

    func testEquatable() {
        let a = HARRRError(type: "System.Exception", message: "test")
        let b = HARRRError(type: "System.Exception", message: "test")
        let c = HARRRError(type: "System.ArgumentException", message: "test")
        XCTAssertEqual(a, b)
        XCTAssertNotEqual(a, c)
    }

    // MARK: - Parse from Error

    func testParseFromError() {
        struct FakeError: LocalizedError {
            var errorDescription: String? {
                #"{"Type":"System.ArgumentException","Message":"Bad"}"#
            }
        }
        let error = parseHARRRError(FakeError())
        XCTAssertEqual(error.type, "System.ArgumentException")
        XCTAssertEqual(error.message, "Bad")
    }

    func testJsonWithTypeErrorFallsBack() {
        // If Type is "Error" (default), treat as not structured and fall back
        let json = #"{"Type":"Error","Message":"generic"}"#
        let error = parseHARRRError(fromMessage: json)
        XCTAssertEqual(error.type, "Error")
        XCTAssertEqual(error.message, json) // Falls back to raw message
    }
}
