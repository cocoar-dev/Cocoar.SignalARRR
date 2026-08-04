using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers the cancellation token SignalR hands to a streaming server method.
/// </summary>
/// <remarks>
/// SignalR cancels that token when the client aborts the stream or the connection drops. It used to
/// be dropped on the floor: the enumerator was obtained without it, and a
/// <see cref="ChannelReader{T}"/> source was converted to an enumerable in the constructor, before
/// any token existed. The server loop then stayed parked in <c>MoveNextAsync</c> and the producer
/// behind it kept going, with nobody left to receive anything.
/// </remarks>
public class StreamingResultTests {

    /// <summary>How long a still-running stream is given before it counts as not cancellable.</summary>
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A stand-in for the per-item authorization check, which is not what these tests are about.
    /// </summary>
    /// <remarks>
    /// <see cref="ClientContext"/> needs a live hub and a <c>HubCallerContext</c>, neither of which
    /// exists here. <c>TryAuthenticate</c> returns success immediately for a method carrying no
    /// authorization data, without touching instance state, so an uninitialized instance is enough
    /// to reach the enumeration. If that short-circuit ever moves below a field access, these tests
    /// will fail with a <see cref="NullReferenceException"/> — which is the signal to give them a
    /// real context rather than to widen this hack.
    /// </remarks>
    private static ClientContext UnauthenticatedContext() =>
        (ClientContext)RuntimeHelpers.GetUninitializedObject(typeof(ClientContext));

    /// <summary>A method with no <c>[Authorize]</c> data, so the check short-circuits.</summary>
    private static MethodInfo UnrestrictedMethod() =>
        typeof(StreamingResultTests).GetMethod(nameof(Unrestricted), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Unrestricted() { }

    private static async IAsyncEnumerable<int> Forever([EnumeratorCancellation] CancellationToken cancellationToken = default) {
        var i = 0;
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i++;
            await Task.Delay(5, cancellationToken);
        }
    }

    private static async Task<bool> StopsWhenCancelled(StreamingResult streamingResult) {
        using var cts = new CancellationTokenSource();

        var pump = Task.Run(async () => {
            var enumerator = streamingResult.GetAsyncEnumerator(cts.Token);
            try {
                var seen = 0;
                while (await enumerator.MoveNextAsync()) {
                    if (++seen == 3) {
                        cts.Cancel();
                    }
                }
            } finally {
                await enumerator.DisposeAsync();
            }
        });

        // Whether it throws OperationCanceledException or ends quietly does not matter; what matters
        // is that it ends at all. Awaiting it directly would hang for the whole test run when the
        // token is ignored, which is precisely the defect.
        var finished = await Task.WhenAny(pump, Task.Delay(Grace));
        return ReferenceEquals(finished, pump);
    }

    /// <summary>
    /// An <c>[EnumeratorCancellation]</c> iterator must stop when SignalR cancels the stream.
    /// </summary>
    [Fact]
    public async Task An_enumerable_source_observes_the_stream_token() {
        var streamingResult = new StreamingResult<int>(Forever(), UnauthenticatedContext(), UnrestrictedMethod());

        Assert.True(await StopsWhenCancelled(streamingResult),
            "The stream kept running after cancellation, so the token never reached the source.");
    }

    /// <summary>
    /// And so must a channel-backed one.
    /// </summary>
    /// <remarks>
    /// This path was worse: <c>ReadAllAsync()</c> was called in the constructor, so the token could
    /// not have been passed even in principle — there was none yet.
    /// </remarks>
    [Fact]
    public async Task A_channel_source_observes_the_stream_token() {
        var channel = Channel.CreateUnbounded<int>();
        _ = Task.Run(async () => {
            var i = 0;
            while (await channel.Writer.WaitToWriteAsync()) {
                await channel.Writer.WriteAsync(i++);
                await Task.Delay(5);
            }
        });

        var streamingResult = new StreamingResult<int>(channel.Reader, UnauthenticatedContext(), UnrestrictedMethod());

        Assert.True(await StopsWhenCancelled(streamingResult),
            "The stream kept running after cancellation, so the token never reached the channel reader.");
    }
}
