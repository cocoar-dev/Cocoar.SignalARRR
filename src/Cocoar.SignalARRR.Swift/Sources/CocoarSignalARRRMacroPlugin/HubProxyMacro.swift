import SwiftSyntax
import SwiftSyntaxMacros

/// Peer macro that generates a proxy class for a SignalARRR hub protocol.
///
/// Given a protocol declaration annotated with `@HubProxy`, this macro generates
/// a `<ProtocolName>Proxy` class that conforms to both the protocol and `HubProxyProtocol`.
///
/// Each method in the protocol is classified by return type:
/// - `async throws` (void) → `connection.send(...)`
/// - `async throws -> T` → `connection.invoke(...)`
/// - `async throws -> AsyncThrowingStream<T, Error>` → `connection.stream(...)`
public struct HubProxyMacro: PeerMacro {
    public static func expansion(
        of node: AttributeSyntax,
        providingPeersOf declaration: some DeclSyntaxProtocol,
        in context: some MacroExpansionContext
    ) throws -> [DeclSyntax] {
        guard let protocolDecl = declaration.as(ProtocolDeclSyntax.self) else {
            throw MacroError("@HubProxy can only be applied to a protocol declaration")
        }

        let protocolName = protocolDecl.name.trimmedDescription
        let proxyClassName = "\(protocolName)Proxy"

        let methods = protocolDecl.memberBlock.members.compactMap { member -> FunctionDeclSyntax? in
            member.decl.as(FunctionDeclSyntax.self)
        }

        var memberLines: [String] = []

        // Properties and init
        memberLines.append("    private let connection: HARRRConnection")
        memberLines.append("    private static let prefix = \"\(protocolName)\"")
        memberLines.append("")
        memberLines.append("    public init(connection: HARRRConnection) {")
        memberLines.append("        self.connection = connection")
        memberLines.append("    }")

        // Generate method implementations
        for method in methods {
            memberLines.append("")
            let impl = try generateMethodImplementation(method)
            memberLines.append(contentsOf: impl)
        }

        let classDecl = """
        public final class \(proxyClassName): \(protocolName), HubProxyProtocol {
        \(memberLines.joined(separator: "\n"))
        }
        """

        return [DeclSyntax(stringLiteral: classDecl)]
    }

    // MARK: - Method Generation

    private static func generateMethodImplementation(_ method: FunctionDeclSyntax) throws -> [String] {
        let methodName = method.name.trimmedDescription
        let category = classifyReturnType(method)

        let signature = buildSignature(method)
        let argsList = buildArgumentsList(method)

        var lines: [String] = []
        lines.append("    \(signature) {")

        switch category {
        case .void:
            if argsList.isEmpty {
                lines.append("        try await connection.send(\"\\(Self.prefix)|\(methodName)\")")
            } else {
                lines.append("        try await connection.send(\"\\(Self.prefix)|\(methodName)\", arguments: \(argsList))")
            }

        case .returnValue(let typeString):
            if argsList.isEmpty {
                lines.append("        try await connection.invoke(\"\\(Self.prefix)|\(methodName)\") as \(typeString)")
            } else {
                lines.append("        try await connection.invoke(\"\\(Self.prefix)|\(methodName)\", arguments: \(argsList)) as \(typeString)")
            }

        case .stream(let elementType):
            if argsList.isEmpty {
                lines.append("        try await connection.stream(\"\\(Self.prefix)|\(methodName)\") as AsyncThrowingStream<\(elementType), Error>")
            } else {
                lines.append("        try await connection.stream(\"\\(Self.prefix)|\(methodName)\", arguments: \(argsList)) as AsyncThrowingStream<\(elementType), Error>")
            }
        }

        lines.append("    }")
        return lines
    }

    // MARK: - Return Type Classification

    private enum ReturnCategory {
        case void
        case returnValue(String)
        case stream(elementType: String)
    }

    private static func classifyReturnType(_ method: FunctionDeclSyntax) -> ReturnCategory {
        guard let returnClause = method.signature.returnClause else {
            return .void
        }

        let returnType = returnClause.type.trimmedDescription

        // AsyncThrowingStream<Element, Error>
        if returnType.hasPrefix("AsyncThrowingStream<") {
            let inner = extractGenericArguments(from: returnType)
            if let element = inner.first {
                return .stream(elementType: element)
            }
        }

        return .returnValue(returnType)
    }

    /// Extract comma-separated generic arguments from a type string like `Foo<A, B>`.
    private static func extractGenericArguments(from typeString: String) -> [String] {
        guard let openAngle = typeString.firstIndex(of: "<"),
              let closeAngle = typeString.lastIndex(of: ">") else {
            return []
        }
        let inner = typeString[typeString.index(after: openAngle)..<closeAngle]
        return inner.split(separator: ",").map { $0.trimmingCharacters(in: .whitespaces) }
    }

    // MARK: - Signature Building

    private static func buildSignature(_ method: FunctionDeclSyntax) -> String {
        let methodName = method.name.trimmedDescription
        let params = method.signature.parameterClause.parameters

        var paramStrings: [String] = []
        for param in params {
            let label = param.firstName.trimmedDescription
            let localName = param.secondName?.trimmedDescription
            let typeName = param.type.trimmedDescription

            if let localName {
                paramStrings.append("\(label) \(localName): \(typeName)")
            } else {
                paramStrings.append("\(label): \(typeName)")
            }
        }

        let paramList = paramStrings.joined(separator: ", ")
        let effectSpecifiers = method.signature.effectSpecifiers?.trimmedDescription ?? ""
        let returnClause = method.signature.returnClause?.trimmedDescription ?? ""

        var sig = "public func \(methodName)(\(paramList))"
        if !effectSpecifiers.isEmpty {
            sig += " \(effectSpecifiers)"
        }
        if !returnClause.isEmpty {
            sig += " \(returnClause)"
        }
        return sig
    }

    /// Build a comma-separated list of parameter names for passing as arguments.
    private static func buildArgumentsList(_ method: FunctionDeclSyntax) -> String {
        let params = method.signature.parameterClause.parameters
        return params.map { param in
            (param.secondName ?? param.firstName).trimmedDescription
        }.joined(separator: ", ")
    }
}

// MARK: - Error

struct MacroError: Error, CustomStringConvertible {
    let description: String
    init(_ description: String) { self.description = description }
}
