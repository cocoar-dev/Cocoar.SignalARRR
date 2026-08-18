using System;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Client {

    public class HARRRConnectionOptions {

        /// <summary>
        /// The credential SignalARRR sends: the <c>Authorization</c> field of every message, the
        /// answer to an authentication challenge, and the file-transfer requests.
        /// </summary>
        /// <remarks>
        /// This is a different thing from SignalR's own <c>AccessTokenProvider</c>, which
        /// authenticates the connection — the negotiate request and the transport. The two are
        /// checked by different things: SignalR's by <c>[Authorize]</c> on the hub class, this one by
        /// <c>[Authorize]</c> on a method or a <c>ServerMethods</c> class. Pass the same provider to
        /// both when it is the same credential, which is the common case.
        /// <para>
        /// SignalARRR used to adopt SignalR's provider by reflecting into two levels of its private
        /// fields, which meant the two could never be told apart, and a credential meant for the
        /// connection alone — a single-use ticket, say — was resent with every message. Nothing is
        /// adopted now: a connection that authenticates per message has to say so here, or the server
        /// will challenge it for a credential it never sends once the auth cache expires.
        /// </para>
        /// </remarks>
        public Func<Task<string>>? Authorization { get; set; }
    }

    public class HARRRConnectionOptionsBuilder {

        private HARRRConnectionOptions Options { get; } = new HARRRConnectionOptions();

        public static implicit operator HARRRConnectionOptions(HARRRConnectionOptionsBuilder builder) {
            return builder?.Options!;
        }

        /// <summary>Resolves the credential per call — for a token that expires or is refreshed.</summary>
        public HARRRConnectionOptionsBuilder WithAuthorization(Func<Task<string>> authorization) {
            Options.Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            return this;
        }

        /// <summary>Resolves the credential per call, synchronously.</summary>
        public HARRRConnectionOptionsBuilder WithAuthorization(Func<string> authorization) {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            Options.Authorization = () => Task.FromResult(authorization());
            return this;
        }

        /// <summary>A credential that is already at hand and does not change.</summary>
        public HARRRConnectionOptionsBuilder WithAuthorization(string authorization) {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            Options.Authorization = () => Task.FromResult(authorization);
            return this;
        }
    }
}
