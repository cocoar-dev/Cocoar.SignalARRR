using System.Text.Json.Serialization;

namespace Cocoar.SignalARRR.Common.RemoteReferenceTypes {
    public class StreamReference {
        [JsonPropertyName("Uri")]
        public string Uri { get; set; } = string.Empty;
    }
}
