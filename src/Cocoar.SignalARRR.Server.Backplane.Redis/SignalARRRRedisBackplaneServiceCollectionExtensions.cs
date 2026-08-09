using System;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection {
    public static class SignalARRRRedisBackplaneServiceCollectionExtensions {
        public static IServiceCollection AddSignalARRRRedisBackplane(
            this IServiceCollection serviceCollection,
            Action<SignalARRRRedisBackplaneOptionsBuilder> options) {
            var builder = new SignalARRRRedisBackplaneOptionsBuilder();
            options(builder);
            var configuredOptions = builder.Build();

            if (string.IsNullOrWhiteSpace(configuredOptions.ConnectionString)) {
                throw new InvalidOperationException("SignalARRR Redis backplane requires a connection string.");
            }

            serviceCollection.AddSingleton(configuredOptions);
            serviceCollection.AddSingleton<RedisSignalARRRBackplane>();
            serviceCollection.Replace(ServiceDescriptor.Singleton<ISignalARRRBackplane>(sp => sp.GetRequiredService<RedisSignalARRRBackplane>()));
            serviceCollection.Replace(ServiceDescriptor.Singleton<ISignalARRRConnectionRegistry>(sp => sp.GetRequiredService<RedisSignalARRRBackplane>()));
            serviceCollection.AddSingleton<IHostedService>(sp => sp.GetRequiredService<RedisSignalARRRBackplane>());

            return serviceCollection;
        }
    }
}
