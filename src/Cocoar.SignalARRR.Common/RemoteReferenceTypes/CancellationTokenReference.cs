using System;
using System.Text.Json.Serialization;

namespace Cocoar.SignalARRR.Common.RemoteReferenceTypes {
    public class CancellationTokenReference {

        /// <summary>Marks this argument as a reference; see <see cref="RemoteReferenceKinds"/>.</summary>
        [JsonPropertyName(RemoteReferenceKinds.PropertyName)]
        public string Type { get; set; } = RemoteReferenceKinds.CancellationToken;

        [JsonPropertyName("Id")]
        public Guid Id { get; set; } = Guid.NewGuid();
    }
}
