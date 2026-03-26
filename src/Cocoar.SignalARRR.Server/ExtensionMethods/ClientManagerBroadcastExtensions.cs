using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {

    /// <summary>
    /// Extension methods for typed broadcast operations on IEnumerable&lt;ClientContext&gt;.
    /// All broadcast sends collect ConnectionIds and use a single SignalR SendCoreAsync call.
    /// </summary>
    public static class ClientManagerBroadcastExtensions {

        /// <summary>
        /// Typed fire-and-forget send to a filtered set of clients.
        /// Collects ConnectionIds and sends a single broadcast via SignalR.
        /// </summary>
        public static async Task SendAsync<T>(this IEnumerable<ClientContext> clients, Action<T> action, CancellationToken cancellationToken = default)
            where T : class {
            var clientList = clients.ToList();
            if (clientList.Count == 0) return;

            var connectionIds = clientList.Select(c => c.Id).ToList();
            var serviceProvider = clientList[0].ServiceProvider;
            var hubType = clientList[0].HARRRType;

            var hubContextType = typeof(IHubContext<>).MakeGenericType(hubType);
            var hubContext = serviceProvider.GetRequiredService(hubContextType);

            var clientsProperty = hubContext.GetType().GetProperty("Clients")!;
            var hubClients = clientsProperty.GetValue(hubContext)!;
            var clientsMethod = hubClients.GetType().GetMethod("Clients", new[] { typeof(IReadOnlyList<string>) })!;
            var clientProxy = (IClientProxy)clientsMethod.Invoke(hubClients, new object[] { connectionIds })!;

            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("SignalARRR.Broadcast");
            var helper = new BroadcastProxyCreatorHelper(clientProxy, logger);
            var proxy = ProxyCreator.CreateInstanceFromInterface<T>(helper);
            action(proxy);
        }

    }
}
