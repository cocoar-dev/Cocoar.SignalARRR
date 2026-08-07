using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    internal sealed class LocalSignalARRRBackplaneDispatcher {
        private static readonly MethodInfo InvokeCoreAsyncMethod = typeof(ISingleClientProxy)
            .GetMethods()
            .Single(m => m.Name == nameof(ISingleClientProxy.InvokeCoreAsync) && m.IsGenericMethodDefinition);

        private readonly IServiceProvider _serviceProvider;
        private readonly IHARRRClientManager _clientManager;
        private readonly ILogger<LocalSignalARRRBackplaneDispatcher> _logger;

        public LocalSignalARRRBackplaneDispatcher(IServiceProvider serviceProvider, IHARRRClientManager clientManager, ILogger<LocalSignalARRRBackplaneDispatcher> logger) {
            _serviceProvider = serviceProvider;
            _clientManager = clientManager;
            _logger = logger;
        }

        /// <summary>
        /// The connections this node currently serves.
        /// </summary>
        /// <remarks>
        /// Used by the backplane to re-assert its registrations after it was evicted from the
        /// distributed registry.
        /// </remarks>
        public IEnumerable<ClientContext> GetLocalConnections() => _clientManager.GetClients();

        public bool HasLocalConnection(string connectionId, Type? hubType) {
            var client = _clientManager.GetClient(connectionId);
            return client != null && (hubType == null || client.HARRRType == hubType);
        }

        /// <summary>
        /// Sends <paramref name="message"/> to the targeted local connections.
        /// </summary>
        /// <remarks>
        /// <paramref name="signalRMethodName"/> names the client-side handler; the default is the
        /// ordinary call path. Broadcast cancellation (N-4) sends the same message shape under
        /// <c>CancelTokenFromServer</c> to the same target set — which is only possible because
        /// this is no longer hard-coded.
        /// </remarks>
        public async Task DispatchAsync(
            Type? hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            IReadOnlyList<string>? connectionIds = null,
            string? groupName = null,
            string? userId = null,
            string? signalRMethodName = null,
            CancellationToken cancellationToken = default) {
            hubType = ResolveHubType(hubType, connectionIds);
            if (hubType == null) {
                return;
            }

            var clientProxy = GetClientProxy(hubType, targetKind, connectionIds, groupName, userId);
            if (clientProxy == null) {
                return;
            }

            await clientProxy.SendCoreAsync(signalRMethodName ?? MethodNames.InvokeServerMessage, new object[] { message }, cancellationToken);
        }

        public async Task<(bool handled, object? result)> InvokeConnectionAsync(
            Type? hubType,
            string connectionId,
            ServerRequestMessage message,
            Type resultType,
            CancellationToken cancellationToken = default) {
            var client = _clientManager.GetClient(connectionId);
            if (client == null || (hubType != null && client.HARRRType != hubType)) {
                return (false, null);
            }

            hubType = client.HARRRType;
            var singleClientProxy = GetSingleClientProxy(hubType, connectionId);
            if (singleClientProxy == null) {
                return (false, null);
            }

            var task = (Task)InvokeCoreAsyncMethod
                .MakeGenericMethod(resultType)
                .Invoke(singleClientProxy, new object[] { MethodNames.InvokeServerRequest, new object[] { message }, cancellationToken })!;

            await task.ConfigureAwait(false);

            return (true, task.GetType().GetProperty("Result")?.GetValue(task));
        }

        public async Task<IReadOnlyList<SignalARRRBackplaneInvokeResult>> InvokeAsync(
            Type hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            Type resultType,
            IReadOnlyList<string>? connectionIds = null,
            string? groupName = null,
            string? userId = null,
            CancellationToken cancellationToken = default) {
            var matchingClients = GetMatchingClients(hubType, targetKind, connectionIds, groupName, userId).ToList();
            if (matchingClients.Count == 0) {
                return Array.Empty<SignalARRRBackplaneInvokeResult>();
            }

            var tasks = matchingClients.Select(client => InvokeConnectionAsync(
                hubType,
                client.Id,
                message,
                resultType,
                cancellationToken));

            var invokeResults = await Task.WhenAll(tasks).ConfigureAwait(false);
            var results = new List<SignalARRRBackplaneInvokeResult>(invokeResults.Length);
            for (int i = 0; i < invokeResults.Length; i++) {
                if (!invokeResults[i].handled) {
                    continue;
                }

                results.Add(new SignalARRRBackplaneInvokeResult {
                    ConnectionId = matchingClients[i].Id,
                    Value = invokeResults[i].result
                });
            }

            return results;
        }

        public async Task<SignalARRRBackplaneInvokeResult?> InvokeOneAsync(
            Type hubType,
            SignalARRRBackplaneTargetKind targetKind,
            ServerRequestMessage message,
            Type resultType,
            IReadOnlyList<string>? connectionIds = null,
            string? groupName = null,
            string? userId = null,
            CancellationToken cancellationToken = default) {
            foreach (var client in GetMatchingClients(hubType, targetKind, connectionIds, groupName, userId)) {
                try {
                    var (handled, result) = await InvokeConnectionAsync(
                        hubType,
                        client.Id,
                        message,
                        resultType,
                        cancellationToken).ConfigureAwait(false);

                    if (handled) {
                        return new SignalARRRBackplaneInvokeResult {
                            ConnectionId = client.Id,
                            Value = result
                        };
                    }
                } catch (Exception ex) {
                    // Try the next candidate, but do not destroy the reason this one failed --
                    // authorization denied, deserialization failure and client timeout are very
                    // different problems and used to be indistinguishable from "nobody answered".
                    _logger.LogWarning(ex, "Invoking '{Method}' on local connection {ConnectionId} failed; trying the next candidate.",
                        message.Method, client.Id);
                }
            }

            return null;
        }

        public async Task ApplyGroupCommandAsync(
            Type? hubType,
            string connectionId,
            string groupName,
            SignalARRRBackplaneGroupAction action,
            CancellationToken cancellationToken = default) {
            var client = _clientManager.GetClient(connectionId);
            if (client == null || (hubType != null && client.HARRRType != hubType)) {
                return;
            }

            var groupManager = GetGroupManager(client.HARRRType);
            if (action == SignalARRRBackplaneGroupAction.Add) {
                await groupManager.AddToGroupAsync(connectionId, groupName, cancellationToken);
                client.AddGroup(groupName);
            } else {
                await groupManager.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
                client.RemoveGroup(groupName);
            }
        }

        private IClientProxy? GetClientProxy(
            Type hubType,
            SignalARRRBackplaneTargetKind targetKind,
            IReadOnlyList<string>? connectionIds,
            string? groupName,
            string? userId) {
            var hubClients = GetHubClients(hubType);

            return targetKind switch {
                SignalARRRBackplaneTargetKind.All => hubClients.GetType().GetProperty("All")?.GetValue(hubClients) as IClientProxy,
                SignalARRRBackplaneTargetKind.Group => string.IsNullOrWhiteSpace(groupName)
                    ? null
                    : GetGroupClientProxy(hubClients, groupName),
                SignalARRRBackplaneTargetKind.User => string.IsNullOrWhiteSpace(userId)
                    ? null
                    : GetUserClientProxy(hubClients, userId),
                SignalARRRBackplaneTargetKind.Connections => connectionIds == null || connectionIds.Count == 0
                    ? null
                    : GetConnectionTargetProxy(hubType, connectionIds),
                _ => null
            };
        }

        private ISingleClientProxy? GetSingleClientProxy(Type hubType, string connectionId) {
            var hubClients = GetHubClients(hubType);
            return hubClients.GetType()
                .GetMethod("Client", new[] { typeof(string) })?
                .Invoke(hubClients, new object[] { connectionId }) as ISingleClientProxy;
        }

        private IClientProxy? GetGroupClientProxy(object hubClients, string groupName) {
            return hubClients.GetType()
                .GetMethod("Group", new[] { typeof(string) })?
                .Invoke(hubClients, new object[] { groupName }) as IClientProxy;
        }

        private IClientProxy? GetUserClientProxy(object hubClients, string userId) {
            return hubClients.GetType()
                .GetMethod("User", new[] { typeof(string) })?
                .Invoke(hubClients, new object[] { userId }) as IClientProxy;
        }

        private IClientProxy? GetConnectionTargetProxy(Type hubType, IReadOnlyList<string> connectionIds) {
            if (connectionIds.Count == 1) {
                return GetSingleClientProxy(hubType, connectionIds[0]);
            }

            var hubClients = GetHubClients(hubType);
            var clientsMethod = hubClients.GetType()
                .GetMethods()
                .FirstOrDefault(m => m.Name == "Clients" && m.GetParameters().Length == 1);

            return clientsMethod?.Invoke(hubClients, new object[] { connectionIds }) as IClientProxy;
        }

        private object GetHubClients(Type hubType) {
            var hubContextType = typeof(IHubContext<>).MakeGenericType(hubType);
            var hubContext = _serviceProvider.GetRequiredService(hubContextType);
            return hubContextType.GetProperty("Clients")!.GetValue(hubContext)!;
        }

        private IGroupManager GetGroupManager(Type hubType) {
            var hubContextType = typeof(IHubContext<>).MakeGenericType(hubType);
            var hubContext = _serviceProvider.GetRequiredService(hubContextType);
            return (IGroupManager)hubContextType.GetProperty("Groups")!.GetValue(hubContext)!;
        }

        private Type? ResolveHubType(Type? explicitHubType, IReadOnlyList<string>? connectionIds) {
            if (explicitHubType != null) {
                return explicitHubType;
            }

            if (connectionIds == null) {
                return null;
            }

            foreach (var connectionId in connectionIds) {
                var client = _clientManager.GetClient(connectionId);
                if (client != null) {
                    return client.HARRRType;
                }
            }

            return null;
        }

        private IEnumerable<ClientContext> GetMatchingClients(
            Type hubType,
            SignalARRRBackplaneTargetKind targetKind,
            IReadOnlyList<string>? connectionIds,
            string? groupName,
            string? userId) {
            var clients = _clientManager.GetClients().Where(c => c.HARRRType == hubType);

            return targetKind switch {
                SignalARRRBackplaneTargetKind.All => clients,
                SignalARRRBackplaneTargetKind.Group => string.IsNullOrWhiteSpace(groupName)
                    ? Enumerable.Empty<ClientContext>()
                    : clients.Where(c => c.Groups.Contains(groupName)),
                SignalARRRBackplaneTargetKind.User => string.IsNullOrWhiteSpace(userId)
                    ? Enumerable.Empty<ClientContext>()
                    : clients.Where(c => string.Equals(c.UserIdentifier, userId, StringComparison.Ordinal)),
                SignalARRRBackplaneTargetKind.Connections => connectionIds == null
                    ? Enumerable.Empty<ClientContext>()
                    : clients.Where(c => connectionIds.Contains(c.Id)),
                _ => Enumerable.Empty<ClientContext>()
            };
        }
    }
}
