using System;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection {
    public static class SignalARRRPostgresBackplaneServiceCollectionExtensions {
        /// <summary>
        /// Makes SignalARRR cluster-aware over PostgreSQL: broadcasts, cluster queries and the
        /// connection registry go through the database, using <c>LISTEN</c>/<c>NOTIFY</c> for
        /// delivery. Mutually exclusive with the Redis backplane; whichever is registered last wins.
        /// </summary>
        public static IServiceCollection AddSignalARRRPostgresBackplane(
            this IServiceCollection serviceCollection,
            Action<SignalARRRPostgresBackplaneOptionsBuilder> options) {
            var builder = new SignalARRRPostgresBackplaneOptionsBuilder();
            options(builder);
            var configuredOptions = builder.Build();
            configuredOptions.Validate();

            serviceCollection.AddSingleton(configuredOptions);
            serviceCollection.AddSingleton<PostgresSignalARRRBackplane>();
            serviceCollection.Replace(ServiceDescriptor.Singleton<ISignalARRRBackplane>(sp => sp.GetRequiredService<PostgresSignalARRRBackplane>()));
            serviceCollection.Replace(ServiceDescriptor.Singleton<ISignalARRRConnectionRegistry>(sp => sp.GetRequiredService<PostgresSignalARRRBackplane>()));
            serviceCollection.AddSingleton<IHostedService>(sp => sp.GetRequiredService<PostgresSignalARRRBackplane>());

            return serviceCollection;
        }
    }
}
