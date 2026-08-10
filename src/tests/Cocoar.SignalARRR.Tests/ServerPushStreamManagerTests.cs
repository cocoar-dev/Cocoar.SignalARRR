using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
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

    private const string Owner = "connection-a";
    private const string SomeoneElse = "connection-b";

    /// <summary>Unlimited by default: the cap has its own tests and would only obscure the others.</summary>
    private static string CreateSlot(ServerPushStreamManager manager, string owner = Owner, int maxSlots = 0) =>
        manager.CreateUploadSlot(BaseUrl, owner, maxSlots);

    private static Stream Payload(string content = "payload") =>
        new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task An_upload_that_never_arrives_times_out() {
        using var manager = new ServerPushStreamManager();
        var slot = CreateSlot(manager);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.WaitForUpload(slot, Owner, TimeSpan.FromMilliseconds(150)));
    }

    [Fact]
    public async Task A_timed_out_slot_is_released() {
        using var manager = new ServerPushStreamManager();
        var slot = CreateSlot(manager);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            manager.WaitForUpload(slot, Owner, TimeSpan.FromMilliseconds(150)));

        // The slot used to be removed only on the success path, so an abandoned wait left it behind
        // for the process lifetime.
        Assert.False(manager.UploadSlotExists(slot));
    }

    [Fact]
    public async Task A_cancelled_wait_is_released() {
        using var manager = new ServerPushStreamManager();
        var slot = CreateSlot(manager);
        using var cts = new CancellationTokenSource();

        var waiting = manager.WaitForUpload(slot, Owner, TimeSpan.FromMinutes(1), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.False(manager.UploadSlotExists(slot));
    }

    [Fact]
    public async Task An_upload_that_arrives_first_is_still_delivered() {
        using var manager = new ServerPushStreamManager();
        var slot = CreateSlot(manager);

        // The client uploads, returns the StreamReference, and only then does the server wait.
        Assert.True(manager.CompleteUpload(slot, Payload("hello")));

        var stream = await manager.WaitForUpload(slot, Owner, TimeSpan.FromSeconds(5));

        Assert.Equal("hello", new StreamReader(stream).ReadToEnd());
    }

    [Fact]
    public async Task An_upload_that_arrives_while_waiting_is_delivered() {
        using var manager = new ServerPushStreamManager();
        var slot = CreateSlot(manager);

        var waiting = manager.WaitForUpload(slot, Owner, TimeSpan.FromSeconds(5));
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
        var slot = CreateSlot(manager);

        Assert.True(manager.CompleteUpload(slot, Payload("first")));
        var second = Payload("second");

        Assert.False(manager.CompleteUpload(slot, second));
        Assert.Throws<ObjectDisposedException>(() => second.ReadByte());

        Assert.Equal("first", new StreamReader(await manager.WaitForUpload(slot, Owner, TimeSpan.FromSeconds(5))).ReadToEnd());
    }

    [Fact]
    public void An_unknown_slot_is_reported_as_missing() {
        using var manager = new ServerPushStreamManager();

        Assert.False(manager.UploadSlotExists("https://localhost:5001/hub/upload/nope"));
    }

    [Fact]
    public void A_created_slot_is_reported_as_existing_regardless_of_casing() {
        using var manager = new ServerPushStreamManager();
        var slot = CreateSlot(manager);

        Assert.True(manager.UploadSlotExists(slot));
        Assert.True(manager.UploadSlotExists(slot.ToUpperInvariant()));
    }

    [Fact]
    public async Task Waiting_on_an_unknown_slot_fails_immediately() {
        using var manager = new ServerPushStreamManager();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.WaitForUpload("https://localhost:5001/hub/upload/nope", Owner, TimeSpan.FromSeconds(5)));
    }

    // ---- Ownership ---------------------------------------------------------------------------
    //
    // A slot used to be a pure bearer capability: the URL was the whole credential. The POST still
    // is one — an HTTP request carries no connection identity — but consuming the slot is not, and
    // that is the side where the caller is known.

    [Fact]
    public async Task A_slot_cannot_be_consumed_by_another_connection() {
        using var manager = new ServerPushStreamManager();
        var slot = CreateSlot(manager, Owner);
        Assert.True(manager.CompleteUpload(slot, Payload("secret")));

        // Naming someone else's slot as a Stream argument used to hand over their bytes under the
        // caller's own principal.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.WaitForUpload(slot, SomeoneElse, TimeSpan.FromSeconds(5)));

        // …and the rejection must not consume it either: the rightful owner still gets its stream.
        Assert.Equal("secret", new StreamReader(await manager.WaitForUpload(slot, Owner, TimeSpan.FromSeconds(5))).ReadToEnd());
    }

    [Fact]
    public async Task A_foreign_slot_is_indistinguishable_from_one_that_does_not_exist() {
        using var manager = new ServerPushStreamManager();
        var slot = CreateSlot(manager, Owner);

        var foreign = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.WaitForUpload(slot, SomeoneElse, TimeSpan.FromSeconds(5)));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.WaitForUpload("https://localhost:5001/hub/upload/nope", SomeoneElse, TimeSpan.FromSeconds(5)));

        // Same shape of answer, so the check cannot be used to probe which URLs are live.
        Assert.StartsWith("Upload slot not found", foreign.Message);
        Assert.StartsWith("Upload slot not found", missing.Message);
    }

    // ---- Per-connection cap ------------------------------------------------------------------

    [Fact]
    public void A_connection_cannot_hold_more_slots_than_its_cap() {
        using var manager = new ServerPushStreamManager();

        CreateSlot(manager, Owner, maxSlots: 2);
        CreateSlot(manager, Owner, maxSlots: 2);

        // Requesting a slot is an ordinary hub call; without a cap, a loop pins memory for the whole
        // expiration window and the sweep never catches up while the loop runs.
        var rejected = Assert.Throws<HARRRException>(() => CreateSlot(manager, Owner, maxSlots: 2));
        Assert.Equal(HARRRErrorCodes.UploadSlotLimitReached, rejected.Error.Code);
    }

    [Fact]
    public void The_cap_is_per_connection_not_global() {
        using var manager = new ServerPushStreamManager();

        CreateSlot(manager, Owner, maxSlots: 1);

        // One busy client must not lock everyone else out.
        var other = CreateSlot(manager, SomeoneElse, maxSlots: 1);
        Assert.True(manager.UploadSlotExists(other));
    }

    [Fact]
    public async Task Consuming_a_slot_gives_the_quota_back() {
        using var manager = new ServerPushStreamManager();

        var slot = CreateSlot(manager, Owner, maxSlots: 1);
        Assert.True(manager.CompleteUpload(slot, Payload()));
        await manager.WaitForUpload(slot, Owner, TimeSpan.FromSeconds(5));

        // Otherwise the cap would be a lifetime budget rather than a concurrency limit.
        var next = CreateSlot(manager, Owner, maxSlots: 1);
        Assert.True(manager.UploadSlotExists(next));
    }

    [Fact]
    public void Slots_are_cancelled_and_returned_when_their_connection_goes_away() {
        using var manager = new ServerPushStreamManager();

        var mine = CreateSlot(manager, Owner, maxSlots: 1);
        var theirs = CreateSlot(manager, SomeoneElse, maxSlots: 1);

        manager.CancelUploadSlotsFor(Owner);

        Assert.False(manager.UploadSlotExists(mine));
        // Only that connection's slots — this used to be nobody's job at all, so a client could
        // disconnect and reconnect to keep allocating past its cap.
        Assert.True(manager.UploadSlotExists(theirs));

        // The quota came back with it.
        Assert.True(manager.UploadSlotExists(CreateSlot(manager, Owner, maxSlots: 1)));
    }
}
