using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server
{
    internal interface IHARRRClientManager
    {
        ClientContext Register(HARRR huc, HubCallerContext hubContext);
        ClientContext UnRegister(string connectionId);
        ClientContext GetClient(string connectionId);
        IEnumerable<ClientContext> GetClients();
    }
}
