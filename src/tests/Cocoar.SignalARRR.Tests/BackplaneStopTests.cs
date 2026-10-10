using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// A host can stop a backplane twice at once (#85): the second <c>StopAsync</c> used to find the
/// heartbeat fields the first had just cleared and threw a NullReferenceException, which a test host
/// reports as a cleanup failure of the whole test class. And a host can stop it while the very first
/// heartbeat iteration is still running, which used to throw the cancellation out of <c>StopAsync</c>.
/// </summary>
public class BackplaneStopTests {

    /// <summary>A backplane without a store; its heartbeat write is slow enough to overlap two stops.</summary>
    private sealed class SlowHeartbeatBackplane : SignalARRRBackplaneBase {
        public int TransportStops;
        public int NodeCleanups;
        public bool MaintenanceWaitsForCancellation;
        public readonly TaskCompletionSource MaintenanceRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource HeartbeatRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SlowHeartbeatBackplane() : base(
            "node-a",
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromSeconds(30),
            null!,
            new ClusterSubjectRegistry(NullLogger<ClusterSubjectRegistry>.Instance),
            NullLogger.Instance) {
        }

        protected override Task StartTransportAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        protected override async Task StopTransportAsync(CancellationToken cancellationToken) {
            Interlocked.Increment(ref TransportStops);
            await Task.Delay(20, CancellationToken.None);
        }

        protected override async Task WriteHeartbeatAsync(CancellationToken cancellationToken) {
            HeartbeatRunning.TrySetResult();
            // Ignores cancellation on purpose: the heartbeat loop is still busy when the stops arrive.
            await Task.Delay(100, CancellationToken.None);
        }

        protected override async Task RunMaintenanceAsync(CancellationToken cancellationToken) {
            if (!MaintenanceWaitsForCancellation) return;
            MaintenanceRunning.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        protected override Task PublishCommandAsync(SignalARRRBackplaneEnvelope envelope) => Task.CompletedTask;
        protected override Task PublishResponseAsync(string targetNodeId, SignalARRRBackplaneEnvelope envelope) => Task.CompletedTask;
        protected override Task StoreRegistrationAsync(SignalARRRConnectionRegistration registration, CancellationToken cancellationToken) => Task.CompletedTask;
        protected override Task<SignalARRRConnectionRegistration?> LoadRegistrationAsync(string connectionId, CancellationToken cancellationToken) => Task.FromResult<SignalARRRConnectionRegistration?>(null);
        protected override Task<bool> IsNodeAliveAsync(string nodeId, CancellationToken cancellationToken) => Task.FromResult(true);
        protected override Task<IReadOnlyList<string>> GetKnownNodeIdsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        protected override Task CleanupNodeAsync(string nodeId, CancellationToken cancellationToken) {
            Interlocked.Increment(ref NodeCleanups);
            return Task.CompletedTask;
        }
        public override Task<TimeSpan?> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult<TimeSpan?>(TimeSpan.Zero);
        public override Task UnregisterConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task<IReadOnlyList<SignalARRRConnectionRegistration>> FindConnectionsAsync(
            Type hubType, string? groupName = null, string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SignalARRRConnectionRegistration>>(Array.Empty<SignalARRRConnectionRegistration>());
        public override Task AddConnectionToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task RemoveConnectionFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Two_overlapping_stops_share_one_shutdown() {
        var backplane = new SlowHeartbeatBackplane();
        await backplane.StartAsync(CancellationToken.None);
        await backplane.HeartbeatRunning.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var first = backplane.StopAsync(CancellationToken.None);
        var second = backplane.StopAsync(CancellationToken.None);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, backplane.TransportStops);
    }

    [Fact]
    public async Task A_stop_after_a_finished_stop_does_nothing() {
        var backplane = new SlowHeartbeatBackplane();
        await backplane.StartAsync(CancellationToken.None);
        await backplane.StopAsync(CancellationToken.None);
        await backplane.StopAsync(CancellationToken.None);

        Assert.Equal(1, backplane.TransportStops);
    }

    [Fact]
    public async Task A_backplane_can_start_again_after_a_stop() {
        var backplane = new SlowHeartbeatBackplane();
        await backplane.StartAsync(CancellationToken.None);
        await backplane.StopAsync(CancellationToken.None);
        await backplane.StartAsync(CancellationToken.None);
        await backplane.StopAsync(CancellationToken.None);

        Assert.Equal(2, backplane.TransportStops);
    }

    [Fact]
    public async Task A_stop_during_the_first_heartbeat_iteration_completes_the_shutdown() {
        var backplane = new SlowHeartbeatBackplane { MaintenanceWaitsForCancellation = true };
        await backplane.StartAsync(CancellationToken.None);
        await backplane.MaintenanceRunning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanupsAfterStart = backplane.NodeCleanups;

        await backplane.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(cleanupsAfterStart + 1, backplane.NodeCleanups);
        Assert.Equal(1, backplane.TransportStops);
        Assert.False(backplane.HeartbeatLoopFaulted);
    }

    [Fact]
    public void Registering_the_Postgres_backplane_twice_hosts_it_once() {
        var services = new ServiceCollection();
        services.AddSignalARRRPostgresBackplane(o => o.WithConnectionString("Host=localhost;Database=a"));
        services.AddSignalARRRPostgresBackplane(o => o.WithConnectionString("Host=localhost;Database=b"));

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void Registering_the_Redis_backplane_twice_hosts_it_once() {
        var services = new ServiceCollection();
        services.AddSignalARRRRedisBackplane(o => o.WithConnectionString("localhost:6379"));
        services.AddSignalARRRRedisBackplane(o => o.WithConnectionString("localhost:6379"));

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }
}
