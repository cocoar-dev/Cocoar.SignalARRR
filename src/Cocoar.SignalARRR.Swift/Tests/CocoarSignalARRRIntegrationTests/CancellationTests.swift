import Foundation
import XCTest
@testable import CocoarSignalARRR

final class CancellationTests: IntegrationTestBase {

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

    func testServerCancelsClientOperation() async throws {
        let connId = try await getConnectionId()

        // Asserted on what this handler experiences, not on the server's answer. The server stops
        // waiting the moment it cancels, so SignalR aborts the pending invocation and reports a
        // HubException — which looks the same whether the token reached us or the call fell over.
        let observedCancellation = XCTestExpectation(description: "the Wait handler observes cancellation")
        let receivedSeconds = XCTestExpectation(description: "the Wait handler receives its seconds argument unshifted")
        // Separate from the one above so a failure names the stage that broke: an unrecognised
        // reference and a token that never fires look identical from the outside otherwise.
        let receivedCancellationHandle = XCTestExpectation(description: "the Wait handler receives a cancellation handle in the token slot")

        // Register the Wait handler that respects cancellation
        await connection.onServerMethod("TestShared.ITestClientMethods|Wait") { [weak self] args in
            guard let self else { return AnyCodable(Optional<String>.none as Any) }
            let seconds = (args.first as? Int) ?? 30
            if seconds == 30 { receivedSeconds.fulfill() }
            let cancellationGuid = args.count > 1 ? (args[1] as? String) : nil
            if cancellationGuid != nil { receivedCancellationHandle.fulfill() }

            if let guid = cancellationGuid {
                // Race between cancellation and actual work
                return try await withThrowingTaskGroup(of: AnyCodable.self) { group in
                    group.addTask {
                        // register(id:) does not return when the server cancels -- the manager
                        // resumes its continuation *throwing*. Signalling after the call would
                        // therefore never run.
                        do {
                            try await self.connection.cancellationManager.register(id: guid)
                        } catch {
                            observedCancellation.fulfill()
                            throw error
                        }
                        throw CancellationError()
                    }
                    group.addTask {
                        try await Task.sleep(nanoseconds: UInt64(seconds) * 1_000_000_000)
                        return AnyCodable(stringLiteral: "done")
                    }
                    let result = try await group.next()!
                    group.cancelAll()
                    return result
                }
            }

            try await Task.sleep(nanoseconds: UInt64(seconds) * 1_000_000_000)
            return AnyCodable(stringLiteral: "done")
        }

        let start = Date()
        let (data, response) = try await triggerServerEndpoint(
            "/__test/trigger-client-cancellation",
            queryParams: ["connectionId": connId, "delayMs": "200"]
        )
        let elapsed = Date().timeIntervalSince(start)

        guard response.statusCode == 200 else {
            let body = String(data: data, encoding: .utf8) ?? "no body"
            XCTFail("Server returned \(response.statusCode): \(body)")
            return
        }

        // The argument arrived where it was sent, and the token the server passed is a working one.
        await fulfillment(of: [receivedSeconds, receivedCancellationHandle, observedCancellation], timeout: 5.0)

        // Must complete in well under 30s — the cancellation should fire after ~200ms
        XCTAssert(elapsed < 5.0, "Cancellation took \(elapsed)s — expected <5s (cancel after 200ms, not 30s wait)")
    }

    func testStreamCancellationStopsStream() async throws {
        // Test that cancelling a stream stops receiving items
        let stream: AsyncThrowingStream<Int, Error> = try await connection.stream("Counter", arguments: 100, 50)

        var collected: [Int] = []
        // Only collect a few items then break
        for try await item in stream {
            collected.append(item)
            if collected.count >= 3 {
                break
            }
        }

        XCTAssertEqual(collected.count, 3)
        XCTAssertEqual(collected, [0, 1, 2])
    }
}

