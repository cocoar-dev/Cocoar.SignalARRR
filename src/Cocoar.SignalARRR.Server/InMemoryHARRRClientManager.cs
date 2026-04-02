using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server {
    internal class InMemoryHARRRClientManager : IHARRRClientManager {
        private ConcurrentDictionary<string, ClientContext> ClientStore { get; } = new ConcurrentDictionary<string, ClientContext>();
        //private IServiceProvider ServiceProvider { get; }

        public InMemoryHARRRClientManager() {
            //ServiceProvider = serviceProvider;
        }

        public ClientContext Register(HARRR huc, HubCallerContext hubContext) {

            return ClientStore.AddOrUpdate(hubContext.ConnectionId, id => {

                return new ClientContext(huc, hubContext) {
                    ConnectedAt = DateTime.UtcNow
                };

            }, (s, cl) => {

                cl.ReconnectedAt.Add(DateTime.UtcNow);
                // Refresh transport-level credentials (cert/principal) from the new connection
                cl.RefreshTransportCredentials(hubContext);
                return cl;

            });

        }

        public ClientContext UnRegister(string connectionId) {
            return ClientStore.TryRemove(connectionId, out var client) ? client : null!;
        }

        public ClientContext GetClient(string connectionId) {
            return ClientStore.TryGetValue(connectionId, out var client) ? client : null!;
        }

        public IEnumerable<ClientContext> GetClients() {
            return ClientStore.Values;
        }
    }
}
