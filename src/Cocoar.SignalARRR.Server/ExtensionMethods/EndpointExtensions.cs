using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    internal static class EndpointExtensions {

        public static bool IsSignalREndpoint(this Endpoint endpoint) {

            return endpoint?.Metadata.GetMetadata<HubMetadata>() != null;
        }

    }
}
