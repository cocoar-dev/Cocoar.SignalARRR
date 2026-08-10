using System;
using System.Collections.Generic;
using System.Threading;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// The cancellation callbacks one outgoing call registered, disposed together when the call is
    /// over.
    /// </summary>
    /// <remarks>
    /// <see cref="CancellationToken.Register(Action)"/> returns a registration that unhooks the
    /// callback; dropping it leaves the callback attached for as long as the <em>token</em> lives.
    /// For a request-scoped token that is harmless, but server code that wants "cancel everything
    /// for this connection" builds exactly the opposite: one long-lived token. Every server-to-client
    /// call then hung another callback on it, each closing over the <see cref="ClientContext"/> — so
    /// the list grew without bound and the context, along with its DI scope, could never be
    /// collected (DI-6).
    /// <para>
    /// Not every registration belongs to a call. A broadcast's cancellation callback deliberately
    /// outlives the send, because a fire-and-forget broadcast stays cancellable after it returns and
    /// there is no later moment that could dispose it — see <see cref="BroadcastArgumentRules"/>.
    /// </para>
    /// </remarks>
    internal sealed class CancellationRegistrations : IDisposable {

        private List<CancellationTokenRegistration>? _registrations;

        public void Add(CancellationTokenRegistration registration) {
            if (registration == default) {
                return;
            }

            (_registrations ??= new List<CancellationTokenRegistration>()).Add(registration);
        }

        public void Dispose() {
            if (_registrations == null) {
                return;
            }

            foreach (var registration in _registrations) {
                registration.Dispose();
            }

            _registrations = null;
        }
    }
}
