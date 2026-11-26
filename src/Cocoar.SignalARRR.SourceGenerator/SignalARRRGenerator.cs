using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Cocoar.SignalARRR.SourceGenerator.Emitters;
using Cocoar.SignalARRR.SourceGenerator.Helpers;
using Cocoar.SignalARRR.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
                spc.AddSource($"{info.InterfaceName}.SignalARRRDispatcher.g.cs", DispatcherEmitter.Emit(info));
            }

            var registrationSource = RegistrationEmitter.Emit(interfaces.ToList());
            if (!string.IsNullOrEmpty(registrationSource)) {
                spc.AddSource("SignalARRRProxyRegistration.g.cs", registrationSource);
            }
        });

        // Targets without ModuleInitializerAttribute (netstandard2.0, .NET Framework) get an internal
        // copy: the compiler recognizes the attribute by name, and every CLR runs module
        // initializers. Without it the registrations below would not compile there.
        var needsModuleInitializerPolyfill = context.CompilationProvider.Select(static (compilation, _) =>
            NeedsModuleInitializerPolyfill(compilation));
        context.RegisterSourceOutput(needsModuleInitializerPolyfill, static (spc, needed) => {
            if (needed) {
                spc.AddSource("SignalARRRModuleInitializerAttribute.g.cs", RegistrationEmitter.ModuleInitializerPolyfill);
            }
        });

        // The generated code uses C# 10 syntax. A .NET Framework project defaults to C# 7.3 and
        // would otherwise fail with a pile of syntax errors inside generated files.
        var languageVersion = context.ParseOptionsProvider.Select(static (options, _) =>
            options is CSharpParseOptions csharp ? (int)csharp.LanguageVersion : int.MaxValue);
        context.RegisterSourceOutput(languageVersion.Combine(collected), static (spc, pair) => {
            var (version, interfaces) = pair;
            if (!interfaces.IsDefaultOrEmpty && version < (int)LanguageVersion.CSharp10) {
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.LanguageVersionTooLow, Location.None, ((LanguageVersion)version).ToDisplayString()));
            }
        });

        // The server's method tables: hubs and ServerMethods classes of this compilation.
        context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) => {
            var (source, diagnostics) = Server.ServerTableBuilder.Build(compilation);
            foreach (var diagnostic in diagnostics) {
                spc.ReportDiagnostic(diagnostic);
            }
            if (source != null) {
                spc.AddSource("SignalARRRServerDispatch.g.cs", source);
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

                    ScanNamespace(reference.GlobalNamespace, reference, compilation, results, alreadyGenerated);
                }

                if (results.Count == 0) return;

                foreach (var info in results) {
                    spc.AddSource($"{info.InterfaceName}.Ref.SignalARRRProxy.g.cs", ProxyEmitter.Emit(info));
                    spc.AddSource($"{info.InterfaceName}.Ref.SignalARRRDispatcher.g.cs", DispatcherEmitter.Emit(info));
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

        var dispatcherClassName = proxyClassName.Substring(0, proxyClassName.Length - "Proxy".Length) + "Dispatcher";

        var methods = interfaceSymbol.GetAllInterfaceMethods()
            .Select(m => ExtractMethodInfo(m.Method, m.IsInherited, ct))
            .ToArray();

        return new ContractInterfaceInfo(
            ns, interfaceName, fullName, MessageName(interfaceSymbol) ?? fullName, proxyClassName, dispatcherClassName,
            new EquatableArray<ContractMethodInfo>(methods));
    }

    private static void ScanNamespace(
        INamespaceSymbol ns,
        IAssemblySymbol assembly,
        Compilation compilation,
        List<ContractInterfaceInfo> results,
        HashSet<string> exclude) {

        foreach (var type in ns.GetTypeMembers()) {
            if (type.TypeKind == TypeKind.Interface &&
                type.DeclaredAccessibility == Accessibility.Public &&
                HasSignalARRRContractAttribute(type)) {

                var fullName = type.ToDisplayString();
                if (exclude.Contains(fullName)) continue;

                var info = ExtractFromSymbol(type, CancellationToken.None);
                if (!info.HasValue) continue;

                // The referenced assembly generated this proxy itself and registers it in its own
                // module initializer. Its proxy is internal, so usually invisible here — but with
                // InternalsVisibleTo (a test project of an API, typically) a second copy generated
                // here would clash with it (CS0436). Leave it to the one that is already there.
                if (HasVisibleProxy(assembly, compilation, info.Value)) continue;

                results.Add(info.Value);
            }
        }

        foreach (var childNs in ns.GetNamespaceMembers()) {
            ScanNamespace(childNs, assembly, compilation, results, exclude);
        }
    }

    private static bool NeedsModuleInitializerPolyfill(Compilation compilation) {
        var attribute = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute");
        if (attribute != null && compilation.IsSymbolAccessibleWithin(attribute, compilation.Assembly)) {
            return false;
        }

        // Only where SignalARRR's generated registrations can appear at all.
        foreach (var reference in compilation.ReferencedAssemblyNames) {
            if (reference.Name == "Cocoar.SignalARRR.ProxyGenerator") return true;
        }

        return false;
    }

    private static bool HasVisibleProxy(IAssemblySymbol assembly, Compilation compilation, ContractInterfaceInfo info) {
        var proxy = assembly.GetTypeByMetadataName($"{info.Namespace}.SignalARRR.{info.ProxyClassName}");
        return proxy != null && compilation.IsSymbolAccessibleWithin(proxy, compilation.Assembly);
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

    private static ContractMethodInfo ExtractMethodInfo(IMethodSymbol method, bool isInherited, CancellationToken ct) {
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
            typeParameterNames: new EquatableArray<string>(typeParameterNames),
            isInherited: isInherited,
            declaringInterface: method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }
}