using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers operator visibility (O-8): stream bookkeeping that used to be private, the idle-stream
/// reaper, and the health check's verdicts for the states an operator needs to tell apart.
/// </summary>
public class OperatorVisibilityTests {

    // ---- Stream counting and reaping -------------------------------------------------------

    [Fact]
    public async Task A_consumed_stream_leaves_no_bookkeeping_behind() {
        using var manager = new ServerStreamManager(TimeSpan.FromMinutes(10));
        var streamId = Guid.NewGuid();
        var channel = manager.CreateStream(streamId, "conn-1");
        Assert.Equal(1, manager.ActiveStreamCount);

        await channel.Writer.WriteAsync("item");
        channel.Writer.TryComplete();
        await foreach (var _ in manager.ReadStream<string>(streamId)) {
        }

        Assert.Equal(0, manager.ActiveStreamCount);
    }

    [Fact]
    public void A_disconnect_clears_the_connections_streams() {
        using var manager = new ServerStreamManager(TimeSpan.FromMinutes(10));
        manager.CreateStream(Guid.NewGuid(), "conn-1");
        manager.CreateStream(Guid.NewGuid(), "conn-1");
        manager.CreateStream(Guid.NewGuid(), "conn-2");

        manager.CompleteStreamsFor("conn-1", "gone");

        Assert.Equal(1, manager.ActiveStreamCount);
    }

    [Fact]
    public async Task A_stream_nobody_ever_consumed_is_reaped() {
        // Zero idle timeout: everything unread is immediately over age.
        using var manager = new ServerStreamManager(TimeSpan.Zero);
        var streamId = Guid.NewGuid();
        var channel = manager.CreateStream(streamId, "conn-1");

        var reaped = manager.SweepIdleStreams();

        Assert.Equal(1, reaped);
        Assert.Equal(0, manager.ActiveStreamCount);
        // The producer side learns about it instead of writing into the void forever.
        await Assert.ThrowsAsync<IOException>(async () => {
            while (await channel.Writer.WaitToWriteAsync()) {
            }
        });
    }

    [Fact]
    public async Task A_stream_that_is_being_read_is_never_reaped() {
        using var manager = new ServerStreamManager(TimeSpan.Zero);
        var streamId = Guid.NewGuid();
        var channel = manager.CreateStream(streamId, "conn-1");
        await channel.Writer.WriteAsync("first");

        // Attach a reader and consume the first item — the stream is alive, however old it is.
        var enumerator = manager.ReadStream<string>(streamId).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());

        Assert.Equal(0, manager.SweepIdleStreams());
        Assert.Equal(1, manager.ActiveStreamCount);

        channel.Writer.TryComplete();
        Assert.False(await enumerator.MoveNextAsync());
        await enumerator.DisposeAsync();
        Assert.Equal(0, manager.ActiveStreamCount);
    }

    // ---- Health check verdicts -------------------------------------------------------------

    private static SignalARRRHealthCheck HealthCheckWith(ISignalARRRBackplane backplane) =>
        new(backplane, new ServerStreamManager(TimeSpan.FromMinutes(10)), new ServerPushStreamManager());

    [Fact]
    public async Task Without_a_backplane_the_check_is_healthy() {
        var result = await HealthCheckWith(new DisabledSignalARRRBackplane())
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("disabled", result.Data["backplane"]);
        Assert.Equal(0, result.Data["activeStreams"]);
    }

    [Fact]
    public async Task A_faulted_heartbeat_loop_is_unhealthy() {
        var backplane = new FakeBackplane { HeartbeatLoopFaulted = true };

        var result = await HealthCheckWith(backplane).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task An_unreachable_store_is_unhealthy() {
        var backplane = new FakeBackplane { Ping = null };

        var result = await HealthCheckWith(backplane).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task A_stale_heartbeat_is_degraded_not_dead() {
        var backplane = new FakeBackplane {
            Ping = TimeSpan.FromMilliseconds(1),
            LastSuccessfulHeartbeatUtc = DateTime.UtcNow - TimeSpan.FromMinutes(5),
        };

        var result = await HealthCheckWith(backplane).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public async Task A_live_backplane_is_healthy_and_reports_the_nodes() {
        var backplane = new FakeBackplane {
            Ping = TimeSpan.FromMilliseconds(1),
            LastSuccessfulHeartbeatUtc = DateTime.UtcNow,
            Nodes = new[] { "node-a", "node-b" },
        };

        var result = await HealthCheckWith(backplane).CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(2, result.Data["activeNodeCount"]);
        Assert.Equal("node-a,node-b", result.Data["activeNodes"]);
    }

    private sealed class FakeBackplane : ISignalARRRBackplane, ISignalARRRBackplaneHealth {
        public bool IsEnabled => true;
        public string NodeId => "node-a";
        public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
        public DateTime? LastSuccessfulHeartbeatUtc { get; init; }
        public bool HeartbeatLoopFaulted { get; init; }
        public TimeSpan? Ping { get; init; }
        public IReadOnlyList<string> Nodes { get; init; } = new[] { "node-a" };

        public Task<TimeSpan?> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(Ping);

        public Task<IReadOnlyList<string>> GetActiveNodesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Nodes);

        public Task PublishDispatchAsync(Type? hubType, SignalARRRBackplaneTargetKind targetKind, Cocoar.SignalARRR.Common.ServerRequestMessage message, IReadOnlyList<string>? connectionIds = null, string? groupName = null, string? userId = null, string? signalRMethodName = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<object?> InvokeConnectionAsync(Type? hubType, string connectionId, Cocoar.SignalARRR.Common.ServerRequestMessage message, Type resultType, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);

        public Task<IReadOnlyList<SignalARRRBackplaneInvokeResult>> InvokeQueryAsync(Type hubType, SignalARRRBackplaneTargetKind targetKind, Cocoar.SignalARRR.Common.ServerRequestMessage message, Type resultType, string? groupName = null, string? userId = null, bool singleResult = false, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SignalARRRBackplaneInvokeResult>>(Array.Empty<SignalARRRBackplaneInvokeResult>());

        public Task PublishGroupCommandAsync(Type? hubType, string connectionId, string groupName, SignalARRRBackplaneGroupAction action, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
