using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Cocoar.SignalARRR.Common.Serialization {
    /// <summary>
    /// Wires SignalARRR's JSON contracts into a set of <see cref="JsonSerializerOptions"/>.
    /// </summary>
    public static class SignalARRRJson {

        /// <summary>
        /// Appends <see cref="SignalARRRJsonContext"/> to the end of the options' resolver chain, so
        /// the app's own resolvers keep precedence.
        /// </summary>
        /// <remarks>
        /// SignalARRR converts arguments with these options. Until 6.0 it used options of its own that
        /// fell back to reflection-based JSON, so a JIT app with a context of its own that misses a
        /// few types still had them converted. Adding a resolver to the chain also ends
        /// System.Text.Json's implicit reflection fallback of an empty chain. So wherever reflection
        /// is enabled at all, the reflection resolver is added as the very last link: nothing an
        /// app's resolvers or this context know changes, and what neither knows keeps working as
        /// before. Under NativeAOT (or with <c>JsonSerializerIsReflectionEnabledByDefault=false</c>)
        /// it is not added; there the app declares its types, and the client checks at start that it did.
        /// <para>
        /// Options that are already in use are read-only and stay as they are.
        /// </para>
        /// </remarks>
        /// <returns><c>false</c> when the options were read-only and could not take the context.</returns>
        public static bool AddTo(JsonSerializerOptions options) {
            if (options.IsReadOnly) {
                return options.TypeInfoResolverChain.Contains(SignalARRRJsonContext.Default);
            }

            var chain = options.TypeInfoResolverChain;
            if (!chain.Contains(SignalARRRJsonContext.Default)) {
                chain.Add(SignalARRRJsonContext.Default);
            }

            if (JsonSerializer.IsReflectionEnabledByDefault && !HasReflectionResolver(chain)) {
                chain.Add(CreateReflectionResolver());
            }

            return true;
        }

        private static bool HasReflectionResolver(System.Collections.Generic.IList<IJsonTypeInfoResolver> chain) {
            foreach (var resolver in chain) {
                if (resolver is DefaultJsonTypeInfoResolver) return true;
            }
            return false;
        }

#if NET
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only reached when JsonSerializer.IsReflectionEnabledByDefault is true, which trimmed and AOT apps switch off.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only reached when JsonSerializer.IsReflectionEnabledByDefault is true, which trimmed and AOT apps switch off.")]
#endif
        private static IJsonTypeInfoResolver CreateReflectionResolver() => new DefaultJsonTypeInfoResolver();
    }
}
