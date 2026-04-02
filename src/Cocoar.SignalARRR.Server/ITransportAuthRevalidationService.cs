using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// Validates transport-level credentials (client certificates, cookies, Windows/Negotiate)
    /// when the authentication cache expires. Implement this interface to provide custom
    /// revalidation logic (e.g., custom CRL endpoints, OCSP stapling, session store checks).
    /// </summary>
    public interface ITransportAuthRevalidationService {

        /// <summary>
        /// Re-validates the transport-level credentials for the given client.
        /// Called when the auth cache expires for a transport-authenticated client.
        /// </summary>
        /// <param name="clientContext">The client context containing the stored certificate and/or principal.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the credentials are still valid; false to reject the client.</returns>
        Task<bool> RevalidateAsync(ClientContext clientContext, CancellationToken cancellationToken = default);
    }
}
