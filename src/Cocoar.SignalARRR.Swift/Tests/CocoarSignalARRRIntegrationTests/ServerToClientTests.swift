import Foundation
import XCTest
@testable import CocoarSignalARRR

final class ServerToClientTests: IntegrationTestBase {

    // MARK: - Helpers

    /// Get the connectionId by asking the server hub.
    private func getConnectionId() async throws -> String {
        let id: String = try await connection.invoke("GetConnectionId")
        guard !id.isEmpty else {
            throw XCTSkip("connectionId not available from server")
        }
        return id
    }

    /// POST to a test trigger endpoint and return the response body.
    private func triggerServerEndpoint(_ path: String, queryParams: [String: String] = [:]) async throws -> (Data, HTTPURLResponse) {
        var urlString = "\(serverURL!)\(path)"
        if !queryParams.isEmpty {
            let query = queryParams.map { "\($0.key)=\($0.value)" }.joined(separator: "&")
            urlString += "?\(query)"
        }
        guard let url = URL(string: urlString) else {
            throw URLError(.badURL)
        }
        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let httpResponse = response as? HTTPURLResponse else {
            throw URLError(.badServerResponse)
        }
        return (data, httpResponse)
    }

    // MARK: - Tests

    func testServerCallsClient_Nix_VoidMethod() async throws {
        let connId = try await getConnectionId()

        // Register handler for Nix (void method)
        await connection.onServerMethod("TestShared.ITestClientMethods|Nix") { _ in
            return AnyCodable(Optional<String>.none as Any)
        }

        let (_, response) = try await triggerServerEndpoint(
            "/__test/trigger-client-typed-call",
            queryParams: ["connectionId": connId]
        )
        XCTAssertEqual(response.statusCode, 200)
    }

    func testServerCallsClient_GetById_ReturnsString() async throws {
        let connId = try await getConnectionId()

        await connection.onServerMethod("TestShared.ITestClientMethods|GetById") { args in
            guard let id = args.first as? String else {
                return AnyCodable(Optional<String>.none as Any)
            }
            return AnyCodable(stringLiteral: id)
        }

        let (data, response) = try await triggerServerEndpoint(
            "/__test/trigger-client-getbyid",
            queryParams: ["connectionId": connId, "id": "test-42"]
        )

        guard response.statusCode == 200 else {
            let body = String(data: data, encoding: .utf8) ?? "no body"
            XCTFail("Server returned \(response.statusCode): \(body)")
            return
        }

        let result = String(data: data, encoding: .utf8) ?? ""
        XCTAssert(result.contains("test-42"), "Expected result to contain 'test-42', got: \(result)")
    }

    func testServerCallsClient_GetContent_ReturnsList() async throws {
        let connId = try await getConnectionId()

        await connection.onServerMethod("TestShared.ITestClientMethods|GetContent") { args in
            let count = (args.first as? Int) ?? 3
            var items: [Any] = []
            for i in 0..<count {
                items.append("item-\(i)")
            }
            return AnyCodable(items)
        }

        let (data, response) = try await triggerServerEndpoint(
            "/__test/trigger-client-getcontent",
            queryParams: ["connectionId": connId, "count": "3"]
        )

        guard response.statusCode == 200 else {
            let body = String(data: data, encoding: .utf8) ?? "no body"
            XCTFail("Server returned \(response.statusCode): \(body)")
            return
        }

        let result = String(data: data, encoding: .utf8) ?? ""
        XCTAssert(result.contains("item-0"), "Expected item-0 in result: \(result)")
        XCTAssert(result.contains("item-2"), "Expected item-2 in result: \(result)")
    }

    func testServerCallsClient_StreamNumbers() async throws {
        let connId = try await getConnectionId()

        await connection.onServerStreamMethod("TestShared.ITestClientMethods|StreamNumbers") { args in
            let count = (args.first as? Int) ?? 5
            return AsyncThrowingStream { continuation in
                Task {
                    for i in 0..<count {
                        try await Task.sleep(nanoseconds: 10_000_000) // 10ms
                        continuation.yield(AnyCodable(i))
                    }
                    continuation.finish()
                }
            }
        }

        let (data, response) = try await triggerServerEndpoint(
            "/__test/trigger-client-stream",
            queryParams: ["connectionId": connId, "count": "4"]
        )

        guard response.statusCode == 200 else {
            let body = String(data: data, encoding: .utf8) ?? "no body"
            XCTFail("Server returned \(response.statusCode): \(body)")
            return
        }

        let result = String(data: data, encoding: .utf8) ?? ""
        XCTAssert(result.contains("0"), "Expected 0 in stream result: \(result)")
        XCTAssert(result.contains("3"), "Expected 3 in stream result: \(result)")
    }

}
