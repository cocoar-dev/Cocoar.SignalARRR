using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cocoar.SignalARRR.Client.ExtensionMethods {
    public static class HubConnectionExtensions {

        public static IServiceProvider GetServiceProvider(this HubConnection hubConnection) {

            var serviceProvider = (IServiceProvider)hubConnection.GetType().GetField("_serviceProvider", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hubConnection)!;
            return serviceProvider;

        }

        public static Func<Task<string>>? GetAccessTokenProvider(this HubConnection hubConnection) {

            var connectionFactory = hubConnection.GetType().GetField("_connectionFactory", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(hubConnection);

            var httpConnectionOption = connectionFactory?.GetType().GetField("_httpConnectionOptions", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(connectionFactory) as HttpConnectionOptions;

            return httpConnectionOption?.AccessTokenProvider!;
        }
    }
}
