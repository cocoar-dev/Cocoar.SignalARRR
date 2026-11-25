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
}
