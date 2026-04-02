namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// Determines how a client authenticates with the SignalARRR hub.
    /// </summary>
    public enum AuthenticationMode {

        /// <summary>
        /// Not yet determined. Will be resolved on the first auth-required call
        /// based on whether the client provides a token or uses transport-level credentials.
        /// </summary>
        None,

        /// <summary>
        /// Token-based authentication (Bearer, Basic, API Key).
        /// The client sends a credential string per message in ClientRequestMessage.Authorization.
        /// On cache expiry, the server challenges the client for a fresh token.
        /// </summary>
        MessageLevel,

        /// <summary>
        /// Transport-level authentication (client certificates, cookies, Windows/Negotiate).
        /// The client authenticates at connection time; no token is sent per message.
        /// On cache expiry, the server re-validates the stored credentials server-side.
        /// </summary>
        TransportLevel
    }
}
