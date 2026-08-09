using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    public class ClientManager {

        private IHARRRClientManager HARRRClientManager { get; }
        internal IServiceProvider ServiceProvider { get; }
        private ISignalARRRBackplane Backplane => ServiceProvider.GetRequiredService<ISignalARRRBackplane>();
        private ISignalARRRConnectionRegistry ConnectionRegistry => ServiceProvider.GetRequiredService<ISignalARRRConnectionRegistry>();
        private LocalSignalARRRBackplaneDispatcher LocalDispatcher => ServiceProvider.GetRequiredService<LocalSignalARRRBackplaneDispatcher>();

        internal ClientManager(IHARRRClientManager harrrClientManager, IServiceProvider serviceProvider) {
            HARRRClientManager = harrrClientManager;
            ServiceProvider = serviceProvider;
        }

        /// <summary>
        /// Primary entry point — select the hub first, then chain filters and call.
        /// </summary>
        /// <remarks>
        /// The result describes a target set rather than listing one: with a backplane enabled the
        /// filters and the send/invoke methods resolve across the whole cluster. Use
        /// <see cref="IClientQuery.LocalClients"/> when you need the <see cref="ClientContext"/>
        /// objects themselves — those exist only for connections this node owns.
        /// </remarks>
        public IClientQuery WithHub<THub>() where THub : HARRR {
            return new ClusterClientQuery(
                HARRRClientManager.GetClients().Where(c => c.HARRRType == typeof(THub)),
                typeof(THub),
                ServiceProvider);
        }

        /// <summary>
        /// Get a single client by connection ID.
        /// </summary>
        public ClientContext GetClientById(string id) {
            return HARRRClientManager.GetClient(id);
        }

        internal T CreateTypedMethodsProxy<T>(string connectionId) where T : class {
            var ctx = GetClientById(connectionId);
            if (ctx != null) {
                return ctx.GetTypedMethods<T>();
            }

            if (!Backplane.IsEnabled) {
                throw new InvalidOperationException($"Client not found: {connectionId}");
            }

            var helper = new BackplaneClientProxyCreatorHelper(null, connectionId, Backplane);
            return ProxyCreator.CreateInstanceFromInterface<T>(helper);
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

        public Task<IReadOnlyList<SignalARRRConnectionSnapshot>> GetConnectionsAsync<THub>(CancellationToken cancellationToken = default)
            where THub : HARRR {
            return GetConnectionSnapshotsAsync(typeof(THub), cancellationToken: cancellationToken);
        }

        public Task<IReadOnlyList<SignalARRRConnectionSnapshot>> GetConnectionsByUserAsync<THub>(string userId, CancellationToken cancellationToken = default)
            where THub : HARRR {
            return GetConnectionSnapshotsAsync(typeof(THub), userId: userId, cancellationToken: cancellationToken);
        }

        public Task<IReadOnlyList<SignalARRRConnectionSnapshot>> GetConnectionsInGroupAsync<THub>(string groupName, CancellationToken cancellationToken = default)
            where THub : HARRR {
            return GetConnectionSnapshotsAsync(typeof(THub), groupName: groupName, cancellationToken: cancellationToken);
        }

        public Task<IReadOnlyList<SignalARRRConnectionSnapshot>> GetConnectionsByAttributeAsync<THub>(string key, string? value = null, CancellationToken cancellationToken = default)
            where THub : HARRR {
            return GetConnectionSnapshotsAsync(
                typeof(THub),
                attributeFilters: new[] {
                    new SignalARRRConnectionAttributeFilter {
                        Key = key,
                        Value = value
                    }
                },
                cancellationToken: cancellationToken);
        }

        public async Task<IReadOnlyList<SignalARRRUserPresenceSnapshot>> GetOnlineUsersAsync<THub>(CancellationToken cancellationToken = default)
            where THub : HARRR {
            var snapshots = await GetConnectionSnapshotsAsync(typeof(THub), cancellationToken: cancellationToken);
            return snapshots
                .Where(s => !string.IsNullOrWhiteSpace(s.UserId))
                .GroupBy(s => s.UserId!, StringComparer.Ordinal)
                .Select(g => new SignalARRRUserPresenceSnapshot {
                    UserId = g.Key,
                    ConnectionIds = g.Select(s => s.ConnectionId).OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    NodeIds = g.Select(s => s.NodeId).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray()
                })
                .OrderBy(s => s.UserId, StringComparer.Ordinal)
                .ToArray();
        }

        public async Task<bool> IsUserOnlineAsync<THub>(string userId, CancellationToken cancellationToken = default)
            where THub : HARRR {
            var snapshots = await GetConnectionSnapshotsAsync(typeof(THub), userId: userId, cancellationToken: cancellationToken);
            return snapshots.Count > 0;
        }

        /// <summary>
        /// Adds a client to a SignalR group AND tracks it in ClientContext.Groups.
        /// </summary>
        public async Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            var client = GetClientById(connectionId);
            if (client != null) {
                await LocalDispatcher.ApplyGroupCommandAsync(client.HARRRType, connectionId, groupName, SignalARRRBackplaneGroupAction.Add, cancellationToken);
                await ConnectionRegistry.AddConnectionToGroupAsync(connectionId, groupName, cancellationToken);
                return;
            }

            if (!Backplane.IsEnabled) {
                throw new InvalidOperationException($"Client not found: {connectionId}");
            }

            await Backplane.PublishGroupCommandAsync(null, connectionId, groupName, SignalARRRBackplaneGroupAction.Add, cancellationToken);
        }

        /// <summary>
        /// Removes a client from a SignalR group AND removes it from ClientContext.Groups.
        /// </summary>
        public async Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) {
            var client = GetClientById(connectionId);
            if (client != null) {
                await LocalDispatcher.ApplyGroupCommandAsync(client.HARRRType, connectionId, groupName, SignalARRRBackplaneGroupAction.Remove, cancellationToken);
                await ConnectionRegistry.RemoveConnectionFromGroupAsync(connectionId, groupName, cancellationToken);
                return;
            }

            if (!Backplane.IsEnabled) {
                throw new InvalidOperationException($"Client not found: {connectionId}");
            }

            await Backplane.PublishGroupCommandAsync(null, connectionId, groupName, SignalARRRBackplaneGroupAction.Remove, cancellationToken);
        }

        private async Task<IReadOnlyList<SignalARRRConnectionSnapshot>> GetConnectionSnapshotsAsync(
            Type hubType,
            string? groupName = null,
            string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null,
            CancellationToken cancellationToken = default) {
            if (ConnectionRegistry.IsEnabled) {
                var registrations = await ConnectionRegistry.FindConnectionsAsync(
                    hubType,
                    groupName,
                    userId,
                    attributeFilters,
                    cancellationToken);

                return registrations
                    .Select(MapConnectionSnapshot)
                    .OrderBy(s => s.ConnectionId, StringComparer.Ordinal)
                    .ToArray();
            }

            var clients = HARRRClientManager.GetClients().Where(c => c.HARRRType == hubType);
            if (!string.IsNullOrWhiteSpace(groupName)) {
                clients = clients.Where(c => c.Groups.Contains(groupName));
            }

            if (!string.IsNullOrWhiteSpace(userId)) {
                clients = clients.Where(c => string.Equals(c.UserIdentifier, userId, StringComparison.Ordinal));
            }

            if (attributeFilters != null) {
                foreach (var filter in attributeFilters) {
                    clients = string.IsNullOrWhiteSpace(filter.Value)
                        ? clients.Where(c => c.Attributes.Has(filter.Key))
                        : clients.Where(c => c.Attributes.Has(filter.Key, filter.Value));
                }
            }

            return clients
                .Select(MapConnectionSnapshot)
                .OrderBy(s => s.ConnectionId, StringComparer.Ordinal)
                .ToArray();
        }

        private SignalARRRConnectionSnapshot MapConnectionSnapshot(ClientContext clientContext) {
            return new SignalARRRConnectionSnapshot {
                ConnectionId = clientContext.Id,
                NodeId = Backplane.NodeId,
                UserId = clientContext.UserIdentifier,
                Groups = clientContext.Groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToArray(),
                Attributes = clientContext.Attributes
                    .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        a => a.Key,
                        a => (IReadOnlyList<string>)a.Value.Where(v => v != null).Select(v => v!).ToArray(),
                        StringComparer.OrdinalIgnoreCase)
            };
        }

        private static SignalARRRConnectionSnapshot MapConnectionSnapshot(SignalARRRConnectionRegistration registration) {
            return new SignalARRRConnectionSnapshot {
                ConnectionId = registration.ConnectionId,
                NodeId = registration.NodeId,
                UserId = registration.UserId,
                Groups = registration.Groups.OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ToArray(),
                Attributes = registration.Attributes
                    .OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        a => a.Key,
                        a => (IReadOnlyList<string>)a.Values.OrderBy(v => v, StringComparer.Ordinal).ToArray(),
                        StringComparer.OrdinalIgnoreCase)
            };
        }
    }
}
