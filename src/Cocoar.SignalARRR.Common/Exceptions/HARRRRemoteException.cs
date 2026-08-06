using System;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Common.Exceptions {
    /// <summary>
    /// A remote SignalARRR call failed. Thrown by the .NET client with the structured
    /// <see cref="Error"/> attached — previously the caller got a bare <see cref="HubException"/>
    /// whose <c>Message</c> was a raw JSON string, and nothing ever called
    /// <see cref="HARRRError.Parse(string)"/> on it.
    /// </summary>
    /// <remarks>
    /// Derives from <see cref="HubException"/> deliberately: existing <c>catch (HubException)</c>
    /// blocks keep catching everything; the typed access is opt-in. <see cref="Exception.Message"/>
    /// is the human-readable remote message, the raw wire payload stays available through
    /// <see cref="Exception.InnerException"/> when constructed from a received exception.
    /// </remarks>
    public class HARRRRemoteException : HubException {

        /// <summary>The structured error the remote side reported.</summary>
        public HARRRError Error { get; }

        /// <summary>
        /// The machine-readable code, verbatim — application-defined codes (e.g. "room_full")
        /// arrive here unchanged, which is the point of the field. Only a missing code folds to
        /// <see cref="HARRRErrorCodes.Internal"/>. For bucketing framework reactions (retry
        /// policies etc.) use <see cref="HARRRError.NormalizedCode"/>, which folds every code it
        /// does not know.
        /// </summary>
        public string Code => string.IsNullOrEmpty(Error.Code) ? HARRRErrorCodes.Internal : Error.Code!;

        public HARRRRemoteException(HARRRError error, Exception? innerException = null)
            : base(string.IsNullOrEmpty(error.Message) ? error.Type : error.Message, innerException) {
            Error = error;
        }

        /// <summary>
        /// Wraps a received exception, parsing the structured error out of its message.
        /// </summary>
        public static HARRRRemoteException FromReceived(Exception exception) {
            return new HARRRRemoteException(HARRRError.Parse(exception), exception);
        }
    }
}
