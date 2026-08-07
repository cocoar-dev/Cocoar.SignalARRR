using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers P-3: the client send path must not block the calling thread while the access token
/// provider resolves. On a single-threaded SynchronizationContext (WPF/WinForms/MAUI) the
/// provider's continuation is posted back to the very thread a blocking wait would occupy —
/// a hard deadlock before anything touches the wire.
/// </summary>
public class TokenResolutionDeadlockTests {

    [Fact]
    public void The_send_path_does_not_block_on_the_token_provider() {
        using var pump = new SingleThreadSynchronizationContext();
        Exception? invocationOutcome = null;

        var uiThread = new Thread(() => {
            SynchronizationContext.SetSynchronizationContext(pump);

            var connection = HARRRConnection.Create(builder => {
                builder.WithUrl("http://localhost:1/signalr/never-connected", options => {
                    options.AccessTokenProvider = async () => {
                        // Posts the continuation to the pump — the thread a blocking wait
                        // would occupy. This is what every await inside a real token
                        // provider does under a UI SynchronizationContext.
                        await Task.Yield();
                        return "token";
                    };
                });
            });

            // Never started on purpose: getting PAST token resolution to the fast
            // "connection is not active" failure is the assertion. A deadlock never
            // reaches it and the pump runs forever.
            var invocation = connection.InvokeCoreAsync<string>("Deadlock.Probe", Array.Empty<object>());
            invocation.ContinueWith(t => {
                invocationOutcome = t.Exception;
                pump.Complete();
            }, TaskScheduler.Default);

            pump.RunOnCurrentThread();
        });
        uiThread.IsBackground = true;
        uiThread.Start();

        Assert.True(uiThread.Join(TimeSpan.FromSeconds(30)),
            "The send path deadlocked: the token provider's continuation never ran because the calling thread was blocked waiting for it.");
        Assert.NotNull(invocationOutcome);
    }

    /// <summary>
    /// The WPF/WinForms dispatcher shape reduced to its essence: continuations are posted to a
    /// queue that only the owning thread drains.
    /// </summary>
    private sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public void RunOnCurrentThread() {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) {
                callback(state);
            }
        }

        public void Complete() => _queue.CompleteAdding();

        public void Dispose() => _queue.Dispose();
    }
}
