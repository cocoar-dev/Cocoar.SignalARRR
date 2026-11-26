using System.Text.Json.Serialization;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The inter-node wire format of the backplanes, source-generated so it serializes without
    /// reflection. The arguments inside a message are the app's types; those come from the hub
    /// protocol's resolvers, combined behind this one.
    /// </summary>
    /// <remarks>
    /// Deliberately without <c>[JsonSourceGenerationOptions]</c>: with it, the generated metadata
    /// resolves the runtime type of an <c>object</c> value (the message arguments) through the
    /// context's own options instead of the combined chain, and every app type fails. The Web
    /// defaults the backplanes always used come from the options the backplane builds, so the format
    /// on the wire is unchanged and nodes of different versions keep understanding each other.
    /// </remarks>
    [JsonSerializable(typeof(SignalARRRBackplaneEnvelope))]
    [JsonSerializable(typeof(SignalARRRConnectionRegistration))]
    [JsonSerializable(typeof(SignalARRRConnectionAttribute[]))]
    internal partial class SignalARRRBackplaneJsonContext : JsonSerializerContext {
    }
}
