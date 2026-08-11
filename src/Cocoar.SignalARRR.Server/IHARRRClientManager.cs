using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server {
    internal interface IHARRRClientManager {
        ClientContext Register(HARRR huc, HubCallerContext hubContext);

        /// <summary>Removes the connection, or <c>null</c> if it was not registered.</summary>
        ClientContext? UnRegister(string connectionId);

        /// <summary>The connection's context, or <c>null</c> if it is not registered here.</summary>
        ClientContext? GetClient(string connectionId);

        IEnumerable<ClientContext> GetClients();
    }
}
