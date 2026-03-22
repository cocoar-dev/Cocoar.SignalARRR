using System;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server {
    public class HARRRException : HubException {

        public HARRRException(Exception exception) : base(SerializeError(exception)) {
        }

        public HARRRException(string type, string message) : base(SerializeError(type, message)) {
        }

        private static string SerializeError(Exception exception) {
            var baseEx = exception.GetBaseException();
            var error = new HARRRError {
                Type = baseEx.GetType().FullName!,
                Message = baseEx.Message,
#if DEBUG
                StackTrace = baseEx.StackTrace,
#endif
            };
            return JsonSerializer.Serialize(error);
        }

        private static string SerializeError(string type, string message) {
            var error = new HARRRError {
                Type = type,
                Message = message,
            };
            return JsonSerializer.Serialize(error);
        }
    }

    /// <summary>
    /// Structured error envelope for SignalARRR exceptions.
    /// Serialized as JSON in the HubException message string.
    /// </summary>
    public class HARRRError {
        public string Type { get; set; } = "Error";
        public string Message { get; set; } = "";
        public string? StackTrace { get; set; }
    }
}
