using Microsoft.AspNetCore.SignalR;

namespace IntegrationTestServer {

    /// <summary>
    /// A plain SignalR hub, deliberately not a <c>HARRR</c>, used as the control in
    /// <c>StatefulReconnectTests</c>.
    /// </summary>
    /// <remarks>
    /// Without it, a failed resume on the SignalARRR hub proves nothing: it could equally be the test
    /// harness, the proxy, or SignalR itself. Running the identical sever-and-resume sequence against
    /// a hub that has none of SignalARRR in it separates "SignalARRR breaks resumption" from
    /// "resumption does not happen here at all".
    /// </remarks>
    public class PlainReconnectHub : Hub {
        public string Echo(string value) => value;
    }
}
