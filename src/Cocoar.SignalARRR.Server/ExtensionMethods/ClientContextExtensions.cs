using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    public static class ClientContextExtensions {

        public static async Task<ClientCollectionResult<TResult>> Invoke<TResult>(this ClientContext clientContext, string method, object[] arguments, CancellationToken cancellationToken) {

            using var serviceProviderScope = clientContext.ServiceProvider.CreateScope();

            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(clientContext.HARRRType);
            var harrrContext = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            var msg = new ServerRequestMessage(method, arguments);
            var res = await harrrContext.InvokeClientAsync<TResult>(clientContext.Id, msg, cancellationToken);
            return new ClientCollectionResult<TResult>(clientContext.Id, res);

        }

        public static async Task CancelToken(this ClientContext clientContext, Guid tokenReference) {

            using var serviceProviderScope = clientContext.ServiceProvider.CreateScope();

            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(clientContext.HARRRType);
            var harrrContext = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            var msg = new ServerRequestMessage(MethodNames.CancelTokenFromServer, tokenReference);

            await harrrContext.CancelToken(clientContext.Id, tokenReference);
        }

        public static async Task<IEnumerable<ClientCollectionResult<TResult>>> InvokeAllAsync<TResult>(this IEnumerable<ClientContext> clientContext, string method, object[] arguments, CancellationToken cancellationToken) {
            if (clientContext is IClusterClientQueryMetadata clusterQuery && clusterQuery.DistributedDispatchSupported) {
                var backplane = clusterQuery.ServiceProvider.GetRequiredService<ISignalARRRBackplane>();
                if (backplane.IsEnabled) {
                    var message = new ServerRequestMessage(method, arguments);
                    IReadOnlyList<SignalARRRBackplaneInvokeResult> results;
                    if (clusterQuery.CanUseDirectBackplaneDispatch) {
                        results = await backplane.InvokeQueryAsync(
                            clusterQuery.HubType,
                            clusterQuery.TargetKind,
                            message,
                            typeof(TResult),
                            groupName: clusterQuery.GroupName,
                            userId: clusterQuery.UserId,
                            cancellationToken: cancellationToken);
                    } else {
                        var localDispatcher = clusterQuery.ServiceProvider.GetRequiredService<LocalSignalARRRBackplaneDispatcher>();
                        var registrations = await DistributedClientQueryResolver.ResolveConnectionsAsync(clusterQuery, cancellationToken);
                        var valuesByConnectionId = new Dictionary<string, object?>(StringComparer.Ordinal);

                        var localConnectionIds = registrations
                            .Where(r => string.Equals(r.NodeId, backplane.NodeId, StringComparison.Ordinal))
                            .Select(r => r.ConnectionId)
                            .ToArray();
                        if (localConnectionIds.Length > 0) {
                            var localResults = await localDispatcher.InvokeAsync(
                                clusterQuery.HubType,
                                SignalARRRBackplaneTargetKind.Connections,
                                message,
                                typeof(TResult),
                                localConnectionIds,
                                cancellationToken: cancellationToken);

                            foreach (var localResult in localResults) {
                                valuesByConnectionId[localResult.ConnectionId] = localResult.Value;
                            }
                        }

                        var remoteTasks = registrations
                            .Where(r => !string.Equals(r.NodeId, backplane.NodeId, StringComparison.Ordinal))
                            .Select(async r => new SignalARRRBackplaneInvokeResult {
                                ConnectionId = r.ConnectionId,
                                Value = await backplane.InvokeConnectionAsync(clusterQuery.HubType, r.ConnectionId, message, typeof(TResult), cancellationToken)
                            });

                        foreach (var remoteResult in await Task.WhenAll(remoteTasks)) {
                            valuesByConnectionId[remoteResult.ConnectionId] = remoteResult.Value;
                        }

                        results = registrations
                            .Where(r => valuesByConnectionId.ContainsKey(r.ConnectionId))
                            .Select(r => new SignalARRRBackplaneInvokeResult {
                                ConnectionId = r.ConnectionId,
                                Value = valuesByConnectionId[r.ConnectionId]
                            })
                            .ToArray();
                    }

                    return results.Select(r => new ClientCollectionResult<TResult>(r.ConnectionId, (TResult)r.Value!));
                }
            }

            var tasks = new List<Task<ClientCollectionResult<TResult>>>();

            foreach (var context in clientContext) {
                tasks.Add(context.Invoke<TResult>(method, arguments, cancellationToken));
            }

            var result = await Task.WhenAll(tasks);

            return result;
        }

        public static async Task<ClientCollectionResult<TResult>> InvokeOneAsync<TResult>(this IEnumerable<ClientContext> clientContext, string method, object[] arguments, CancellationToken cancellationToken) {
            // Every candidate that fails contributes its exception here. Without them the caller only
            // ever saw "No client responded", which says nothing about *why* -- authorization denied,
            // deserialization failure and client timeout all looked identical.
            var failures = new List<Exception>();

            if (clientContext is IClusterClientQueryMetadata clusterQuery && clusterQuery.DistributedDispatchSupported) {
                var backplane = clusterQuery.ServiceProvider.GetRequiredService<ISignalARRRBackplane>();
                if (backplane.IsEnabled) {
                    var message = new ServerRequestMessage(method, arguments);
                    SignalARRRBackplaneInvokeResult? firstResult;
                    if (clusterQuery.CanUseDirectBackplaneDispatch) {
                        var results = await backplane.InvokeQueryAsync(
                            clusterQuery.HubType,
                            clusterQuery.TargetKind,
                            message,
                            typeof(TResult),
                            groupName: clusterQuery.GroupName,
                            userId: clusterQuery.UserId,
                            singleResult: true,
                            cancellationToken: cancellationToken);
                        firstResult = results.FirstOrDefault();
                    } else {
                        var localDispatcher = clusterQuery.ServiceProvider.GetRequiredService<LocalSignalARRRBackplaneDispatcher>();
                        firstResult = null;

                        foreach (var registration in await DistributedClientQueryResolver.ResolveConnectionsAsync(clusterQuery, cancellationToken)) {
                            try {
                                if (string.Equals(registration.NodeId, backplane.NodeId, StringComparison.Ordinal)) {
                                    var (handled, localResult) = await localDispatcher.InvokeConnectionAsync(
                                        clusterQuery.HubType,
                                        registration.ConnectionId,
                                        message,
                                        typeof(TResult),
                                        cancellationToken);

                                    if (handled) {
                                        firstResult = new SignalARRRBackplaneInvokeResult {
                                            ConnectionId = registration.ConnectionId,
                                            Value = localResult
                                        };
                                        break;
                                    }
                                } else {
                                    firstResult = new SignalARRRBackplaneInvokeResult {
                                        ConnectionId = registration.ConnectionId,
                                        Value = await backplane.InvokeConnectionAsync(clusterQuery.HubType, registration.ConnectionId, message, typeof(TResult), cancellationToken)
                                    };
                                    break;
                                }
                            } catch (Exception e) {
                                failures.Add(new InvalidOperationException(
                                    $"Invoking '{method}' on connection '{registration.ConnectionId}' (node '{registration.NodeId}') failed.", e));
                            }
                        }
                    }

                    if (firstResult == null) {
                        throw NoClientResponded(method, failures);
                    }

                    return new ClientCollectionResult<TResult>(firstResult.ConnectionId, (TResult)firstResult.Value!);
                }
            }


            ClientCollectionResult<TResult>? result = default;
            foreach (var context in clientContext) {

                try {

                    result = await context.Invoke<TResult>(method, arguments, cancellationToken);
                    break;
                } catch (Exception e) {
                    failures.Add(new InvalidOperationException(
                        $"Invoking '{method}' on connection '{context.Id}' failed.", e));
                }

            }

            if (result == null) {
                // Previously this returned null behind a `!`, so the caller got a NullReferenceException
                // at an unrelated frame -- and only on the local path, while the cluster path above threw.
                // Same failure, same exception, regardless of whether a backplane is configured.
                throw NoClientResponded(method, failures);
            }

            return result;
        }

        private static Exception NoClientResponded(string method, IReadOnlyList<Exception> failures) {
            var message = $"No client responded to the invoke request for '{method}'.";

            return failures.Count switch {
                0 => new InvalidOperationException($"{message} No client matched the query."),
                1 => new InvalidOperationException(message, failures[0]),
                _ => new AggregateException(message, failures)
            };
        }


        /// <summary>
        /// Typed invoke on all clients in the collection. Calls each client individually via InvokeCoreAsync,
        /// awaits all in parallel, and returns results per client.
        /// </summary>
        public static async Task<IEnumerable<ClientCollectionResult<TResult>>> InvokeAllAsync<TInterface, TResult>(
            this IEnumerable<ClientContext> clientContexts,
            Func<TInterface, TResult> action,
            CancellationToken cancellationToken = default)
            where TInterface : class {

            var (methodName, arguments) = CaptureCall<TInterface>(action, validateNoStreams: true);
            ValidateNoStreamResult<TResult>(methodName);
            return await clientContexts.InvokeAllAsync<TResult>(methodName, arguments, cancellationToken);
        }

        /// <summary>
        /// Typed invoke on all clients (async method overload).
        /// </summary>
        public static async Task<IEnumerable<ClientCollectionResult<TResult>>> InvokeAllAsync<TInterface, TResult>(
            this IEnumerable<ClientContext> clientContexts,
            Func<TInterface, Task<TResult>> action,
            CancellationToken cancellationToken = default)
            where TInterface : class {

            var (methodName, arguments) = CaptureCall<TInterface>(action, validateNoStreams: true);
            ValidateNoStreamResult<TResult>(methodName);
            return await clientContexts.InvokeAllAsync<TResult>(methodName, arguments, cancellationToken);
        }

        /// <summary>
        /// Typed invoke — calls clients one by one until the first succeeds.
        /// </summary>
        public static async Task<ClientCollectionResult<TResult>> InvokeOneAsync<TInterface, TResult>(
            this IEnumerable<ClientContext> clientContexts,
            Func<TInterface, TResult> action,
            CancellationToken cancellationToken = default)
            where TInterface : class {

            var (methodName, arguments) = CaptureCall<TInterface>(action, validateNoStreams: true);
            ValidateNoStreamResult<TResult>(methodName);
            return await clientContexts.InvokeOneAsync<TResult>(methodName, arguments, cancellationToken);
        }

        /// <summary>
        /// Typed invoke one (async method overload).
        /// </summary>
        public static async Task<ClientCollectionResult<TResult>> InvokeOneAsync<TInterface, TResult>(
            this IEnumerable<ClientContext> clientContexts,
            Func<TInterface, Task<TResult>> action,
            CancellationToken cancellationToken = default)
            where TInterface : class {

            var (methodName, arguments) = CaptureCall<TInterface>(action, validateNoStreams: true);
            ValidateNoStreamResult<TResult>(methodName);
            return await clientContexts.InvokeOneAsync<TResult>(methodName, arguments, cancellationToken);
        }

        private static (string methodName, object[] arguments) CaptureCall<TInterface>(Delegate action, bool validateNoStreams = false) where TInterface : class {
            var capturer = new CapturingProxyCreatorHelper();
            var proxy = ProxyCreator.CreateInstanceFromInterface<TInterface>(capturer);
            action.DynamicInvoke(proxy);

            if (capturer.CapturedMethodName == null)
                throw new InvalidOperationException("No method call was captured from the action.");

            var arguments = capturer.CapturedArguments ?? Array.Empty<object>();

            if (validateNoStreams && arguments.Any(a => a is Stream)) {
                throw new NotSupportedException(
                    $"Method '{capturer.CapturedMethodName}' has a Stream argument. Stream arguments are not supported for multi-client operations. Use single-client GetTypedMethods<T>() instead.");
            }

            return (capturer.CapturedMethodName, arguments);
        }

        private static void ValidateNoStreamResult<TResult>(string methodName) {
            if (typeof(Stream).IsAssignableFrom(typeof(TResult))) {
                throw new NotSupportedException(
                    $"Method '{methodName}' returns a Stream. Stream return values are not supported for multi-client operations. Use single-client GetTypedMethods<T>() instead.");
            }
        }

        public static IEnumerable<ClientContext> WithGroup(this IEnumerable<ClientContext> clientContexts, string groupName) {
            if (clientContexts is ClusterClientQuery clusterQuery) {
                return clusterQuery.WithGroup(groupName);
            }

            return clientContexts.Where(c => c.Groups.Contains(groupName));
        }

        public static IEnumerable<ClientContext> WithAttribute(this IEnumerable<ClientContext> clientContexts, string key) {
            if (clientContexts is ClusterClientQuery clusterQuery) {
                return clusterQuery.WithAttribute(key);
            }

            return clientContexts.Where(c => c.Attributes.Has(key));
        }

        public static IEnumerable<ClientContext> WithAttribute(this IEnumerable<ClientContext> clientContexts, string key, string value) {
            if (clientContexts is ClusterClientQuery clusterQuery) {
                return clusterQuery.WithAttribute(key, value);
            }

            return clientContexts.Where(c => c.Attributes.Has(key, value));
        }

        public static IEnumerable<ClientContext> WithUser(this IEnumerable<ClientContext> clientContexts, string userId) {
            if (clientContexts is ClusterClientQuery clusterQuery) {
                return clusterQuery.WithUser(userId);
            }

            return clientContexts.Where(c => string.Equals(c.UserIdentifier, userId, StringComparison.Ordinal));
        }


    }

    public class ClientCollectionResult<TResult> {

        public string ClientId { get; }

        public TResult Value { get; }

        public ClientCollectionResult(string clientId, TResult value) {
            ClientId = clientId;
            Value = value;
        }
    }

}
