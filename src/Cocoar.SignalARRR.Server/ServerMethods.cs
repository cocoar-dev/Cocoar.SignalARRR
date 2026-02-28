using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    public class ServerMethods {
        public ClientContext ClientContext { get; set; } = null!;

        public HubCallerContext Context { get; set; } = null!;

        public IHubCallerClients Clients { get; set; } = null!;

        public IGroupManager Groups { get; set; } = null!;

        public ILogger Logger { get; set; } = null!;
    }

    public class ServerMethods<T> : ServerMethods where T : HARRR {

    }

}
