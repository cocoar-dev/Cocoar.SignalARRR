using System;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Client {

    /// <summary>
    /// SignalARRR's options for a connection. A connection has two credentials, checked by
    /// different things: the <b>connection credential</b> authenticates negotiate and the transport
    /// and is what <c>[Authorize]</c> on the hub class checks; the <b>message credential</b> travels
    /// with every message, answers authentication challenges and authenticates file transfers, and
    /// is what <c>[Authorize]</c> on a method or a <c>ServerMethods</c> class checks.
    /// </summary>
    /// <remarks>
    /// The options are named the same in every SignalARRR client — <c>ConnectionCredential</c>,
    /// <c>MessageCredential</c>, and <c>Credential</c> for both — and nothing is coupled
    /// implicitly: setting one of the two sets only that one.
    /// </remarks>
    public class HARRRConnectionOptions {

        /// <summary>
        /// Authenticates the connection: the negotiate request and the transport. Set on SignalR's
        /// <c>AccessTokenProvider</c>, so it is called on every connect and reconnect. Only available
        /// through <see cref="HARRRConnection.Create(Action{Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder}, Action{HARRRConnectionOptionsBuilder})"/>;
        /// a <c>HubConnection</c> that is already built has its own.
        /// </summary>
        public Func<Task<string>>? ConnectionCredential { get; set; }

        /// <summary>
        /// The credential SignalARRR sends with every message, as the answer to an authentication
        /// challenge, and on file-transfer requests. Called per use, so a refreshed token is picked up.
        /// </summary>
        public Func<Task<string>>? MessageCredential { get; set; }

        /// <summary>
        /// The message credential under its former name. Use <see cref="MessageCredential"/>.
        /// </summary>
        /// <remarks>
        /// SignalARRR used to adopt SignalR's provider by reflecting into two levels of its private
        /// fields, which meant the two could never be told apart, and a credential meant for the
        /// connection alone — a single-use ticket, say — was resent with every message. Nothing is
        /// adopted now: a connection that authenticates per message has to say so, or the server
        /// will challenge it for a credential it never sends once the auth cache expires.
        /// </remarks>
        [Obsolete("Use MessageCredential (or Credential for one credential covering the connection and every message).")]
        public Func<Task<string>>? Authorization { get; set; }

        /// <summary>
        /// The message credential in effect, after checking that the old and the new option are not
        /// both set.
        /// </summary>
        internal Func<Task<string>>? ResolveMessageCredential() {
#pragma warning disable CS0618 // the deprecated option is still honoured
            var legacy = Authorization;
#pragma warning restore CS0618
            if (legacy != null && MessageCredential != null) {
                throw new InvalidOperationException(
                    "Both Authorization and MessageCredential are set. Authorization is the former name of MessageCredential; set only MessageCredential.");
            }
            if (legacy != null) return legacy;
            return MessageCredential;
        }
    }

    public class HARRRConnectionOptionsBuilder {

        private HARRRConnectionOptions Options { get; } = new HARRRConnectionOptions();

        public static implicit operator HARRRConnectionOptions(HARRRConnectionOptionsBuilder builder) {
            return builder?.Options!;
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
        /// on the hub class checks. Called on every connect and reconnect.
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

        /// <summary>The message credential under its former name. Use <see cref="WithMessageCredential(Func{Task{string}})"/>.</summary>
        [Obsolete("Use WithMessageCredential (or WithCredential for one credential covering the connection and every message).")]
        public HARRRConnectionOptionsBuilder WithAuthorization(Func<Task<string>> authorization) {
            Options.Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            return this;
        }

        /// <summary>The message credential under its former name. Use <see cref="WithMessageCredential(Func{string})"/>.</summary>
        [Obsolete("Use WithMessageCredential (or WithCredential for one credential covering the connection and every message).")]
        public HARRRConnectionOptionsBuilder WithAuthorization(Func<string> authorization) {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            Options.Authorization = () => Task.FromResult(authorization());
            return this;
        }

        /// <summary>The message credential under its former name. Use <see cref="WithMessageCredential(string)"/>.</summary>
        [Obsolete("Use WithMessageCredential (or WithCredential for one credential covering the connection and every message).")]
        public HARRRConnectionOptionsBuilder WithAuthorization(string authorization) {
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            Options.Authorization = () => Task.FromResult(authorization);
            return this;
        }
    }
}
