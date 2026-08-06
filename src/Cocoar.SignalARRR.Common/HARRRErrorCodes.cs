using System;

namespace Cocoar.SignalARRR.Common {
    /// <summary>
    /// The machine-readable error codes of the wire contract. Clients branch on these — a
    /// TypeScript or Swift client can do nothing with a .NET type name, so the code is the only
    /// cross-language signal. The set is small and stable by design: one code per pipeline stage,
    /// not one per exception type.
    /// </summary>
    /// <remarks>
    /// The first four mean "the caller got it wrong" (fix the call), the last four describe the
    /// state of the world (retry, re-authenticate, or surface to the user). Application code may
    /// put its own codes on the wire by throwing <c>HARRRException(code, message)</c> — those
    /// travel verbatim and are never overwritten by the framework. Consumers should use
    /// lower_snake_case for their own codes; the names below are reserved.
    /// <para>
    /// Forward compatibility: a client that meets a code it does not know must treat it like
    /// <see cref="Internal"/>. That allows the set to grow without breaking older clients.
    /// </para>
    /// </remarks>
    public static class HARRRErrorCodes {

        /// <summary>Authorization rejected the call.</summary>
        public const string Unauthorized = "unauthorized";

        /// <summary>No method (or interface) is registered under the requested name.</summary>
        public const string MethodNotFound = "method_not_found";

        /// <summary>The name exists, but no registered method accepts this argument count.</summary>
        public const string InvalidArgumentCount = "invalid_argument_count";

        /// <summary>An argument could not be deserialized or coerced to the parameter type.</summary>
        public const string ArgumentBindingFailed = "argument_binding_failed";

        /// <summary>The call was cancelled — an expected outcome, not a failure.</summary>
        public const string Cancelled = "cancelled";

        /// <summary>A server-side deadline expired (e.g. waiting for a stream upload).</summary>
        public const string Timeout = "timeout";

        /// <summary>No client answered the invoke (locally or across the backplane).</summary>
        public const string NoClientResponded = "no_client_responded";

        /// <summary>The invoked method itself threw — the default bucket.</summary>
        public const string Internal = "internal";

        /// <summary>
        /// Maps a wire code to one this client version knows, folding unknown or missing codes to
        /// <see cref="Internal"/> so the set can grow additively.
        /// </summary>
        public static string Normalize(string? code) {
            switch (code) {
                case Unauthorized:
                case MethodNotFound:
                case InvalidArgumentCount:
                case ArgumentBindingFailed:
                case Cancelled:
                case Timeout:
                case NoClientResponded:
                case Internal:
                    return code;
                default:
                    return Internal;
            }
        }
    }
}
