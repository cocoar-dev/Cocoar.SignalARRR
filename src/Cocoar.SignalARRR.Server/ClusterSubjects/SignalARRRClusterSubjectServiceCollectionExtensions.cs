using System;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection {
    public static class SignalARRRClusterSubjectServiceCollectionExtensions {
        /// <summary>
        /// Registers an <see cref="IClusterSubject{T}"/> named <paramref name="name"/>: an
        /// observable whose events reach subscribers on every node when a backplane is configured,
        /// and a plain local subject when none is. One subject per event type; the name is a
        /// cluster-wide identifier and must match on every node.
        /// </summary>
        public static IServiceCollection AddSignalARRRClusterSubject<T>(
            this IServiceCollection serviceCollection,
            string name,
            Action<ClusterSubjectOptions>? configure = null) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("A cluster subject needs a name; it is how the nodes match events to subjects.", nameof(name));
            }

            var options = new ClusterSubjectOptions();
            configure?.Invoke(options);

            serviceCollection.TryAddSingleton<ClusterSubjectRegistry>();
            serviceCollection.AddSingleton<IClusterSubject<T>>(sp => new ClusterSubject<T>(
                name,
                options,
                sp.GetRequiredService<ISignalARRRBackplane>(),
                sp.GetRequiredService<ClusterSubjectRegistry>(),
                sp.GetRequiredService<ILogger<ClusterSubject<T>>>()));

            // Constructed at startup, not on first use: a subject nobody has resolved yet is not
            // in the registry, and events from other nodes for it would be dropped until then.
            serviceCollection.AddSingleton<IHostedService>(sp => new ClusterSubjectActivator(() => sp.GetRequiredService<IClusterSubject<T>>()));

            return serviceCollection;
        }

        private sealed class ClusterSubjectActivator : IHostedService {
            private readonly Func<object> _resolve;

            public ClusterSubjectActivator(Func<object> resolve) {
                _resolve = resolve;
            }

            public Task StartAsync(CancellationToken cancellationToken) {
                _resolve();
                return Task.CompletedTask;
            }

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
