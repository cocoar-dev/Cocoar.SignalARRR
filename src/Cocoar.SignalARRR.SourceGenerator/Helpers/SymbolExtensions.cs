using System.Collections.Generic;
using System.Linq;
using Cocoar.SignalARRR.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace Cocoar.SignalARRR.SourceGenerator.Helpers;

internal static class SymbolExtensions {
    public static string ToFullyQualifiedString(this ITypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>The contract's own methods first, then those of every interface it inherits.</summary>
    public static IEnumerable<(IMethodSymbol Method, bool IsInherited)> GetAllInterfaceMethods(this INamedTypeSymbol interfaceSymbol) {
        foreach (var method in interfaceSymbol.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
            yield return (method, false);

        foreach (var baseInterface in interfaceSymbol.AllInterfaces) {
            foreach (var method in baseInterface.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
                yield return (method, true);
        }
    }

    public static ContractParameterInfo ToParameterInfo(this IParameterSymbol parameter) {
        var isCancellationToken = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == "global::System.Threading.CancellationToken";

        string? defaultValueLiteral = null;
        if (parameter.HasExplicitDefaultValue) {
            defaultValueLiteral = FormatDefaultValue(parameter);
        }

        var clrTypeName = parameter.Type.ToFullyQualifiedString();
        var isStream = clrTypeName == "global::System.IO.Stream";
        var typeFullName = clrTypeName;
        if (parameter.NullableAnnotation == NullableAnnotation.Annotated
            && !typeFullName.EndsWith("?"))
            typeFullName += "?";

        return new ContractParameterInfo(
            typeFullName: typeFullName,
            name: parameter.Name,
            hasDefaultValue: parameter.HasExplicitDefaultValue,
            defaultValueLiteral: defaultValueLiteral,
            isCancellationToken: isCancellationToken,
            clrTypeName: clrTypeName,
            isStream: isStream);
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