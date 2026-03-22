import XCTest
@testable import CocoarSignalARRR

final class AnyCodableTests: XCTestCase {

    // MARK: - Encoding

    func testEncodeString() throws {
        let value = AnyCodable("hello")
        let data = try JSONEncoder().encode(value)
        XCTAssertEqual(String(data: data, encoding: .utf8), "\"hello\"")
    }

    func testEncodeInt() throws {
        let value = AnyCodable(42)
        let data = try JSONEncoder().encode(value)
        XCTAssertEqual(String(data: data, encoding: .utf8), "42")
    }

    func testEncodeBool() throws {
        let value = AnyCodable(true)
        let data = try JSONEncoder().encode(value)
        XCTAssertEqual(String(data: data, encoding: .utf8), "true")
    }

    func testEncodeNull() throws {
        let value = AnyCodable(NSNull())
        let data = try JSONEncoder().encode(value)
        XCTAssertEqual(String(data: data, encoding: .utf8), "null")
    }

    func testEncodeArray() throws {
        let value = AnyCodable([1, 2, 3] as [Any])
        let data = try JSONEncoder().encode(value)
        XCTAssertEqual(String(data: data, encoding: .utf8), "[1,2,3]")
    }

    func testEncodeDictionary() throws {
        let value = AnyCodable(["key": "value"] as [String: Any])
        let data = try JSONEncoder().encode(value)
        XCTAssertEqual(String(data: data, encoding: .utf8), "{\"key\":\"value\"}")
    }

    // MARK: - Decoding

    func testDecodeString() throws {
        let data = "\"hello\"".data(using: .utf8)!
        let decoded = try JSONDecoder().decode(AnyCodable.self, from: data)
        XCTAssertEqual(decoded.value as? String, "hello")
    }

    func testDecodeInt() throws {
        let data = "42".data(using: .utf8)!
        let decoded = try JSONDecoder().decode(AnyCodable.self, from: data)
        XCTAssertEqual(decoded.value as? Int, 42)
    }

    func testDecodeBool() throws {
        let data = "true".data(using: .utf8)!
        let decoded = try JSONDecoder().decode(AnyCodable.self, from: data)
        XCTAssertEqual(decoded.value as? Bool, true)
    }

    func testDecodeNull() throws {
        let data = "null".data(using: .utf8)!
        let decoded = try JSONDecoder().decode(AnyCodable.self, from: data)
        XCTAssert(decoded.value is NSNull)
    }

    func testDecodeArray() throws {
        let data = "[1,2,3]".data(using: .utf8)!
        let decoded = try JSONDecoder().decode(AnyCodable.self, from: data)
        let array = try XCTUnwrap(decoded.value as? [Any])
        XCTAssertEqual(array.count, 3)
    }

    func testDecodeDictionary() throws {
        let data = "{\"key\":\"value\"}".data(using: .utf8)!
        let decoded = try JSONDecoder().decode(AnyCodable.self, from: data)
        let dict = try XCTUnwrap(decoded.value as? [String: Any])
        XCTAssertEqual(dict["key"] as? String, "value")
    }

    // MARK: - Round-trip

    func testRoundTripDouble() throws {
        let original = AnyCodable(3.14)
        let data = try JSONEncoder().encode(original)
        let decoded = try JSONDecoder().decode(AnyCodable.self, from: data)
        XCTAssertEqual(decoded.value as? Double, 3.14)
    }

    // MARK: - Equatable

    func testEquality() {
        XCTAssertEqual(AnyCodable("a"), AnyCodable("a"))
        XCTAssertNotEqual(AnyCodable("a"), AnyCodable("b"))
        XCTAssertEqual(AnyCodable(1), AnyCodable(1))
        XCTAssertEqual(AnyCodable(true), AnyCodable(true))
        XCTAssertNotEqual(AnyCodable(true), AnyCodable(false))
    }

    // MARK: - Literals

    func testLiterals() {
        let _: AnyCodable = nil
        let _: AnyCodable = true
        let _: AnyCodable = 42
        let _: AnyCodable = 3.14
        let _: AnyCodable = "hello"
        let _: AnyCodable = [1, 2, 3]
        let _: AnyCodable = ["key": "value"]
    }
}
