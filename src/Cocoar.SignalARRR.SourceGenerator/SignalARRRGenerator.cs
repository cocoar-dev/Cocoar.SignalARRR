using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Cocoar.SignalARRR.SourceGenerator.Emitters;
using Cocoar.SignalARRR.SourceGenerator.Helpers;
using Cocoar.SignalARRR.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cocoar.SignalARRR.SourceGenerator;

[Generator]
public class SignalARRRGenerator : IIncrementalGenerator {
    private const string AttributeFullName = "Cocoar.SignalARRR.Contracts.SignalARRRContractAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var interfaceDeclarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            AttributeFullName,
            predicate: static (node, _) => node is InterfaceDeclarationSyntax,
            transform: static (ctx, ct) => ExtractInterfaceInfo(ctx, ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!.Value);

        var collected = interfaceDeclarations.Collect();

        context.RegisterSourceOutput(collected, static (spc, interfaces) => {
            if (interfaces.IsDefaultOrEmpty)
                return;

            foreach (var info in interfaces) {
                var proxySource = ProxyEmitter.Emit(info);
                spc.AddSource($"{info.InterfaceName}.SignalARRRProxy.g.cs", proxySource);
            }

            var registrationSource = RegistrationEmitter.Emit(interfaces.ToList());
            if (!string.IsNullOrEmpty(registrationSource)) {
                spc.AddSource("SignalARRRProxyRegistration.g.cs", registrationSource);
            }
        });
    }

    private static ContractInterfaceInfo? ExtractInterfaceInfo(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct) {
        if (context.TargetSymbol is not INamedTypeSymbol interfaceSymbol)
            return null;

        ct.ThrowIfCancellationRequested();

        var ns = interfaceSymbol.ContainingNamespace.ToDisplayString();
        var interfaceName = interfaceSymbol.Name;
        var fullName = interfaceSymbol.ToDisplayString();

        // Strip leading 'I' for proxy class name
        var proxyClassName = interfaceName.StartsWith("I") && interfaceName.Length > 1 && char.IsUpper(interfaceName[1])
            ? interfaceName.Substring(1) + "Proxy"
            : interfaceName + "Proxy";

        var methods = interfaceSymbol.GetAllInterfaceMethods()
            .Select(m => ExtractMethodInfo(m, ct))
            .ToArray();

        return new ContractInterfaceInfo(
            ns,
            interfaceName,
            fullName,
            proxyClassName,
            new EquatableArray<ContractMethodInfo>(methods));
    }

    private static ContractMethodInfo ExtractMethodInfo(IMethodSymbol method, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();

        var (category, elementType) = ReturnTypeClassifier.Classify(method.ReturnType);

        var parameters = method.Parameters
            .Select(p => p.ToParameterInfo())
            .ToArray();

        var typeParameterNames = method.TypeParameters
            .Select(tp => tp.Name)
            .ToArray();

        return new ContractMethodInfo(
            name: method.Name,
            returnCategory: category,
            returnTypeFullName: method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            elementTypeFullName: elementType,
            parameters: new EquatableArray<ContractParameterInfo>(parameters),
            typeParameterNames: new EquatableArray<string>(typeParameterNames));
    }
}
