using System;

namespace Cocoar.SignalARRR.Common.Serialization {
    /// <summary>
    /// Abstraction for protocol-level serialization/deserialization.
    /// Handles conversion of wire protocol values to typed objects.
    /// <see cref="JsonProtocolSerializer"/> handles both hub protocols: JSON values directly,
    /// MessagePack values through a JSON round-trip.
    /// </summary>
    public interface IProtocolSerializer {

        /// <summary>
        /// Try to convert a wire value to the target type.
        /// The value may be a JsonElement (JSON protocol), a MessagePack primitive,
        /// or already the correct type.
        /// </summary>
        object? ConvertTo(object? value, Type targetType);

        /// <summary>
        /// Try to convert a wire value to a specific type.
        /// Returns null if conversion fails.
        /// </summary>
        T? TryConvertTo<T>(object? value) where T : class;
    }
}
