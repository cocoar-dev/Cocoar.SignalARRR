using System;

namespace Cocoar.SignalARRR.Common.Exceptions {
    /// <summary>
    /// No client answered an invoke — locally or across the backplane. Derives from
    /// <see cref="InvalidOperationException"/> because that is what callers historically caught.
    /// </summary>
    public class NoClientRespondedException : InvalidOperationException {
        public NoClientRespondedException(string message) : base(message) {
        }

        public NoClientRespondedException(string message, Exception innerException) : base(message, innerException) {
        }
    }
}
