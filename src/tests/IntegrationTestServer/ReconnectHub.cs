using System;
using Cocoar.SignalARRR.Server;

namespace IntegrationTestServer {

    /// <summary>
    /// A SignalARRR hub of its own for <c>StatefulReconnectTests</c>, kept apart from
    /// <see cref="TestHub"/> on purpose.
    /// </summary>
    /// <remarks>
    /// That test deliberately leaves a connection with a dead transport registered in
    /// <c>ClientManager</c> for a moment. Tests in other collections run in parallel against this
    /// same server process, and several of them broadcast to <em>every</em> client on their hub —
    /// which would then try to reach the corpse and fail with a 500 that has nothing to do with
    /// them. It happened: three macOS legs went red on <c>BroadcastTests</c> and
    /// <c>ServerPushTests</c>, not on the reconnect test itself.
    /// <para>
    /// Scoping the reconnect client to its own hub keeps it out of every <c>WithHub&lt;TestHub&gt;()</c>
    /// query. The <c>ClientManager</c> registration and group tracking under test are hub-agnostic,
    /// so nothing is lost by moving.
    /// </para>
    /// </remarks>
    public class ReconnectHub : HARRR {

        public ReconnectHub(IServiceProvider serviceProvider) : base(serviceProvider) {
        }

        public string GetName() => "MyName";
    }
}
