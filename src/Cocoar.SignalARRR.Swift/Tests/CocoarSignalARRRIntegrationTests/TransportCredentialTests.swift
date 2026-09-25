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

    /// Headers prefixed with `#` become client attributes on the server, which reports them back.
    func testCustomHeadersReachTheServer() async throws {
        let connection = await HARRRConnection.create(
            url: "\(serverURL!)/signalr/testhub",
            headers: ["#ClientType": "Swift", "#AppVersion": "3.0.0"]
        )
        try await connection.start()
        do {
            let connectionId: String = try await connection.invoke("GetConnectionId")
            try await Task.sleep(nanoseconds: 500_000_000)   // let the server register the client

            var components = URLComponents(string: "\(serverURL!)/__test/get-client-attributes")!
            components.queryItems = [URLQueryItem(name: "connectionId", value: connectionId)]
            var request = URLRequest(url: components.url!)
            request.httpMethod = "POST"
            let (data, response) = try await URLSession.shared.data(for: request)
            XCTAssertEqual((response as? HTTPURLResponse)?.statusCode, 200)
            let attributes = try JSONSerialization.jsonObject(with: data) as? [String: Any]
            XCTAssertEqual(attributes?["ClientType"] as? String, "Swift")
            XCTAssertEqual(attributes?["AppVersion"] as? String, "3.0.0")
            await connection.stop()
        } catch {
            await connection.stop()
            throw error
        }
    }

    func testRejectedNegotiateReportsItsHTTPStatus() async throws {
        let connection = await HARRRConnection.create(
            url: "\(serverURL!)/signalr/no-such-hub",
            reconnectPolicy: .disabled
        )
        do {
            try await connection.start()
            XCTFail("start() should have failed")
        } catch let error as SignalRError {
            XCTAssertEqual(error.statusCode, 404)
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
