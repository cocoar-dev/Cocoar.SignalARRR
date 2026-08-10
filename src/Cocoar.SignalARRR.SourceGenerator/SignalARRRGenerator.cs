using System.Collections.Generic;
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

        // Referenced assembly discovery — separate pipeline
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(collected),
            static (spc, pair) => {
                var (compilation, localInfos) = pair;

                var alreadyGenerated = new HashSet<string>();
                foreach (var local in localInfos) {
                    alreadyGenerated.Add(local.FullName);
                }

                var results = new List<ContractInterfaceInfo>();

                foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols) {
                    // Only scan assemblies that reference Cocoar.SignalARRR.Contracts
                    var referencesContracts = false;
                    foreach (var module in reference.Modules) {
                        foreach (var asmRef in module.ReferencedAssemblySymbols) {
                            if (asmRef.Name == "Cocoar.SignalARRR.Contracts") {
                                referencesContracts = true;
                                break;
                            }
                        }
                        if (referencesContracts) break;
                    }

                    if (!referencesContracts) continue;

                    ScanNamespace(reference.GlobalNamespace, results, alreadyGenerated);
                }

                if (results.Count == 0) return;

                foreach (var info in results) {
                    spc.AddSource($"{info.InterfaceName}.Ref.SignalARRRProxy.g.cs", ProxyEmitter.Emit(info));
                }

                spc.AddSource("SignalARRRProxyRegistration.Ref.g.cs", RegistrationEmitter.EmitReferenced(results));
            });
    }

    private static ContractInterfaceInfo? ExtractInterfaceInfo(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct) {
        if (context.TargetSymbol is not INamedTypeSymbol interfaceSymbol)
            return null;

        return ExtractFromSymbol(interfaceSymbol, ct);
    }

    private static ContractInterfaceInfo? ExtractFromSymbol(
        INamedTypeSymbol interfaceSymbol,
        CancellationToken ct) {
        ct.ThrowIfCancellationRequested();

        var ns = interfaceSymbol.ContainingNamespace.ToDisplayString();
        var interfaceName = interfaceSymbol.Name;
        var fullName = interfaceSymbol.ToDisplayString();

        var proxyClassName = interfaceName.StartsWith("I") && interfaceName.Length > 1 && char.IsUpper(interfaceName[1])
            ? interfaceName.Substring(1) + "Proxy"
            : interfaceName + "Proxy";

        var methods = interfaceSymbol.GetAllInterfaceMethods()
            .Select(m => ExtractMethodInfo(m, ct))
            .ToArray();

        return new ContractInterfaceInfo(
            ns, interfaceName, fullName, MessageName(interfaceSymbol) ?? fullName, proxyClassName,
            new EquatableArray<ContractMethodInfo>(methods));
    }

    private static void ScanNamespace(
        INamespaceSymbol ns,
        List<ContractInterfaceInfo> results,
        HashSet<string> exclude) {

        foreach (var type in ns.GetTypeMembers()) {
            if (type.TypeKind == TypeKind.Interface &&
                type.DeclaredAccessibility == Accessibility.Public &&
                HasSignalARRRContractAttribute(type)) {

                var fullName = type.ToDisplayString();
                if (exclude.Contains(fullName)) continue;

                var info = ExtractFromSymbol(type, CancellationToken.None);
                if (info.HasValue) results.Add(info.Value);
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers()) {
            ScanNamespace(childNs, results, exclude);
        }
    }

    private static bool HasSignalARRRContractAttribute(INamedTypeSymbol type) {
        foreach (var attr in type.GetAttributes()) {
            var attrClass = attr.AttributeClass;
            if (attrClass != null &&
                attrClass.Name == "SignalARRRContractAttribute" &&
                attrClass.ContainingNamespace.ToDisplayString() == "Cocoar.SignalARRR.Contracts") {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The name declared with [MessageName], or null. The generator cannot call
    /// Cocoar.SignalARRR.Common.WireName -- it runs against Roslyn symbols, not loaded types -- so it
    /// applies the same rule here. Both sides have to agree: the proxy emits the name and the
    /// registration indexes it, and a disagreement means "method not found" at runtime.
    /// </summary>
    private static string? MessageName(ISymbol symbol) {
        foreach (var attr in symbol.GetAttributes()) {
            var attrClass = attr.AttributeClass;
            if (attrClass == null ||
                attrClass.Name != "MessageNameAttribute" ||
                attrClass.ContainingNamespace.ToDisplayString() != "Cocoar.SignalARRR.Common.Attributes") {
                continue;
            }

            if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string name) {
                return name;
            }
        }

        return null;
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
            wireName: MessageName(method) ?? method.Name,
            returnCategory: category,
            returnTypeFullName: method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            elementTypeFullName: elementType,
            parameters: new EquatableArray<ContractParameterInfo>(parameters),
            typeParameterNames: new EquatableArray<string>(typeParameterNames));
    }
}
