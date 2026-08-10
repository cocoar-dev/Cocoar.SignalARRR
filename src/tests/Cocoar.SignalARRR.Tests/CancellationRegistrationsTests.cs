using System.Threading;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    /// <summary>
    /// The seam the DI-6 fix rests on: a registration that is disposed must no longer fire.
    /// </summary>
    /// <remarks>
    /// Worth pinning down because the failure it guards against is invisible. Dropping a
    /// <see cref="CancellationTokenRegistration"/> leaves the callback attached to the token, and on a
    /// long-lived token — the kind built for "cancel everything for this connection" — every
    /// server-to-client call added one more, each holding a <c>ClientContext</c> and its DI scope
    /// alive. Nothing fails; the process just grows.
    /// </remarks>
    public class CancellationRegistrationsTests {

        [Fact]
        public void Disposing_unhooks_the_callback() {
            using var cts = new CancellationTokenSource();
            var fired = false;

            var registrations = new CancellationRegistrations();
            registrations.Add(cts.Token.Register(() => fired = true));

            registrations.Dispose();
            cts.Cancel();

            Assert.False(fired);
        }

        [Fact]
        public void A_callback_still_fires_while_the_call_is_running() {
            // The other half of the contract: unhooking early would break cancellation itself.
            using var cts = new CancellationTokenSource();
            var fired = false;

            var registrations = new CancellationRegistrations();
            registrations.Add(cts.Token.Register(() => fired = true));

            cts.Cancel();

            Assert.True(fired);
            registrations.Dispose();
        }

        [Fact]
        public void Every_registration_is_unhooked_not_just_the_last() {
            using var cts = new CancellationTokenSource();
            var count = 0;

            var registrations = new CancellationRegistrations();
            for (var i = 0; i < 5; i++) {
                registrations.Add(cts.Token.Register(() => Interlocked.Increment(ref count)));
            }

            registrations.Dispose();
            cts.Cancel();

            Assert.Equal(0, count);
        }

        [Fact]
        public void A_default_registration_is_ignored() {
            // RegisterCallCancellation returns default when the call needs no id of its own, which is
            // the common case — the token already travels as an argument.
            var registrations = new CancellationRegistrations();
            registrations.Add(default);

            registrations.Dispose();
        }

        [Fact]
        public void Disposing_twice_is_safe() {
            using var cts = new CancellationTokenSource();

            var registrations = new CancellationRegistrations();
            registrations.Add(cts.Token.Register(() => { }));

            registrations.Dispose();
            registrations.Dispose();
        }
    }
}
