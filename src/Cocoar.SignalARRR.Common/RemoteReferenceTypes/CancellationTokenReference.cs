using System;

namespace Cocoar.SignalARRR.Common.RemoteReferenceTypes {
    public class CancellationTokenReference {
        public Guid Id { get; set; } = Guid.NewGuid();
    }
}
