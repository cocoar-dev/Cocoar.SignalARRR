using System;
using System.Text.Json;

namespace Cocoar.SignalARRR.Common.Serialization {
    /// <summary>
    /// JSON-based protocol serializer. Handles JsonElement values from SignalR's JSON hub protocol.
    /// </summary>
    public class JsonProtocolSerializer : IProtocolSerializer {

        private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() {
            PropertyNameCaseInsensitive = true,
        };

        public object? ConvertTo(object? value, Type targetType) {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;

            if (value is JsonElement je) {
                return je.Deserialize(targetType, CaseInsensitiveOptions);
            }

            // Fallback: JSON round-trip for other representations
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize(json, targetType, CaseInsensitiveOptions);
        }

        public T? TryConvertTo<T>(object? value) where T : class {
            if (value == null) return null;
            if (value is T typed) return typed;

            try {
                if (value is JsonElement je) {
                    return je.Deserialize<T>(CaseInsensitiveOptions);
                }

                var json = JsonSerializer.Serialize(value);
                return JsonSerializer.Deserialize<T>(json, CaseInsensitiveOptions);
            } catch {
                return null;
            }
        }
    }
}
