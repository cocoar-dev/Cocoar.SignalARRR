import Foundation
import XCTest
@testable import CocoarSignalARRR

final class AdvancedFeatureTests: IntegrationTestBase {

    func testFromServicesInjection() async throws {
        // Server's ExtraMethods.GetServiceInfo has [FromServices] IServiceProvider param
        // The parameter is injected by DI, not sent by the client
        let result: String = try await connection.invoke("ExtraMethods.GetServiceInfo")
        XCTAssertEqual(result, "ServiceProviderInjected")
    }
}
