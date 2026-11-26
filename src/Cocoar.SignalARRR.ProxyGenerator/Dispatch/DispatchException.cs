using System;

namespace Cocoar.SignalARRR.ProxyGenerator {
    /// <summary>
    /// A server call the client could not match to a method. <see cref="Code"/> carries the same
    /// error code the server uses for the same failure, so the caller can branch on it.
    /// </summary>
    public sealed class DispatchException : Exception {

        /// <summary>Same value as <c>HARRRErrorCodes.MethodNotFound</c>.</summary>
        public const string MethodNotFound = "method_not_found";

        /// <summary>Same value as <c>HARRRErrorCodes.InvalidArgumentCount</c>.</summary>
        public const string InvalidArgumentCount = "invalid_argument_count";

        public string Code { get; }

        public DispatchException(string code, string message) : base(message) {
            Code = code;
        }
    }
}