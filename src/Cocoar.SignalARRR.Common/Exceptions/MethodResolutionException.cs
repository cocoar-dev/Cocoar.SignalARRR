using System;

namespace Cocoar.SignalARRR.Common.Exceptions {
    /// <summary>
    /// An incoming call could not be resolved to a registered method. Carries the wire error code
    /// distinguishing "the name is unknown" from "the name exists but no method accepts this
    /// argument count" — the caller's fix is different (correct the name vs. correct the
    /// arguments), so the codes are too.
    /// </summary>
    public class MethodResolutionException : Exception {

        /// <summary>
        /// <see cref="HARRRErrorCodes.MethodNotFound"/> or
        /// <see cref="HARRRErrorCodes.InvalidArgumentCount"/>.
        /// </summary>
        public string Code { get; }

        public MethodResolutionException(string code, string message) : base(message) {
            Code = code;
        }
    }
}
