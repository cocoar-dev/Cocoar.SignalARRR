import SwiftSyntaxMacros
import SwiftSyntaxMacrosTestSupport
import XCTest
import CocoarSignalARRRMacroPlugin

final class HubProxyMacroTests: XCTestCase {
    let testMacros: [String: Macro.Type] = [
        "HubProxy": HubProxyMacro.self,
    ]

    func testVoidMethod() {
        assertMacroExpansion(
            """
            @HubProxy
            protocol IChatHub {
                func sendMessage(user: String, message: String) async throws
            }
            """,
            expandedSource: """
            protocol IChatHub {
                func sendMessage(user: String, message: String) async throws
            }

            public final class IChatHubProxy: IChatHub, HubProxyProtocol {
                private let connection: HARRRConnection
                private static let prefix = "IChatHub"

                public init(connection: HARRRConnection) {
                    self.connection = connection
                }

                public func sendMessage(user: String, message: String) async throws {
                    try await connection.send("\\(Self.prefix)|sendMessage", arguments: user, message)
                }
            }
            """,
            macros: testMacros
        )
    }

    func testReturnValueMethod() {
        assertMacroExpansion(
            """
            @HubProxy
            protocol IChatHub {
                func getHistory() async throws -> [String]
            }
            """,
            expandedSource: """
            protocol IChatHub {
                func getHistory() async throws -> [String]
            }

            public final class IChatHubProxy: IChatHub, HubProxyProtocol {
                private let connection: HARRRConnection
                private static let prefix = "IChatHub"

                public init(connection: HARRRConnection) {
                    self.connection = connection
                }

                public func getHistory() async throws -> [String] {
                    try await connection.invoke("\\(Self.prefix)|getHistory") as [String]
                }
            }
            """,
            macros: testMacros
        )
    }

    func testStreamMethod() {
        assertMacroExpansion(
            """
            @HubProxy
            protocol IChatHub {
                func streamMessages() async throws -> AsyncThrowingStream<String, Error>
            }
            """,
            expandedSource: """
            protocol IChatHub {
                func streamMessages() async throws -> AsyncThrowingStream<String, Error>
            }

            public final class IChatHubProxy: IChatHub, HubProxyProtocol {
                private let connection: HARRRConnection
                private static let prefix = "IChatHub"

                public init(connection: HARRRConnection) {
                    self.connection = connection
                }

                public func streamMessages() async throws -> AsyncThrowingStream<String, Error> {
                    try await connection.stream("\\(Self.prefix)|streamMessages") as AsyncThrowingStream<String, Error>
                }
            }
            """,
            macros: testMacros
        )
    }

    func testMixedMethods() {
        assertMacroExpansion(
            """
            @HubProxy
            protocol IChatHub {
                func sendMessage(user: String, message: String) async throws
                func getHistory() async throws -> [String]
                func streamMessages() async throws -> AsyncThrowingStream<String, Error>
            }
            """,
            expandedSource: """
            protocol IChatHub {
                func sendMessage(user: String, message: String) async throws
                func getHistory() async throws -> [String]
                func streamMessages() async throws -> AsyncThrowingStream<String, Error>
            }

            public final class IChatHubProxy: IChatHub, HubProxyProtocol {
                private let connection: HARRRConnection
                private static let prefix = "IChatHub"

                public init(connection: HARRRConnection) {
                    self.connection = connection
                }

                public func sendMessage(user: String, message: String) async throws {
                    try await connection.send("\\(Self.prefix)|sendMessage", arguments: user, message)
                }

                public func getHistory() async throws -> [String] {
                    try await connection.invoke("\\(Self.prefix)|getHistory") as [String]
                }

                public func streamMessages() async throws -> AsyncThrowingStream<String, Error> {
                    try await connection.stream("\\(Self.prefix)|streamMessages") as AsyncThrowingStream<String, Error>
                }
            }
            """,
            macros: testMacros
        )
    }

    func testAppliedToNonProtocolProducesError() {
        assertMacroExpansion(
            """
            @HubProxy
            class NotAProtocol {}
            """,
            expandedSource: """
            class NotAProtocol {}
            """,
            diagnostics: [
                DiagnosticSpec(message: "@HubProxy can only be applied to a protocol declaration", line: 1, column: 1),
            ],
            macros: testMacros
        )
    }

    func testNoParameterMethod() {
        assertMacroExpansion(
            """
            @HubProxy
            protocol IPingHub {
                func ping() async throws
            }
            """,
            expandedSource: """
            protocol IPingHub {
                func ping() async throws
            }

            public final class IPingHubProxy: IPingHub, HubProxyProtocol {
                private let connection: HARRRConnection
                private static let prefix = "IPingHub"

                public init(connection: HARRRConnection) {
                    self.connection = connection
                }

                public func ping() async throws {
                    try await connection.send("\\(Self.prefix)|ping")
                }
            }
            """,
            macros: testMacros
        )
    }
}
