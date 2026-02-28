using System.Collections.Generic;
using System.Linq;
using Cocoar.SignalARRR.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace Cocoar.SignalARRR.SourceGenerator.Helpers;

internal static class SymbolExtensions {
    public static string ToFullyQualifiedString(this ITypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public static IEnumerable<IMethodSymbol> GetAllInterfaceMethods(this INamedTypeSymbol interfaceSymbol) {
        foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
            yield return method;

        foreach (var baseInterface in interfaceSymbol.AllInterfaces) {
            foreach (var method in baseInterface.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
                yield return method;
        }
    }

    public static ContractParameterInfo ToParameterInfo(this IParameterSymbol parameter) {
        var isCancellationToken = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == "global::System.Threading.CancellationToken";

        string? defaultValueLiteral = null;
        if (parameter.HasExplicitDefaultValue) {
            defaultValueLiteral = FormatDefaultValue(parameter);
        }

        var typeFullName = parameter.Type.ToFullyQualifiedString();
        if (parameter.NullableAnnotation == NullableAnnotation.Annotated
            && !typeFullName.EndsWith("?"))
            typeFullName += "?";

        return new ContractParameterInfo(
            typeFullName: typeFullName,
            name: parameter.Name,
            hasDefaultValue: parameter.HasExplicitDefaultValue,
            defaultValueLiteral: defaultValueLiteral,
            isCancellationToken: isCancellationToken);
    }

    private static string FormatDefaultValue(IParameterSymbol parameter) {
        if (!parameter.HasExplicitDefaultValue)
            return "default";

        var value = parameter.ExplicitDefaultValue;

        if (value is null)
            return "default";

        if (value is string s)
            return $"\"{EscapeString(s)}\"";

        if (value is bool b)
            return b ? "true" : "false";

        if (value is char c)
            return $"'{EscapeChar(c)}'";

        if (value is float f)
            return f.ToString("R") + "f";

        if (value is double d)
            return d.ToString("R") + "d";

        if (value is decimal m)
            return m.ToString() + "m";

        if (value is long l)
            return l.ToString() + "L";

        if (value is ulong ul)
            return ul.ToString() + "UL";

        return value.ToString()!;
    }

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    private static string EscapeChar(char c) =>
        c switch {
            '\\' => "\\\\",
            '\'' => "\\'",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ => c.ToString()
        };
}
