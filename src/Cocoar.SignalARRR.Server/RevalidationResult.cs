using System;

namespace Cocoar.SignalARRR.Server {

    /// <summary>What re-validating a connection's credentials concluded.</summary>
    public enum RevalidationOutcome {

        /// <summary>The credentials still hold. The call proceeds.</summary>
        Valid,

        /// <summary>
        /// The credentials no longer hold. This call is refused; the connection stays open.
        /// </summary>
        Deny,

        /// <summary>
        /// The credentials no longer hold and the connection should not survive it.
        /// </summary>
        /// <remarks>
        /// For a request/response API, refusing the call is the whole answer — the caller gets an
        /// error and knows. On a long-lived push connection it leaves a state worse than either
        /// alternative: the client believes it is connected, the server will not serve it, and
        /// nothing tells either side to give up. An application whose presence display is derived
        /// from live connections would go on showing a revoked session as present while it received
        /// nothing.
        /// </remarks>
        Abort
    }

    /// <summary>
    /// The outcome of a re-validation, and how long it may be trusted.
    /// </summary>
    /// <remarks>
    /// The validity window exists because <c>AuthCacheDuration</c> is one number for the whole
    /// server, while credentials on the same hub differ: a browser cookie and a reference token
    /// whose revocation should be noticed within seconds do not want the same cadence. Returning a
    /// window lets the service that actually knows say so, instead of the configuration being set
    /// for the strictest case and everything else paying for it.
    /// </remarks>
    public readonly struct RevalidationResult {

        public RevalidationOutcome Outcome { get; }

        /// <summary>
        /// How long this verdict may be cached. <c>null</c> uses the configured
        /// <c>AuthCacheDuration</c>; <see cref="TimeSpan.Zero"/> re-validates on the next call.
        /// Ignored unless the outcome is <see cref="RevalidationOutcome.Valid"/>.
        /// </summary>
        public TimeSpan? ValidFor { get; }

        private RevalidationResult(RevalidationOutcome outcome, TimeSpan? validFor) {
            Outcome = outcome;
            ValidFor = validFor;
        }

        /// <summary>Good, for the configured cache duration.</summary>
        public static RevalidationResult Valid() => new(RevalidationOutcome.Valid, null);

        /// <summary>Good, for as long as the caller says.</summary>
        public static RevalidationResult ValidForDuration(TimeSpan duration) {
            if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
            return new RevalidationResult(RevalidationOutcome.Valid, duration);
        }

        /// <summary>Refuse this call, leave the connection up.</summary>
        public static RevalidationResult Deny() => new(RevalidationOutcome.Deny, null);

        /// <summary>Refuse this call and drop the connection.</summary>
        public static RevalidationResult Abort() => new(RevalidationOutcome.Abort, null);

        public static implicit operator RevalidationResult(bool valid) => valid ? Valid() : Deny();
    }
}
