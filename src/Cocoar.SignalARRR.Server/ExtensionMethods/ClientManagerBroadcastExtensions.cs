using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
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
                var (methodName, arguments) = CaptureCall<T>(action);
                var message = new ServerRequestMessage(methodName, arguments).WithTraceContext();
                var backplane = clusterQuery.ServiceProvider.GetRequiredService<ISignalARRRBackplane>();
                var localDispatcher = clusterQuery.ServiceProvider.GetRequiredService<LocalSignalARRRBackplaneDispatcher>();

                if (backplane.IsEnabled && clusterQuery.CanUseDirectBackplaneDispatch) {
                    await localDispatcher.DispatchAsync(
                        clusterQuery.HubType,
                        clusterQuery.TargetKind,
                        message,
                        groupName: clusterQuery.GroupName,
                        userId: clusterQuery.UserId,
                        cancellationToken: cancellationToken);

                    await backplane.PublishDispatchAsync(
                        clusterQuery.HubType,
                        clusterQuery.TargetKind,
                        message,
                        groupName: clusterQuery.GroupName,
                        userId: clusterQuery.UserId,
                        cancellationToken: cancellationToken);

                    return;
                }

                if (backplane.IsEnabled) {
                    var registrations = await DistributedClientQueryResolver.ResolveConnectionsAsync(clusterQuery, cancellationToken);
                    var localConnectionIds = registrations
                        .Where(r => string.Equals(r.NodeId, backplane.NodeId, StringComparison.Ordinal))
                        .Select(r => r.ConnectionId)
                        .ToArray();

                    if (localConnectionIds.Length > 0) {
                        await localDispatcher.DispatchAsync(
                            clusterQuery.HubType,
                            SignalARRRBackplaneTargetKind.Connections,
                            message,
                            localConnectionIds,
                            cancellationToken: cancellationToken);
                    }

                    foreach (var nodeGroup in registrations
                        .Where(r => !string.Equals(r.NodeId, backplane.NodeId, StringComparison.Ordinal))
                        .GroupBy(r => r.NodeId, StringComparer.Ordinal)) {
                        await backplane.PublishDispatchAsync(
                            clusterQuery.HubType,
                            SignalARRRBackplaneTargetKind.Connections,
                            message,
                            nodeGroup.Select(r => r.ConnectionId).ToArray(),
                            cancellationToken: cancellationToken);
                    }

                    return;
                }

                await localDispatcher.DispatchAsync(
                    clusterQuery.HubType,
                    clusterQuery.TargetKind,
                    message,
                    groupName: clusterQuery.GroupName,
                    userId: clusterQuery.UserId,
                    cancellationToken: cancellationToken);
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

        private static (string methodName, object[] arguments) CaptureCall<TInterface>(Delegate action) where TInterface : class {
            var capturer = new CapturingProxyCreatorHelper();
            var proxy = ProxyCreator.CreateInstanceFromInterface<TInterface>(capturer);
            action.DynamicInvoke(proxy);

            if (capturer.CapturedMethodName == null)
                throw new InvalidOperationException("No method call was captured from the action.");

            // Same rule as the proxy-helper path takes, so which one runs stays an internal detail.
            BroadcastArgumentRules.RejectCancellationTokens(
                capturer.CapturedMethodName, capturer.CapturedArguments ?? Array.Empty<object>());

            var arguments = capturer.CapturedArguments ?? Array.Empty<object>();
            if (arguments.Any(a => a is System.IO.Stream)) {
                throw new NotSupportedException(
                    $"Method '{capturer.CapturedMethodName}' has a Stream argument. Stream arguments are not supported for multi-client operations. Use single-client GetTypedMethods<T>() instead.");
            }

            return (capturer.CapturedMethodName, arguments);
        }

    }
}
