import Foundation
import XCTest
@testable import CocoarSignalARRR

final class StreamingTests: IntegrationTestBase {

    func testStreamReceivesAllItems() async throws {
        let stream: AsyncThrowingStream<Int, Error> = try await connection.stream("Counter", arguments: 5, 10)

        var collected: [Int] = []
        for try await item in stream {
            collected.append(item)
        }

        XCTAssertEqual(collected, [0, 1, 2, 3, 4])
    }
}
