using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// Validates transport-level credentials (client certificates, Windows/Negotiate, and any scheme
    /// declared in <c>SignalARRRServerOptions.ConnectionBoundSchemes</c>) when the authentication
    /// cache expires. Implement this interface to provide custom revalidation logic — a custom CRL
    /// endpoint, OCSP stapling, a session store or introspection check.
    /// </summary>
    public interface ITransportAuthRevalidationService {

        /// <summary>
        /// Re-validates the transport-level credentials for the given client.
        /// Called when the auth cache expires for a transport-authenticated client.
        /// </summary>
        /// <param name="clientContext">The client context holding the stored certificate and principal.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// Whether the credentials still hold, how long that verdict may be cached, and — when they
        /// do not — whether the connection should be dropped rather than merely refused. Returning a
        /// <see cref="bool"/> works too: it converts to
        /// <see cref="RevalidationResult.Valid()"/> or <see cref="RevalidationResult.Deny()"/>.
        /// </returns>
        Task<RevalidationResult> RevalidateAsync(ClientContext clientContext, CancellationToken cancellationToken = default);
    }
}
