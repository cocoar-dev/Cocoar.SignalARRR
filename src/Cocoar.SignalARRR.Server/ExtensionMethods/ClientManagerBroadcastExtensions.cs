using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
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
            if (clients is IClusterClientQueryMetadata clusterQuery && clusterQuery.DistributedDispatchSupported) {
                var (methodName, capturedArguments) = CaptureCall<T>(action);

                // The route is frozen while the request scope is still alive: the cancellation
                // callback fires long after this method returned, when resolving services through
                // the query's (scoped, by then disposed) provider would throw. It captures only
                // singletons and plain values — and for the resolved-connections route the very
                // recipient set the call went to, so the cancellation reaches exactly the callees.
                var deliver = await BuildTargetDeliveryAsync(clusterQuery, cancellationToken);

                var arguments = BroadcastArgumentRules.PrepareCancellationTokens(
                    capturedArguments,
                    tokenId => deliver(
                        BroadcastArgumentRules.CancellationMessage(tokenId),
                        MethodNames.CancelTokenFromServer,
                        CancellationToken.None),
                    LoggerCache.For(clusterQuery.ServiceProvider, "SignalARRR.Broadcast"));

                var message = new ServerRequestMessage(methodName, arguments).WithTraceContext();
                await deliver(message, null, cancellationToken);
                return;
            }

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

            var logger = LoggerCache.For(serviceProvider, "SignalARRR.Broadcast");
            var helper = new BroadcastProxyCreatorHelper(clientProxy, logger);
            var proxy = ProxyCreator.CreateInstanceFromInterface<T>(helper);
            action(proxy);
        }

        /// <summary>
        /// Resolves the query's route once and returns a delivery function for it. The call and
        /// its cancellation both go through the returned function, so they cannot take different
        /// routes — and the function stays usable after the originating scope is gone, because it
        /// closes over singletons and plain values only.
        /// </summary>
        private static async Task<Func<ServerRequestMessage, string?, CancellationToken, Task>> BuildTargetDeliveryAsync(
            IClusterClientQueryMetadata clusterQuery,
            CancellationToken cancellationToken) {
            var backplane = clusterQuery.ServiceProvider.GetRequiredService<ISignalARRRBackplane>();
            var localDispatcher = clusterQuery.ServiceProvider.GetRequiredService<LocalSignalARRRBackplaneDispatcher>();
            var hubType = clusterQuery.HubType;
            var targetKind = clusterQuery.TargetKind;
            var groupName = clusterQuery.GroupName;
            var userId = clusterQuery.UserId;

            if (backplane.IsEnabled && clusterQuery.CanUseDirectBackplaneDispatch) {
                return async (message, signalRMethodName, ct) => {
                    await localDispatcher.DispatchAsync(
                        hubType,
                        targetKind,
                        message,
                        groupName: groupName,
                        userId: userId,
                        signalRMethodName: signalRMethodName,
                        cancellationToken: ct);

                    await backplane.PublishDispatchAsync(
                        hubType,
                        targetKind,
                        message,
                        groupName: groupName,
                        userId: userId,
                        signalRMethodName: signalRMethodName,
                        cancellationToken: ct);
                };
            }

            if (backplane.IsEnabled) {
                var registrations = await DistributedClientQueryResolver.ResolveConnectionsAsync(clusterQuery, cancellationToken);
                var localConnectionIds = registrations
                    .Where(r => string.Equals(r.NodeId, backplane.NodeId, StringComparison.Ordinal))
                    .Select(r => r.ConnectionId)
                    .ToArray();
                var remoteNodeGroups = registrations
                    .Where(r => !string.Equals(r.NodeId, backplane.NodeId, StringComparison.Ordinal))
                    .GroupBy(r => r.NodeId, StringComparer.Ordinal)
                    .Select(g => g.Select(r => r.ConnectionId).ToArray())
                    .ToArray();

                return async (message, signalRMethodName, ct) => {
                    if (localConnectionIds.Length > 0) {
                        await localDispatcher.DispatchAsync(
                            hubType,
                            SignalARRRBackplaneTargetKind.Connections,
                            message,
                            localConnectionIds,
                            signalRMethodName: signalRMethodName,
                            cancellationToken: ct);
                    }

                    foreach (var nodeConnectionIds in remoteNodeGroups) {
                        await backplane.PublishDispatchAsync(
                            hubType,
                            SignalARRRBackplaneTargetKind.Connections,
                            message,
                            nodeConnectionIds,
                            signalRMethodName: signalRMethodName,
                            cancellationToken: ct);
                    }
                };
            }

            return (message, signalRMethodName, ct) => localDispatcher.DispatchAsync(
                hubType,
                targetKind,
                message,
                groupName: groupName,
                userId: userId,
                signalRMethodName: signalRMethodName,
                cancellationToken: ct);
        }

        private static (string methodName, object[] arguments) CaptureCall<TInterface>(Delegate action) where TInterface : class {
            var capturer = new CapturingProxyCreatorHelper();
            var proxy = ProxyCreator.CreateInstanceFromInterface<TInterface>(capturer);
            action.DynamicInvoke(proxy);

            if (capturer.CapturedMethodName == null)
                throw new InvalidOperationException("No method call was captured from the action.");

            var arguments = capturer.CapturedArguments ?? Array.Empty<object>();
            if (arguments.Any(a => a is System.IO.Stream)) {
                throw new NotSupportedException(
                    $"Method '{capturer.CapturedMethodName}' has a Stream argument. Stream arguments are not supported for multi-client operations. Use single-client GetTypedMethods<T>() instead.");
            }

            return (capturer.CapturedMethodName, arguments);
        }

    }
}
