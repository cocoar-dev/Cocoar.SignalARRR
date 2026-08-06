using System;
using System.Text.Json;

namespace Cocoar.SignalARRR.Common {
    /// <summary>
    /// Structured error envelope for SignalARRR exceptions.
    /// The server serializes it as pure JSON into the HubException message string.
    /// </summary>
    /// <remarks>
    /// <see cref="Code"/> is the machine-readable contract (see <see cref="HARRRErrorCodes"/>);
    /// <see cref="Message"/> is for humans; <see cref="Type"/> is the .NET exception type name and
    /// exists for .NET-side diagnostics only — non-.NET clients should never need it.
    /// </remarks>
    public class HARRRError {
        /// <summary>
        /// Version of this envelope's shape. 1 since the error contract rework; absent (0) on
        /// messages from older servers.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("Version")]
        public int Version { get; set; }

        /// <summary>
        /// Machine-readable error code — the field clients branch on. Unknown or missing codes
        /// must be treated as <see cref="HARRRErrorCodes.Internal"/>.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("Code")]
        public string? Code { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Type")]
        public string Type { get; set; } = "Error";

        [System.Text.Json.Serialization.JsonPropertyName("Message")]
        public string Message { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("StackTrace")]
        public string? StackTrace { get; set; }

        /// <summary>
        /// The cause chain, nested instead of flattened: previously only
        /// <c>GetBaseException()</c> survived the wire, which discarded every intermediate step.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyName("InnerError")]
        public HARRRError? InnerError { get; set; }

        /// <summary>
        /// The <see cref="Code"/> folded to a value this client version knows
        /// (unknown/missing → <see cref="HARRRErrorCodes.Internal"/>).
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string NormalizedCode => HARRRErrorCodes.Normalize(Code);

        /// <summary>
        /// Try to parse a HubException message into a structured HARRRError.
        /// Supports pure JSON (current servers), the SignalR-prefixed form, and the legacy
        /// [Type] Message format.
        /// </summary>
        public static HARRRError Parse(string message) {
            // Current servers put pure JSON into HubException.Message; on some paths SignalR
            // prefixes it ("... HARRRException: {json}"), so the JSON part is extracted first.
            var jsonCandidate = message;
            var marker = "HARRRException: ";
            var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0) {
                jsonCandidate = message.Substring(markerIndex + marker.Length);
            }

            try {
                var error = JsonSerializer.Deserialize<HARRRError>(jsonCandidate);
                // Accepted when it carries actual error content — a versioned envelope, a code, or
                // a concrete type. A bare "{}" (or unrelated JSON) falls through to the fallbacks.
                if (error != null && (error.Version >= 1 || !string.IsNullOrEmpty(error.Code) || (!string.IsNullOrEmpty(error.Type) && error.Type != "Error"))) {
                    return error;
                }
            } catch {
                // Not JSON — try legacy format
            }

            // Legacy format: [Type] Message (may also be after the SignalR prefix)
            var match = System.Text.RegularExpressions.Regex.Match(message, @"\[([\w.]+)\]\s*(.*)");
            if (match.Success) {
                return new HARRRError {
                    Type = match.Groups[1].Value,
                    Message = match.Groups[2].Value,
                };
            }

            // Fallback
            return new HARRRError {
                Type = "Error",
                Message = message,
            };
        }

        /// <summary>
        /// Try to parse a HubException into a structured HARRRError.
        /// </summary>
        public static HARRRError Parse(Exception exception) {
            return Parse(exception.Message);
        }
    }
}
