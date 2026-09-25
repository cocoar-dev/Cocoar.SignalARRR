import XCTest
@testable import CocoarSignalARRR

/// The credential options — `credential`, `connectionCredential`, `messageCredential` — named and
/// behaving as in every SignalARRR client. `create` does not throw, so a credential set in two places
/// surfaces from `start()`, before any connection attempt.
final class CredentialOptionsTests: XCTestCase {

    private let url = "http://127.0.0.1:1/hub"

    func testSpecificCredentialTakesItsPartOverFromCredential() async throws {
        let options = HARRRConnectionOptions(
            credential: { "both" },
            messageCredential: { "message" }
        )
        let connection = await options.resolvedConnectionCredential?()
        let message = await options.resolvedMessageCredential?()
        XCTAssertEqual(connection, "both")
        XCTAssertEqual(message, "message")
    }

    func testMessageCredentialAloneLeavesTheConnectionCredentialUnset() {
        let options = HARRRConnectionOptions(messageCredential: { "message" })
        XCTAssertNil(options.resolvedConnectionCredential)
    }

    func testAccessTokenFactoryTogetherWithACredentialIsAConfigurationError() async {
        let connection = await HARRRConnection.create(
            url: url,
            accessTokenFactory: { "legacy" },
            options: HARRRConnectionOptions(credential: { "new" })
        )
        await assertConfigurationError(connection)
    }

    func testConnectionCredentialOnABuiltClientIsAConfigurationError() async {
        let client = SignalRWebSocketClient(url: url)
        let connection = await HARRRConnection.create(
            client: client,
            options: HARRRConnectionOptions(connectionCredential: { "connection" })
        )
        await assertConfigurationError(connection)
    }

    func testStatusCodeIsReadFromARejectedNegotiate() {
        XCTAssertEqual(SignalRError.negotiationFailed("HTTP 401").statusCode, 401)
        XCTAssertNil(SignalRError.negotiationFailed("HTTP 0").statusCode)
        XCTAssertNil(SignalRError.negotiationFailed("Missing connectionToken in response").statusCode)
        XCTAssertNil(SignalRError.connectionFailed("HTTP 401").statusCode)
    }

    private func assertConfigurationError(_ connection: HARRRConnection, file: StaticString = #filePath, line: UInt = #line) async {
        do {
            try await connection.start()
            XCTFail("start() should have thrown a configuration error", file: file, line: line)
        } catch is HARRRConfigurationError {
            // expected
        } catch {
            XCTFail("expected HARRRConfigurationError, got \(error)", file: file, line: line)
        }
    }
}
