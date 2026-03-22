using System;
using System.Text.Json.Serialization;

namespace Cocoar.SignalARRR.Common.RemoteReferenceTypes {
    public class CancellationTokenReference {
        [JsonPropertyName("Id")]
        public Guid Id { get; set; } = Guid.NewGuid();
    }
}
