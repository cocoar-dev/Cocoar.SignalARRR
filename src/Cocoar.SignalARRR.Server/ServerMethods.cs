using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    public class ServerMethods {
        public ClientContext ClientContext { get; set; }

        public HubCallerContext Context { get; set; }

        public IHubCallerClients Clients { get; set; }

        public IGroupManager Groups { get; set; }

        public ILogger Logger { get; set; }
    }

    public class ServerMethods<T> : ServerMethods where T : HARRR {

    }

}
