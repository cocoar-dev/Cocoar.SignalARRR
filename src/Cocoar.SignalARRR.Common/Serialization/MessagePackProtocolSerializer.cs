using System;
using MessagePack;

namespace Cocoar.SignalARRR.Common.Serialization {
    /// <summary>
    /// MessagePack-based protocol serializer. Handles values from SignalR's MessagePack hub protocol.
    /// With MessagePack, arguments arrive as native .NET types (int, string, etc.) or as
    /// MessagePack primitives that need conversion.
    /// </summary>
    public class MessagePackProtocolSerializer : IProtocolSerializer {

        private static readonly MessagePackSerializerOptions Options =
            MessagePackSerializerOptions.Standard.WithResolver(
                MessagePack.Resolvers.ContractlessStandardResolver.Instance);

        public object? ConvertTo(object? value, Type targetType) {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;

            // MessagePack round-trip: serialize to bytes, then deserialize as target type
            try {
                var bytes = MessagePackSerializer.Serialize(value, Options);
                return MessagePackSerializer.Deserialize(targetType, bytes, Options);
            } catch {
                // Fallback: try direct conversion
                try {
                    return Convert.ChangeType(value, targetType);
                } catch {
                    return null;
                }
            }
        }

        public T? TryConvertTo<T>(object? value) where T : class {
            if (value == null) return null;
            if (value is T typed) return typed;

            try {
                var bytes = MessagePackSerializer.Serialize(value, Options);
                return MessagePackSerializer.Deserialize<T>(bytes, Options);
            } catch {
                return null;
            }
        }
    }
}
