using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers the client-to-server upload slots.
/// </summary>
/// <remarks>
/// A server method with a <see cref="Stream"/> parameter waits here for the client to upload it.
/// The wait had no timeout, and on the non-streaming invoke path not even a cancellation token —
/// so a client could request a slot, invoke the method and never upload, parking a thread pool
/// thread for the lifetime of the process. Repeated, that is a remote denial of service.
/// </remarks>
public class ServerPushStreamManagerTests {

    private static readonly Uri BaseUrl = new("https://localhost:5001/hub");

    private static Stream Payload(string content = "payload") =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task An_upload_that_never_arrives_times_out() {
        using var manager = new ServerPushStreamManager();
        var slot = manager.CreateUploadSlot(BaseUrl);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.WaitForUpload(slot, TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public async Task A_timed_out_slot_is_released() {
        using var manager = new ServerPushStreamManager();
        var slot = manager.CreateUploadSlot(BaseUrl);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.WaitForUpload(slot, TimeSpan.FromMilliseconds(150)));

        // The slot used to be removed only on the success path, so an abandoned wait left it behind
        // for the process lifetime.
        Assert.False(manager.UploadSlotExists(slot));
    }

    [Fact]
    public async Task A_cancelled_wait_is_released() {
        using var manager = new ServerPushStreamManager();
        var slot = manager.CreateUploadSlot(BaseUrl);
        using var cts = new CancellationTokenSource();

        var waiting = manager.WaitForUpload(slot, TimeSpan.FromMinutes(1), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(manager.UploadSlotExists(slot));
    }

    [Fact]
    public async Task An_upload_that_arrives_first_is_still_delivered() {
        using var manager = new ServerPushStreamManager();
        var slot = manager.CreateUploadSlot(BaseUrl);

        // The client uploads, returns the StreamReference, and only then does the server wait.
        Assert.True(manager.CompleteUpload(slot, Payload("hello")));

        var stream = await manager.WaitForUpload(slot, TimeSpan.FromSeconds(5));

        Assert.Equal("hello", new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public async Task An_upload_that_arrives_while_waiting_is_delivered() {
        using var manager = new ServerPushStreamManager();
        var slot = manager.CreateUploadSlot(BaseUrl);

        var waiting = manager.WaitForUpload(slot, TimeSpan.FromSeconds(5));
        Assert.True(manager.CompleteUpload(slot, Payload("world")));

        Assert.Equal("world", new StreamReader(await waiting).ReadToEnd());
    }

    [Fact]
    public void An_upload_to_an_unknown_slot_is_rejected_and_does_not_leak_the_stream() {
        using var manager = new ServerPushStreamManager();
        var stream = Payload();

        Assert.False(manager.CompleteUpload("https://localhost:5001/hub/upload/does-not-exist", stream));

        // Nobody will ever consume it, so the manager has to dispose it rather than hold the memory.
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact]
    public async Task A_second_upload_to_the_same_slot_is_rejected() {
        using var manager = new ServerPushStreamManager();
        var slot = manager.CreateUploadSlot(BaseUrl);

        Assert.True(manager.CompleteUpload(slot, Payload("first")));
        var second = Payload("second");

        Assert.False(manager.CompleteUpload(slot, second));
        Assert.Throws<ObjectDisposedException>(() => second.ReadByte());

        Assert.Equal("first", new StreamReader(await manager.WaitForUpload(slot, TimeSpan.FromSeconds(5))).ReadToEnd());
    }

    [Fact]
    public void An_unknown_slot_is_reported_as_missing() {
        using var manager = new ServerPushStreamManager();

        Assert.False(manager.UploadSlotExists("https://localhost:5001/hub/upload/nope"));
    }

    [Fact]
    public void A_created_slot_is_reported_as_existing_regardless_of_casing() {
        using var manager = new ServerPushStreamManager();
        var slot = manager.CreateUploadSlot(BaseUrl);

        Assert.True(manager.UploadSlotExists(slot));
        Assert.True(manager.UploadSlotExists(slot.ToUpperInvariant()));
    }

    [Fact]
    public async Task Waiting_on_an_unknown_slot_fails_immediately() {
        using var manager = new ServerPushStreamManager();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.WaitForUpload("https://localhost:5001/hub/upload/nope", TimeSpan.FromSeconds(5)));
    }
}
