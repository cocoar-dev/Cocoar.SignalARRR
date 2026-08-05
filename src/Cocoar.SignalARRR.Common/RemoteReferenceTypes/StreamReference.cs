using System.Text.Json.Serialization;

namespace Cocoar.SignalARRR.Common.RemoteReferenceTypes {
    public class StreamReference {

        /// <summary>Marks this argument as a reference; see <see cref="RemoteReferenceKinds"/>.</summary>
        [JsonPropertyName(RemoteReferenceKinds.PropertyName)]
        public string Type { get; set; } = RemoteReferenceKinds.Stream;

        [JsonPropertyName("Uri")]
        public string Uri { get; set; } = string.Empty;
    }
}
