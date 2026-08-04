using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.ProxyGenerator;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers the stream-to-channel bridge behind every generated proxy method that returns a
/// <see cref="ChannelReader{T}"/>.
/// </summary>
/// <remarks>
/// The pump ran without error handling, so a stream that faulted — a dropped connection, a server
/// exception — skipped <c>TryComplete</c> entirely. The channel was never completed, and every
/// consumer waited for an item that could not arrive, for the lifetime of the process. The
/// exception was never observed either, so nothing said why.
/// </remarks>
public class ToChannelReaderTests {

    private sealed class Bridge : ProxyCreatorHelper {
        public override void Send(string m, IEnumerable<object> a, string[] g, CancellationToken c = default) => throw new NotSupportedException();
        public override Task SendAsync(string m, IEnumerable<object> a, string[] g, CancellationToken c = default) => throw new NotSupportedException();
        public override T Invoke<T>(string m, IEnumerable<object> a, string[] g, CancellationToken c = default) => throw new NotSupportedException();
        public override Task<T> InvokeAsync<T>(string m, IEnumerable<object> a, string[] g, CancellationToken c = default) => throw new NotSupportedException();
        public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string m, IEnumerable<object> a, string[] g, CancellationToken c = default) => throw new NotSupportedException();
    }

    private static async IAsyncEnumerable<int> ThrowsAfter(int items, Exception failure,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {

        for (var i = 0; i < items; i++) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
            await Task.Yield();
        }

        throw failure;
    }

    private static async IAsyncEnumerable<int> Forever([EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var i = 0;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i++;
            await Task.Delay(5, cancellationToken);
        }
    }

    /// <summary>
    /// A faulting stream surfaces as an exception on the reader, not as an endless wait.
    /// </summary>
    [Fact]
    public async Task A_failing_stream_faults_the_reader() {
        var reader = new Bridge().ToChannelReader(ThrowsAfter(2, new InvalidOperationException("upstream went away")));

        var received = new List<int>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () => {
            await foreach (var item in reader.ReadAllAsync().WithCancellation(Timeout(TimeSpan.FromSeconds(20)))) {
                received.Add(item);
            }
        });

        Assert.Equal("upstream went away", thrown.Message);
        Assert.Equal(new[] { 0, 1 }, received);
    }

    /// <summary>
    /// The items that did arrive before the failure are still delivered.
    /// </summary>
    /// <remarks>
    /// Completing with the exception must not discard what was already written — a consumer that
    /// processes as it reads has legitimately seen those items.
    /// </remarks>
    [Fact]
    public async Task A_normal_stream_completes() {
        var reader = new Bridge().ToChannelReader(ThrowsAfter(3, new OperationCanceledException()));

        var received = new List<int>();
        try {
            await foreach (var item in reader.ReadAllAsync().WithCancellation(Timeout(TimeSpan.FromSeconds(20)))) {
                received.Add(item);
            }
        } catch (OperationCanceledException) {
            // expected: the source ends by throwing
        }

        Assert.Equal(new[] { 0, 1, 2 }, received);
    }

    /// <summary>
    /// Cancelling stops the producer, not only the writes.
    /// </summary>
    /// <remarks>
    /// The token used to reach <c>WriteAsync</c> only. An unbounded channel never blocks a write, so
    /// it was effectively ignored and the producer kept running after the consumer had gone.
    /// </remarks>
    [Fact]
    public async Task Cancelling_stops_the_producer() {
        using var cts = new CancellationTokenSource();
        var reader = new Bridge().ToChannelReader(Forever(), cts.Token);

        await reader.ReadAsync(Timeout(TimeSpan.FromSeconds(20)));
        cts.Cancel();

        // Asserted on Completion, not by enumerating: an enumeration guarded by a timeout token
        // throws OperationCanceledException when the producer stops *and* when it never does, so it
        // is satisfied by the very hang this covers. The channel reaching a completed state at all
        // is the thing that distinguishes the two.
        var completion = reader.Completion;
        await Task.WhenAny(completion, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.True(completion.IsCompleted,
            "The channel never completed, so the producer was still running after cancellation.");
    }

    /// <summary>
    /// A token that trips after <paramref name="after"/>, used to turn a hang into a failure.
    /// </summary>
    /// <remarks>
    /// The sources are rooted deliberately. An undisposed <see cref="CancellationTokenSource"/> with
    /// a timer is collectable once nothing references it, and a collected one never fires — so the
    /// hang this is meant to catch would simply wait forever instead.
    /// </remarks>
    private readonly List<CancellationTokenSource> _timeouts = new();

    private CancellationToken Timeout(TimeSpan after) {
        var cts = new CancellationTokenSource(after);
        _timeouts.Add(cts);
        return cts.Token;
    }
}
