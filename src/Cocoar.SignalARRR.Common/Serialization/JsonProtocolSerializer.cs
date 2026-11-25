using System;
using System.Text.Json;

namespace Cocoar.SignalARRR.Common.Serialization {
    /// <summary>
    /// JSON-based protocol serializer. Converts the values SignalR's hub protocol delivered into the
    /// types a method expects.
    /// </summary>
    /// <remarks>
    /// It converts with the hub protocol's own <c>PayloadSerializerOptions</c>, the same options the
    /// values were written with, so the app's converters, naming policy and source-generated
    /// contexts apply. It used to carry options of its own without a resolver, which fall back to
    /// reflection-based JSON: with that switched off (NativeAOT) every conversion threw, and
    /// fire-and-forget server pushes were lost without a trace. Every conversion goes through
    /// <see cref="JsonSerializerOptions.GetTypeInfo"/>, so a type the options do not know fails with
    /// System.Text.Json's message naming it.
    /// </remarks>
    public class JsonProtocolSerializer : IProtocolSerializer {

        private readonly JsonSerializerOptions _options;

        public JsonProtocolSerializer(JsonSerializerOptions options) {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public object? ConvertTo(object? value, Type targetType) {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;

            var targetInfo = _options.GetTypeInfo(targetType);
            if (value is JsonElement je) {
                return je.Deserialize(targetInfo);
            }

            // Values that did not come in as JSON (MessagePack delivers plain .NET objects) take a
            // JSON round-trip through the same options.
            var element = JsonSerializer.SerializeToElement(value, _options.GetTypeInfo(value.GetType()));
            return element.Deserialize(targetInfo);
        }

        public T? TryConvertTo<T>(object? value) where T : class {
            if (value == null) return null;
            if (value is T typed) return typed;

            try {
                return (T?)ConvertTo(value, typeof(T));
            } catch {
                return null;
            }
        }
    }
}
