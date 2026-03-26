using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cocoar.SignalARRR.Client.FullFramework {
    internal static class HubConnectionExtensions {

        public static IServiceProvider GetServiceProvider(this HubConnection hubConnection) {
            var serviceProvider = (IServiceProvider)hubConnection.GetType()
                .GetField("_serviceProvider", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(hubConnection);
            return serviceProvider;
        }
    }
}
