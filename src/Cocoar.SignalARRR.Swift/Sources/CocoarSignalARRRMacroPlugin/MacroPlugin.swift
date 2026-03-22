import SwiftCompilerPlugin
import SwiftSyntaxMacros

@main
struct CocoarSignalARRRMacroPlugin: CompilerPlugin {
    let providingMacros: [Macro.Type] = [
        HubProxyMacro.self,
    ]
}
