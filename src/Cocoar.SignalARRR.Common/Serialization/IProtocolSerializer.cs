using System;

namespace Cocoar.SignalARRR.Common.Serialization {
    /// <summary>
    /// Abstraction for protocol-level serialization/deserialization.
    /// Handles conversion of wire protocol values to typed objects.
    /// Implementations exist for JSON (default) and MessagePack.
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
