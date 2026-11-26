using Microsoft.CodeAnalysis;

namespace Cocoar.SignalARRR.SourceGenerator;

internal static class Diagnostics {
    public static readonly DiagnosticDescriptor LanguageVersionTooLow = new(
        id: "SARRR001",
        title: "SignalARRR needs C# 10",
        messageFormat: "The code SignalARRR generates for [SignalARRRContract] interfaces needs C# 10 or later, but this project compiles with C# {0}. Set <LangVersion>10</LangVersion> (or 'latest') in the project file.",
        category: "SignalARRR",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousServerMethod = new(
        id: "SARRR002",
        title: "Server methods are indistinguishable on the wire",
        messageFormat: "{0} and {1} are both reachable as '{2}' with the same argument count. The wire carries no parameter types, so methods sharing a name must differ in argument count; rename one of them.",
        category: "SignalARRR",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidGenericArguments = new(
        id: "SARRR003",
        title: "Generic server method without declared type arguments",
        messageFormat: "The generic server method '{0}' cannot be called: {1}. A client names the type argument as a string, and the server only calls instantiations it knows at compile time.",
        category: "SignalARRR",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidWireName = new(
        id: "SARRR004",
        title: "Invalid wire name",
        messageFormat: "[MessageName(\"{0}\")] on '{1}' cannot be called: a wire name must not be empty or contain '|', which separates the interface from the method on the wire",
        category: "SignalARRR",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
