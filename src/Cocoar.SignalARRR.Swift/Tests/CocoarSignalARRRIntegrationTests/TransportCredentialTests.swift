import Foundation
import XCTest
@testable import CocoarSignalARRR

/// Where the transport carries the connection token. `TestHub.TransportCredential` reports what the
/// server saw on the transport request; the test server does not copy the query into the header.
final class TransportCredentialTests: XCTestCase {
    private var serverURL: String!

    override func setUp() async throws {
        guard let url = ProcessInfo.processInfo.environment["SIGNALARRR_TEST_SERVER_URL"] else {
            throw XCTSkip("SIGNALARRR_TEST_SERVER_URL not set — skipping integration tests")
        }
        serverURL = url
    }

    private func transportCredential(
        _ transport: TransportType, _ credential: TransportCredential? = nil
    ) async throws -> String {
        let connection: HARRRConnection
        if let credential {
            connection = await HARRRConnection.create(
                url: "\(serverURL!)/signalr/testhub",
                accessTokenFactory: { "probe-token" },
                transportCredential: credential,
                allowedTransports: [transport]
            )
        } else {
            connection = await HARRRConnection.create(
                url: "\(serverURL!)/signalr/testhub",
                accessTokenFactory: { "probe-token" },
                allowedTransports: [transport]
            )
        }
        try await connection.start()
        do {
            let result: String = try await connection.invoke("TransportCredential")
            await connection.stop()
            return result
        } catch {
            await connection.stop()
            throw error
        }
    }

    func testCredentialOptionAuthenticatesTheConnection() async throws {
        let connection = await HARRRConnection.create(
            url: "\(serverURL!)/signalr/testhub",
            options: HARRRConnectionOptions(credential: { "probe-token" })
        )
        try await connection.start()
        do {
            let result: String = try await connection.invoke("TransportCredential")
            await connection.stop()
            XCTAssertEqual(result, "header=Bearer probe-token;query=-")
        } catch {
            await connection.stop()
            throw error
        }
    }

    func testHeaderByDefaultOverWebSockets() async throws {
        let result = try await transportCredential(.webSockets)
        XCTAssertEqual(result, "header=Bearer probe-token;query=-")
    }

    func testHeaderByDefaultOverServerSentEvents() async throws {
        let result = try await transportCredential(.serverSentEvents)
        XCTAssertEqual(result, "header=Bearer probe-token;query=-")
    }

    func testHeaderByDefaultOverLongPolling() async throws {
        let result = try await transportCredential(.longPolling)
        XCTAssertEqual(result, "header=Bearer probe-token;query=-")
    }

    func testQueryWhenAskedOverWebSockets() async throws {
        let result = try await transportCredential(.webSockets, .query)
        XCTAssertEqual(result, "header=-;query=probe-token")
    }

    func testQueryWhenAskedOverServerSentEvents() async throws {
        let result = try await transportCredential(.serverSentEvents, .query)
        XCTAssertEqual(result, "header=-;query=probe-token")
    }

    func testQueryWhenAskedOverLongPolling() async throws {
        let result = try await transportCredential(.longPolling, .query)
        XCTAssertEqual(result, "header=-;query=probe-token")
    }
}
