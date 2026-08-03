using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers ownership and buffering of client-to-server streams.
/// </summary>
/// <remarks>
/// <c>StreamItemToServer</c> and <c>StreamCompleteToServer</c> are public hub methods that used to
/// look the stream up in a global dictionary by id alone. The id was therefore the only credential:
/// any connected client that learned another's — from a log, a shared proxy, or a replay — could
/// inject forged items into that stream or abort it with an error.
/// </remarks>
public class ServerStreamManagerTests {

    private const string Owner = "connection-owner";
    private const string Stranger = "connection-stranger";

    private static async Task<List<T>> ReadAllAsync<T>(IAsyncEnumerable<T> source) {
        var items = new List<T>();
        await foreach (var item in source) {
            items.Add(item);
        }
        return items;
    }

    [Fact]
    public async Task The_owner_can_write_and_complete() {
        var manager = new ServerStreamManager();
        var streamId = Guid.NewGuid();
        manager.CreateStream(streamId, Owner);

        Assert.True(await manager.WriteItemAsync(streamId, "a", Owner));
        Assert.True(await manager.WriteItemAsync(streamId, "b", Owner));
        Assert.True(manager.CompleteStream(streamId, Owner));

        Assert.Equal(new[] { "a", "b" }, await ReadAllAsync(manager.ReadStream<string>(streamId)));
    }

    [Fact]
    public async Task Another_connection_cannot_inject_items() {
        var manager = new ServerStreamManager();
        var streamId = Guid.NewGuid();
        manager.CreateStream(streamId, Owner);

        Assert.False(await manager.WriteItemAsync(streamId, "forged", Stranger));

        Assert.True(await manager.WriteItemAsync(streamId, "genuine", Owner));
        Assert.True(manager.CompleteStream(streamId, Owner));

        Assert.Equal(new[] { "genuine" }, await ReadAllAsync(manager.ReadStream<string>(streamId)));
    }

    [Fact]
    public async Task Another_connection_cannot_complete_the_stream() {
        var manager = new ServerStreamManager();
        var streamId = Guid.NewGuid();
        manager.CreateStream(streamId, Owner);

        Assert.False(manager.CompleteStream(streamId, Stranger));

        // Still usable by its owner — the stranger's attempt changed nothing.
        Assert.True(await manager.WriteItemAsync(streamId, "still here", Owner));
        Assert.True(manager.CompleteStream(streamId, Owner));

        Assert.Equal(new[] { "still here" }, await ReadAllAsync(manager.ReadStream<string>(streamId)));
    }

    [Fact]
    public async Task Another_connection_cannot_fault_the_stream() {
        var manager = new ServerStreamManager();
        var streamId = Guid.NewGuid();
        manager.CreateStream(streamId, Owner);

        Assert.False(manager.CompleteStream(streamId, Stranger, "boom"));

        Assert.True(manager.CompleteStream(streamId, Owner));
        Assert.Empty(await ReadAllAsync(manager.ReadStream<string>(streamId)));
    }

    [Fact]
    public async Task An_unknown_stream_id_is_rejected() {
        var manager = new ServerStreamManager();

        Assert.False(await manager.WriteItemAsync(Guid.NewGuid(), "x", Owner));
        Assert.False(manager.CompleteStream(Guid.NewGuid(), Owner));
    }

    [Fact]
    public async Task An_error_from_the_owner_faults_the_stream() {
        var manager = new ServerStreamManager();
        var streamId = Guid.NewGuid();
        manager.CreateStream(streamId, Owner);

        Assert.True(manager.CompleteStream(streamId, Owner, "client blew up"));

        var ex = await Assert.ThrowsAsync<Exception>(() => ReadAllAsync(manager.ReadStream<string>(streamId)));
        Assert.Contains("client blew up", ex.Message);
    }

    [Fact]
    public async Task The_buffer_is_bounded_so_a_fast_producer_has_to_wait() {
        var manager = new ServerStreamManager();
        var streamId = Guid.NewGuid();
        manager.CreateStream(streamId, Owner, bufferSize: 2);

        Assert.True(await manager.WriteItemAsync(streamId, 1, Owner));
        Assert.True(await manager.WriteItemAsync(streamId, 2, Owner));

        // The third write cannot complete until something is read. Previously the channel was
        // unbounded and written with TryWrite, which never fails — a client could push faster than
        // the server consumed and grow the heap without limit.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.WriteItemAsync(streamId, 3, Owner, cts.Token));
    }

    [Fact]
    public async Task Disconnecting_releases_a_server_task_waiting_on_that_connections_stream() {
        var manager = new ServerStreamManager();
        var ownerStream = Guid.NewGuid();
        var otherStream = Guid.NewGuid();
        manager.CreateStream(ownerStream, Owner);
        manager.CreateStream(otherStream, Stranger);

        // This is the real shape: the server is already awaiting the stream when the client goes
        // away. Without the disconnect hook the channel is never completed, so that task stays
        // parked for the process lifetime holding everything buffered in it.
        var reading = ReadAllAsync(manager.ReadStream<string>(ownerStream));

        manager.CompleteStreamsFor(Owner, "The client disconnected while streaming.");

        await Assert.ThrowsAsync<System.IO.IOException>(() => reading);
    }

    [Fact]
    public async Task Disconnecting_leaves_other_connections_streams_alone() {
        var manager = new ServerStreamManager();
        var ownerStream = Guid.NewGuid();
        var otherStream = Guid.NewGuid();
        manager.CreateStream(ownerStream, Owner);
        manager.CreateStream(otherStream, Stranger);

        manager.CompleteStreamsFor(Owner, "The client disconnected while streaming.");

        Assert.True(await manager.WriteItemAsync(otherStream, "untouched", Stranger));
        Assert.True(manager.CompleteStream(otherStream, Stranger));
        Assert.Equal(new[] { "untouched" }, await ReadAllAsync(manager.ReadStream<string>(otherStream)));
    }
}
