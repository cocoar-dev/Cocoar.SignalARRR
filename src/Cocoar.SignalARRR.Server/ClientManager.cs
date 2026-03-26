using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    public class ClientManager {

        private IHARRRClientManager HARRRClientManager { get; }
        internal IServiceProvider ServiceProvider { get; }

        internal ClientManager(IHARRRClientManager harrrClientManager, IServiceProvider serviceProvider) {
            HARRRClientManager = harrrClientManager;
            ServiceProvider = serviceProvider;
        }

        /// <summary>
        /// Primary entry point — select the hub first, then chain filters.
        /// Returns all clients connected to the specified hub type.
        /// </summary>
        public IEnumerable<ClientContext> WithHub<THub>() where THub : HARRR {
            return HARRRClientManager.GetClients().Where(c => c.HARRRType == typeof(THub));
        }

        /// <summary>
        /// Get a single client by connection ID.
        /// </summary>
        public ClientContext GetClientById(string id) {
            return HARRRClientManager.GetClient(id);
        }

        /// <summary>
        /// Get all connected clients (across all hubs).
        /// Prefer WithHub&lt;T&gt;() to scope to a specific hub.
        /// </summary>
        public IEnumerable<ClientContext> GetAllClients() {
            return HARRRClientManager.GetClients();
        }

        /// <summary>
        /// Get all connected clients matching a predicate.
        /// </summary>
        public IEnumerable<ClientContext> GetAllClients(Func<ClientContext, bool> predicate) {
            return GetAllClients().Where(predicate);
        }

        [Obsolete("Use WithHub<T>() instead. Will be removed in v5.0.")]
        public IEnumerable<ClientContext> GetHARRRClients<T>() {
            return HARRRClientManager.GetClients().Where(c => c.HARRRType == typeof(T));
        }

        [Obsolete("Use WithHub<T>().Where(predicate) instead. Will be removed in v5.0.")]
        public IEnumerable<ClientContext> GetHARRRClients<T>(Func<ClientContext, bool> predicate) {
            return GetHARRRClients<T>().Where(predicate);
        }

        /// <summary>
        /// Adds a client to a SignalR group AND tracks it in ClientContext.Groups.
        /// </summary>
        public async Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            var client = GetClientById(connectionId);
            var hubContextType = typeof(IHubContext<>).MakeGenericType(client.HARRRType);
            var hubContext = ServiceProvider.GetRequiredService(hubContextType);
            var groupsProperty = hubContextType.GetProperty("Groups")!;
            var groupManager = (IGroupManager)groupsProperty.GetValue(hubContext)!;
            await groupManager.AddToGroupAsync(connectionId, groupName, cancellationToken);
            client.AddGroup(groupName);
        }

        /// <summary>
        /// Removes a client from a SignalR group AND removes it from ClientContext.Groups.
        /// </summary>
        public async Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            var client = GetClientById(connectionId);
            var hubContextType = typeof(IHubContext<>).MakeGenericType(client.HARRRType);
            var hubContext = ServiceProvider.GetRequiredService(hubContextType);
            var groupsProperty = hubContextType.GetProperty("Groups")!;
            var groupManager = (IGroupManager)groupsProperty.GetValue(hubContext)!;
            await groupManager.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
            client.RemoveGroup(groupName);
        }
    }
}
