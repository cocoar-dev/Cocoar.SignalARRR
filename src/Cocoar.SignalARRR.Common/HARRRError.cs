using System;
using System.Text.Json;

namespace Cocoar.SignalARRR.Common {
    /// <summary>
    /// Structured error envelope for SignalARRR exceptions.
    /// The server serializes exceptions as JSON in the HubException message string.
    /// </summary>
    public class HARRRError {
        [System.Text.Json.Serialization.JsonPropertyName("Type")]
        public string Type { get; set; } = "Error";

        [System.Text.Json.Serialization.JsonPropertyName("Message")]
        public string Message { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("StackTrace")]
        public string? StackTrace { get; set; }

        /// <summary>
        /// Try to parse a HubException message into a structured HARRRError.
        /// Supports both JSON format and legacy [Type] Message format.
        /// </summary>
        public static HARRRError Parse(string message) {
            // SignalR wraps HubException messages with prefix text like:
            // "An unexpected error occurred invoking '...' on the server. HARRRException: {json}"
            // Extract the JSON portion after "HARRRException: " if present
            var jsonCandidate = message;
            var marker = "HARRRException: ";
            var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0) {
                jsonCandidate = message.Substring(markerIndex + marker.Length);
            }

            // Try JSON format
            try {
                var error = JsonSerializer.Deserialize<HARRRError>(jsonCandidate);
                if (error != null && !string.IsNullOrEmpty(error.Type) && error.Type != "Error") {
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
