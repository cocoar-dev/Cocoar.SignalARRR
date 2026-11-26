using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Tests;

/// <summary>The method tables AddSignalARRR builds from the dispatch the generator produced for an assembly.</summary>
internal static class ServerTables {

    public static ServerMethodTable For<THub>(Assembly? assembly = null) where THub : HARRR => For(typeof(THub), assembly ?? typeof(THub).Assembly);

    public static ServerMethodTable For(Type hubType, Assembly assembly) {
        var services = new ServiceCollection();
        services.AddSignalARRR(b => b.AddServerMethodsFrom(assembly));
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ServerMethodTables>().For(hubType);
    }

    public static bool IsRegistered(this ServerMethodTable table, string name, int argumentCount = 0) {
        try {
            table.Resolve(name, argumentCount);
            return true;
        } catch (Exception) {
            return false;
        }
    }
}

/// <summary>
/// Compiles a C# snippet with the SignalARRR source generator, for what must not compile in the test
/// assembly itself: ambiguous overloads, invalid wire names, generic methods without declared types.
/// A snippet that compiles is loaded, so AddSignalARRR can build its tables.
/// </summary>
internal static class GeneratorHarness {

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(() => {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).ToList();
        paths.AddRange(new[] {
            typeof(HARRR).Assembly.Location,
            typeof(Cocoar.SignalARRR.Common.WireName).Assembly.Location,
            typeof(Cocoar.SignalARRR.Contracts.SignalARRRContractAttribute).Assembly.Location,
            typeof(Cocoar.SignalARRR.ProxyGenerator.ProxyCreator).Assembly.Location,
        });
        return paths.Where(File.Exists).Distinct().Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToImmutableArray();
    });

    private static int _counter;

    public sealed class Result {
        public ImmutableArray<Diagnostic> Diagnostics { get; init; }
        public string GeneratedServerDispatch { get; init; } = "";
        public Assembly? Assembly { get; init; }

        public IEnumerable<Diagnostic> Errors => Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error);
    }

    public static Result Compile(string source) {
        var compilation = CSharpCompilation.Create(
            $"GeneratorHarness{System.Threading.Interlocked.Increment(ref _counter)}",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new SignalARRRGenerator().AsSourceGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        var generated = driver.GetRunResult().GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("SignalARRRServerDispatch.g.cs", StringComparison.Ordinal))?.ToString() ?? "";

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        var diagnostics = generatorDiagnostics.AddRange(emit.Diagnostics);

        return new Result {
            Diagnostics = diagnostics,
            GeneratedServerDispatch = generated,
            Assembly = emit.Success ? Assembly.Load(stream.ToArray()) : null,
        };
    }

    public const string Usings = @"
using System;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
";
}
