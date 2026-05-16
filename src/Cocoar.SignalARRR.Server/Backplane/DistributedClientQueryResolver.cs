using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    internal static class DistributedClientQueryResolver {
        public static Task<IReadOnlyList<SignalARRRConnectionRegistration>> ResolveConnectionsAsync(
            IClusterClientQueryMetadata metadata,
            CancellationToken cancellationToken = default) {
            var registry = metadata.ServiceProvider.GetRequiredService<ISignalARRRConnectionRegistry>();
            return registry.FindConnectionsAsync(
                metadata.HubType,
                metadata.GroupName,
                metadata.UserId,
                metadata.AttributeFilters,
                cancellationToken);
        }
    }
}
