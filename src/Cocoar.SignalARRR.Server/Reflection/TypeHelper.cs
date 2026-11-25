using System;
using System.Collections.Concurrent;

namespace Cocoar.SignalARRR.Common.Helper {
    public static class TypeHelper {

        /// <summary>
        /// Upper bound for the resolution cache.
        /// </summary>
        /// <remarks>
        /// The names reaching this method come off the wire. The cache previously had no bound and
        /// stored every name it was ever asked about — including the ones that resolved to nothing —
        /// so a peer sending distinct names grew it for the lifetime of the process.
        /// </remarks>
        private const int MaxCachedTypes = 1024;

        private static readonly ConcurrentDictionary<string, Type> TypeFromString =
            new ConcurrentDictionary<string, Type>(StringComparer.Ordinal);

        public static Type? FindType(string typeName) {

            if (string.IsNullOrWhiteSpace(typeName))
                return typeof(void);

            if (TypeFromString.TryGetValue(typeName, out var cached))
                return cached;

            var foundType = Resolve(typeName);

            // Only successes are cached. Caching a miss used to make the result permanent, so a type
            // from an assembly loaded later could never be resolved again.
            if (foundType != null && TypeFromString.Count < MaxCachedTypes) {
                TypeFromString.TryAdd(typeName, foundType);
            }

            return foundType;
        }

        private static Type? Resolve(string typeName) {

            if (!typeName.Contains(".")) {
                var systemType = Type.GetType($"System.{typeName}", throwOnError: false, ignoreCase: true);
                if (systemType != null) {
                    return systemType;
                }
            }

            // Only assemblies that are already loaded are searched, and only case-sensitively.
            // The previous case-insensitive second pass could resolve a *different* type than the
            // caller named, which is the wrong answer to give for a value that decides which type a
            // generic method operates on.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                var foundType = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
                if (foundType != null) {
                    return foundType;
                }
            }

            return null;
        }
    }
}
