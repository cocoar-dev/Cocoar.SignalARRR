using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Cocoar.SignalARRR.ProxyGenerator {
    /// <summary>
    /// Reads arguments with the hub protocol's JSON options: the options they were written with, so
    /// the app's converters, naming policy and source-generated contexts apply.
    /// </summary>
    /// <remarks>
    /// Every read goes through <see cref="JsonSerializerOptions.GetTypeInfo"/>, never through
    /// reflection-based JSON, which is what keeps it working under NativeAOT. Values that did not
    /// arrive as JSON (the MessagePack protocol delivers plain .NET objects) take a JSON round-trip
    /// through the same options; that needs a contract for their runtime type, which under NativeAOT
    /// only exists for types an app declared — one reason MessagePack is not supported there.
    /// </remarks>
    public sealed class JsonArgumentReader : ArgumentReader {

        private readonly JsonSerializerOptions _options;

        public JsonArgumentReader(IReadOnlyList<object?> arguments, JsonSerializerOptions options, CallContext context)
            : base(arguments, context) {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public override T Read<T>(int index) {
            var value = GetRaw(index);
            switch (value) {
                case null:
                    return default!;
                case T typed:
                    return typed;
                case JsonElement element:
                    // null used to bind as default(T) even for value types; kept that way.
                    return element.ValueKind == JsonValueKind.Null ? default! : element.Deserialize(TypeInfo<T>())!;
                default:
                    var roundTrip = JsonSerializer.SerializeToElement(value, _options.GetTypeInfo(value.GetType()));
                    return roundTrip.Deserialize(TypeInfo<T>())!;
            }
        }

        private JsonTypeInfo<T> TypeInfo<T>() => (JsonTypeInfo<T>)_options.GetTypeInfo(typeof(T));
    }
}
