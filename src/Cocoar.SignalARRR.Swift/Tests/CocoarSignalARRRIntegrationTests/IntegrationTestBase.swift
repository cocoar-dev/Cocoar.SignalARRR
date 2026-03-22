import Foundation
import XCTest
@testable import CocoarSignalARRR

class IntegrationTestBase: XCTestCase {
    var serverURL: String!
    var connection: HARRRConnection!

    override func setUp() async throws {
        guard let url = ProcessInfo.processInfo.environment["SIGNALARRR_TEST_SERVER_URL"] else {
            throw XCTSkip("SIGNALARRR_TEST_SERVER_URL not set — skipping integration tests")
        }
        serverURL = url
        connection = await HARRRConnection.create({ builder in
            _ = builder.withUrl(url: "\(url)/signalr/testhub")
        })
        try await connection.start()
    }

    override func tearDown() async throws {
        await connection?.stop()
    }
}
