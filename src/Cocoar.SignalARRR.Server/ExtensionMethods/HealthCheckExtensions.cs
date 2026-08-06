using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    public static class HealthCheckExtensions {
        /// <summary>
        /// Registers the SignalARRR health check (name: <c>"signalarrr"</c>): stream/upload
        /// bookkeeping and, when a backplane is configured, store reachability, heartbeat
        /// freshness and heartbeat-loop liveness. Call after <c>AddSignalARRR()</c>; expose via
        /// the standard <c>MapHealthChecks</c> endpoint.
        /// </summary>
        public static IHealthChecksBuilder AddSignalARRRHealthChecks(this IServiceCollection services, string name = "signalarrr") {
            return services.AddHealthChecks().AddCheck<SignalARRRHealthCheck>(name);
        }
    }
}
