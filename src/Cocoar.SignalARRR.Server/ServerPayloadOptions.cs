using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The hub protocol's JSON options. <c>AddSignalARRR</c> appends SignalARRR's own contracts to them
    /// behind the app's (<see cref="SignalARRRJson.AddTo"/>), before SignalR first uses them.
    /// Arguments are read with these, the options SignalR itself uses for every payload, so the
    /// app's converters and source-generated contexts apply, and under NativeAOT only the app's
    /// context has to name its types.
    /// </summary>
    internal sealed class ServerPayloadOptions {

        public ServerPayloadOptions(IOptions<JsonHubProtocolOptions> hubProtocolOptions) {
            Options = hubProtocolOptions.Value.PayloadSerializerOptions;
        }

        public JsonSerializerOptions Options { get; }
    }
}
