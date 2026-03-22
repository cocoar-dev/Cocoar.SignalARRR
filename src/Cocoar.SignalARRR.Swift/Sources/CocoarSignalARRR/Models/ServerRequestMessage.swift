import Foundation

/// Message sent from server to client for RPC calls and events.
///
/// JSON keys are PascalCase to match the .NET server's `System.Text.Json` default.
public struct ServerRequestMessage: Codable, Sendable {
    public var id: String
    public var method: String
    public var arguments: [AnyCodable]?
    public var genericArguments: [String]?
    public var cancellationGuid: String?
    public var streamId: String?

    public init(
        id: String = UUID().uuidString,
        method: String,
        arguments: [AnyCodable]? = nil,
        genericArguments: [String]? = nil,
        cancellationGuid: String? = nil,
        streamId: String? = nil
    ) {
        self.id = id
        self.method = method
        self.arguments = arguments
        self.genericArguments = genericArguments
        self.cancellationGuid = cancellationGuid
        self.streamId = streamId
    }

    enum CodingKeys: String, CodingKey {
        case id = "Id"
        case method = "Method"
        case arguments = "Arguments"
        case genericArguments = "GenericArguments"
        case cancellationGuid = "CancellationGuid"
        case streamId = "StreamId"
    }
}
