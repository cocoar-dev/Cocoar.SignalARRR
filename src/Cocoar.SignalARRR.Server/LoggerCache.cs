using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// Process-wide logger cache. <c>LoggerFactory.CreateLogger</c> serializes on a process-wide
    /// lock, and the message pipeline used to call it per message — every RPC on every connection
    /// queued on that one lock (P-1). Loggers are immutable and category names are types here, so
    /// one instance per type for the process lifetime is exactly right.
    /// </summary>
    internal static class LoggerCache {

        private static readonly ConcurrentDictionary<string, ILogger> Cache = new();

        internal static ILogger For(IServiceProvider serviceProvider, Type type) =>
            For(serviceProvider, type.FullName!);

        internal static ILogger For(IServiceProvider serviceProvider, string category) {
            if (Cache.TryGetValue(category, out var cached)) {
                return cached;
            }

            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(category)
                ?? (ILogger)NullLogger.Instance;

            // A NullLogger is not cached: it only means no ILoggerFactory was registered in THIS
            // scope's provider — a later resolution with logging configured must not be pinned to
            // the fallback forever.
            if (ReferenceEquals(logger, NullLogger.Instance)) {
                return logger;
            }

            return Cache.GetOrAdd(category, logger);
        }
    }
}
