import Foundation

/// Message sent from client to server for RPC calls.
///
/// JSON keys are PascalCase to match the .NET server's `System.Text.Json` default.
public struct ClientRequestMessage: Codable, Sendable {
    public var method: String
    public var arguments: [AnyCodable]
    public var authorization: String
    public var genericArguments: [String]

    public init(
        method: String,
        arguments: [AnyCodable] = [],
        authorization: String = "",
        genericArguments: [String] = []
    ) {
        self.method = method
        self.arguments = arguments
        self.authorization = authorization
        self.genericArguments = genericArguments
    }

    enum CodingKeys: String, CodingKey {
        case method = "Method"
        case arguments = "Arguments"
        case authorization = "Authorization"
        case genericArguments = "GenericArguments"
    }
}
