using Cocoar.SignalARRR.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace Cocoar.SignalARRR.SourceGenerator.Helpers;

internal static class ReturnTypeClassifier
{
    public static (ReturnTypeCategory Category, string? ElementType) Classify(ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_Void)
            return (ReturnTypeCategory.Void, null);

        var fullName = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Task (non-generic)
        if (fullName == "global::System.Threading.Tasks.Task")
            return (ReturnTypeCategory.Task, null);

        if (returnType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDef = namedType.ConstructedFrom;
            var metadataName = originalDef.MetadataName;
            var nsName = originalDef.ContainingNamespace?.ToDisplayString() ?? "";

            // Task<T>
            if (metadataName == "Task`1" && nsName == "System.Threading.Tasks")
            {
                var elementType = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return (ReturnTypeCategory.TaskOfT, elementType);
            }

            // IObservable<T>
            if (metadataName == "IObservable`1" && nsName == "System")
            {
                var elementType = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return (ReturnTypeCategory.Observable, elementType);
            }

            // ChannelReader<T>
            if (metadataName == "ChannelReader`1" && nsName == "System.Threading.Channels")
            {
                var elementType = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return (ReturnTypeCategory.ChannelReader, elementType);
            }

            // IAsyncEnumerable<T>
            if (metadataName == "IAsyncEnumerable`1" && nsName == "System.Collections.Generic")
            {
                var elementType = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return (ReturnTypeCategory.AsyncEnumerable, elementType);
            }
        }

        // Sync return (non-void, non-Task, non-streaming)
        return (ReturnTypeCategory.SyncReturn, null);
    }
}
