using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    /// <summary>
    /// A backplane registered before <c>AddSignalARRR</c> has to survive it.
    /// </summary>
    /// <remarks>
    /// <c>AddSignalARRRRedisBackplane</c> swaps its implementation in with <c>Replace</c>, which adds
    /// when there is nothing to replace. So if it runs first, the disabled default must not be
    /// appended afterwards — with plain <c>AddSingleton</c> it would be, and last-registration-wins
    /// would hand back the disabled one: a configured cluster quietly running single-node, with no
    /// error anywhere. <c>TryAddSingleton</c> is what prevents that, so it is load-bearing rather
    /// than decorative, and worth a test that says so.
    /// </remarks>
    public class BackplaneRegistrationOrderTests {

        private sealed class MarkerBackplane : ISignalARRRBackplane {
            public bool IsEnabled => true;
            public string NodeId => "marker";

            public Task PublishDispatchAsync(Type? hubType, SignalARRRBackplaneTargetKind targetKind, ServerRequestMessage message, IReadOnlyList<string>? connectionIds = null, string? groupName = null, string? userId = null, string? signalRMethodName = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<object?> InvokeConnectionAsync(Type? hubType, string connectionId, ServerRequestMessage message, Type resultType, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
            public Task<IReadOnlyList<SignalARRRBackplaneInvokeResult>> InvokeQueryAsync(Type hubType, SignalARRRBackplaneTargetKind targetKind, ServerRequestMessage message, Type resultType, string? groupName = null, string? userId = null, bool singleResult = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SignalARRRBackplaneInvokeResult>>(Array.Empty<SignalARRRBackplaneInvokeResult>());
            public Task PublishGroupCommandAsync(Type? hubType, string connectionId, string groupName, SignalARRRBackplaneGroupAction action, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task<IReadOnlyList<string>> GetActiveNodesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            public Task PublishClusterEventAsync(string subject, string payloadJson, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private static IServiceCollection MinimalSignalARRR(IServiceCollection services) {
            services.AddLogging();
            services.AddSignalR();
            return services.AddSignalARRR(_ => { });
        }

        [Fact]
        public void A_backplane_registered_first_survives_AddSignalARRR() {
            var services = new ServiceCollection();

            // What AddSignalARRRRedisBackplane does, minus Redis.
            services.Replace(ServiceDescriptor.Singleton<ISignalARRRBackplane>(new MarkerBackplane()));
            MinimalSignalARRR(services);

            using var provider = services.BuildServiceProvider();
            Assert.IsType<MarkerBackplane>(provider.GetRequiredService<ISignalARRRBackplane>());
        }

        [Fact]
        public void A_backplane_registered_afterwards_replaces_the_default() {
            var services = new ServiceCollection();

            MinimalSignalARRR(services);
            services.Replace(ServiceDescriptor.Singleton<ISignalARRRBackplane>(new MarkerBackplane()));

            using var provider = services.BuildServiceProvider();
            Assert.IsType<MarkerBackplane>(provider.GetRequiredService<ISignalARRRBackplane>());
        }

        [Fact]
        public void Without_a_backplane_the_disabled_default_is_used() {
            var services = new ServiceCollection();
            MinimalSignalARRR(services);

            using var provider = services.BuildServiceProvider();
            Assert.False(provider.GetRequiredService<ISignalARRRBackplane>().IsEnabled);
        }
    }
}
