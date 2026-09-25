using System;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Client.FullFramework {

    /// <summary>
    /// SignalARRR's options for a connection — the same credential options as every other SignalARRR
    /// client. The <b>connection credential</b> authenticates negotiate and the transport and is what
    /// <c>[Authorize]</c> on the hub class checks; the <b>message credential</b> travels with every
    /// message, answers authentication challenges and authenticates file transfers, and is what
    /// <c>[Authorize]</c> on a method or a <c>ServerMethods</c> class checks. Nothing is coupled
    /// implicitly: setting one of the two sets only that one.
    /// </summary>
    public class HARRRConnectionOptions {

        /// <summary>
        /// Authenticates the connection: the negotiate request and the transport. Set on SignalR's
        /// <c>AccessTokenProvider</c>, so it is called on every connect. Only available through
        /// <c>HARRRConnection.Create(builder => ..., options => ...)</c>; a <c>HubConnection</c> that is
        /// already built has its own.
        /// </summary>
        public Func<Task<string>> ConnectionCredential { get; set; }

        /// <summary>
        /// The credential SignalARRR sends with every message, as the answer to an authentication
        /// challenge, and on file-transfer requests. Called per use, so a refreshed token is picked up.
        /// </summary>
        public Func<Task<string>> MessageCredential { get; set; }
    }

    public class HARRRConnectionOptionsBuilder {

        private HARRRConnectionOptions Options { get; } = new HARRRConnectionOptions();

        public static implicit operator HARRRConnectionOptions(HARRRConnectionOptionsBuilder builder) {
            return builder?.Options;
        }

        /// <summary>
        /// One credential for the connection and for every message — the common case. Resolved per
        /// use, for a token that expires or is refreshed.
        /// </summary>
        public HARRRConnectionOptionsBuilder WithCredential(Func<Task<string>> credential) {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            Options.ConnectionCredential = credential;
            Options.MessageCredential = credential;
            return this;
        }

        /// <summary>One credential for the connection and for every message, resolved synchronously.</summary>
        public HARRRConnectionOptionsBuilder WithCredential(Func<string> credential) {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return WithCredential(() => Task.FromResult(credential()));
        }

        /// <summary>One credential for the connection and for every message that does not change.</summary>
        public HARRRConnectionOptionsBuilder WithCredential(string credential) {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return WithCredential(() => Task.FromResult(credential));
        }

        /// <summary>
        /// Authenticates the connection only — negotiate and the transport, what <c>[Authorize]</c>
        /// on the hub class checks. Called on every connect.
        /// </summary>
        public HARRRConnectionOptionsBuilder WithConnectionCredential(Func<Task<string>> credential) {
            Options.ConnectionCredential = credential ?? throw new ArgumentNullException(nameof(credential));
            return this;
        }

        /// <summary>Authenticates the connection only, resolved synchronously.</summary>
        public HARRRConnectionOptionsBuilder WithConnectionCredential(Func<string> credential) {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return WithConnectionCredential(() => Task.FromResult(credential()));
        }

        /// <summary>Authenticates the connection only, with a credential that does not change.</summary>
        public HARRRConnectionOptionsBuilder WithConnectionCredential(string credential) {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return WithConnectionCredential(() => Task.FromResult(credential));
        }

        /// <summary>
        /// Authenticates every message only — what <c>[Authorize]</c> on a method or a
        /// <c>ServerMethods</c> class checks, the answer to a challenge, and file transfers. Resolved
        /// per use.
        /// </summary>
        public HARRRConnectionOptionsBuilder WithMessageCredential(Func<Task<string>> credential) {
            Options.MessageCredential = credential ?? throw new ArgumentNullException(nameof(credential));
            return this;
        }

        /// <summary>Authenticates every message only, resolved synchronously.</summary>
        public HARRRConnectionOptionsBuilder WithMessageCredential(Func<string> credential) {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return WithMessageCredential(() => Task.FromResult(credential()));
        }

        /// <summary>Authenticates every message only, with a credential that does not change.</summary>
        public HARRRConnectionOptionsBuilder WithMessageCredential(string credential) {
            if (credential == null) throw new ArgumentNullException(nameof(credential));
            return WithMessageCredential(() => Task.FromResult(credential));
        }
    }
}
