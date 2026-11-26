using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    internal sealed class LocalSignalARRRBackplaneDispatcher {
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

            var raw = await InvokeRawAsync(singleClientProxy, message, cancellationToken).ConfigureAwait(false);
            return (true, ConvertResult(raw, resultType));
        }

        /// <summary>
        /// Invokes a client and returns its answer as the hub protocol delivered it (a
        /// <c>JsonElement</c> for JSON clients), for a node that forwards it without knowing its type.
        /// </summary>
        public async Task<(bool handled, object? result)> InvokeConnectionRawAsync(
            Type? hubType,
            string connectionId,
            ServerRequestMessage message,
            CancellationToken cancellationToken = default) {
            var client = _clientManager.GetClient(connectionId);
            if (client == null || (hubType != null && client.HARRRType != hubType)) {
                return (false, null);
            }

            var singleClientProxy = GetSingleClientProxy(client.HARRRType, connectionId);
            if (singleClientProxy == null) {
                return (false, null);
            }

            return (true, await InvokeRawAsync(singleClientProxy, message, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>
        /// Asks for the answer as <see cref="object"/> rather than closing <c>InvokeCoreAsync&lt;T&gt;</c>
        /// over the result type at runtime, which NativeAOT cannot do; the conversion happens after.
        /// </summary>
        private static Task<object?> InvokeRawAsync(ISingleClientProxy proxy, ServerRequestMessage message, CancellationToken cancellationToken) =>
            proxy.InvokeCoreAsync<object?>(MethodNames.InvokeServerRequest, new object[] { message }, cancellationToken);

        private object? ConvertResult(object? raw, Type resultType) =>
            _serviceProvider.GetRequiredService<Common.Serialization.IProtocolSerializer>().ConvertTo(raw, resultType);

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
            var hubClients = ServerDispatchRegistry.Hub(hubType).Clients(_serviceProvider);

            return targetKind switch {
                SignalARRRBackplaneTargetKind.All => hubClients.All,
                SignalARRRBackplaneTargetKind.Group => string.IsNullOrWhiteSpace(groupName) ? null : hubClients.Group(groupName),
                SignalARRRBackplaneTargetKind.User => string.IsNullOrWhiteSpace(userId) ? null : hubClients.User(userId),
                SignalARRRBackplaneTargetKind.Connections => connectionIds == null || connectionIds.Count == 0
                    ? null
                    : connectionIds.Count == 1 ? hubClients.Client(connectionIds[0]) : hubClients.Clients(connectionIds),
                _ => null
            };
        }

        private ISingleClientProxy GetSingleClientProxy(Type hubType, string connectionId) =>
            ServerDispatchRegistry.Hub(hubType).Clients(_serviceProvider).Client(connectionId);

        private IGroupManager GetGroupManager(Type hubType) => ServerDispatchRegistry.Hub(hubType).Groups(_serviceProvider);

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
