using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// Decides whether a connection's credentials are bound to the transport, and whether the
    /// identity they produced has outlived its stated validity.
    /// </summary>
    /// <remarks>
    /// Kept as a pure decision, separate from <see cref="ClientContext"/>, because it is the hinge
    /// of an authentication escalation and needs to be verifiable on its own.
    /// </remarks>
    internal static class TransportCredentialPolicy {

        /// <summary>
        /// Authentication schemes whose credential is the connection itself, so the identity
        /// established at handshake stays meaningful for the connection's lifetime.
        /// </summary>
        private static readonly string[] ConnectionBoundSchemes = {
            "Negotiate", "NTLM", "Kerberos", "Windows", "Certificate"
        };

        /// <summary>
        /// Indicates whether the credentials are bound to the transport rather than to a message.
        /// </summary>
        /// <remarks>
        /// Deliberately narrower than "is authenticated". A principal derived from a bearer token is
        /// authenticated too, but its validity is bounded by the token, not by the connection.
        /// Treating it as transport-level is what let a client escape token expiry: answering an
        /// authentication challenge with an empty string moved it to transport mode, where
        /// revalidation then approved it against that very same cached principal — permanently,
        /// because the mode persists for the connection.
        /// </remarks>
        /// <param name="additionalSchemes">
        /// Schemes the application has declared connection-bound via
        /// <c>SignalARRRServerOptions.ConnectionBoundSchemes</c>. Deliberately a parameter rather
        /// than a lookup: this decision is the hinge of an escalation and stays a pure function.
        /// </param>
        public static bool IsTransportLevel(
            X509Certificate2? certificate, ClaimsPrincipal? user, IReadOnlyList<string>? additionalSchemes = null) {

            if (certificate != null) {
                return true;
            }

            var identity = user?.Identity;
            if (identity?.IsAuthenticated != true || identity.AuthenticationType == null) {
                return false;
            }

            if (Array.Exists(ConnectionBoundSchemes,
                scheme => string.Equals(scheme, identity.AuthenticationType, StringComparison.OrdinalIgnoreCase))) {
                return true;
            }

            if (additionalSchemes == null) {
                return false;
            }

            for (var i = 0; i < additionalSchemes.Count; i++) {
                if (string.Equals(additionalSchemes[i], identity.AuthenticationType, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Indicates whether the principal states an expiry that has already passed.
        /// </summary>
        /// <remarks>
        /// Defence in depth: even a transport-level identity is rejected once its ticket says it has
        /// expired, so no path can extend a credential beyond its own stated lifetime.
        /// </remarks>
        public static bool IsExpired(ClaimsPrincipal? user) {

            var expiry = user?.FindFirst("exp") ?? user?.FindFirst(ClaimTypes.Expiration);
            if (expiry == null) {
                return false;
            }

            // JWT "exp" is Unix seconds; ClaimTypes.Expiration is a formatted timestamp.
            if (long.TryParse(expiry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds)) {
                return DateTimeOffset.FromUnixTimeSeconds(unixSeconds) < DateTimeOffset.UtcNow;
            }

            if (DateTimeOffset.TryParse(expiry.Value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var timestamp)) {
                return timestamp < DateTimeOffset.UtcNow;
            }

            // Unparseable expiry: fail closed rather than silently ignoring it.
            return true;
        }
    }
}
