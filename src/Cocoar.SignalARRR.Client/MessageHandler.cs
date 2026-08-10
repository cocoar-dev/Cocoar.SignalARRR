using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Client.ExtensionMethods;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.Common.Interfaces;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.SignalARRR.Client {
    internal class MessageHandler {
        private readonly ClientConnectionContext _connectionContext;
        private readonly Common.Serialization.IProtocolSerializer _serializer;
        private readonly ILogger _logger;
        private ISignalARRRMethodsCollection MethodsCollection { get; set; } = new SignalARRRMethodsCollection();

        private ISignalARRRInterfaceCollection InterfaceCollection { get; set; } = new SignalARRRInterfaceCollection();

        public MessageHandler(ClientConnectionContext connectionContext, Common.Serialization.IProtocolSerializer? serializer = null, ILogger? logger = null) {
            _connectionContext = connectionContext;
            _serializer = serializer ?? new Common.Serialization.JsonProtocolSerializer();
            _logger = logger ?? NullLogger.Instance;
        }

        public async Task<string?> ChallengeAuthentication(ServerRequestMessage message) {
            return await _connectionContext.AccessTokenProvider();
        }

        /// <summary>
        /// Everything logged while a server-to-client call runs carries the method and the server's
        /// invocation id, so a client log line can be matched to the server line that caused it.
        /// </summary>
        private IDisposable? BeginServerRequestLogScope(ServerRequestMessage message) =>
            _logger.BeginScope(new Dictionary<string, object?> {
                ["SignalARRRMethod"] = message.Method,
                ["SignalARRRInvocationId"] = message.Id,
            });

        public async Task<object?> InvokeServerRequest(ServerRequestMessage message) {
            message = PrepareServerRequestMessage(message);
            using var logScope = BeginServerRequestLogScope(message);
            using var activity = SignalARRRClientTelemetry.StartIncomingCall(message);
            try {
                var result = await InvokeAsync(message);

                // If the result is a Stream, upload it to the server and return a StreamReference
                if (result is Stream stream) {
                    return await UploadStreamAndReturnReference(stream);
                }

                return result;
            } catch (Exception ex) {
                SignalARRRClientTelemetry.RecordFailure(activity, ex);
                throw;
            }
        }

        private async Task<StreamReference> UploadStreamAndReturnReference(Stream stream) {
            var hubConnection = _connectionContext.GetHubConnection();

            // Ask server for an upload URL
            var uploadUrl = await hubConnection.InvokeCoreAsync<string>("RequestUploadSlot", Array.Empty<object>(), default);

            // Upload the stream via HTTP POST
            using var httpClient = new HttpClient();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var response = await httpClient.PostAsync(uploadUrl, content);
            response.EnsureSuccessStatusCode();

            return new StreamReference { Uri = uploadUrl };
        }

        private Task _fireAndForgetChain = Task.CompletedTask;
        private readonly object _fireAndForgetChainLock = new object();

        /// <summary>
        /// Runs a fire-and-forget server message without occupying SignalR's receive loop.
        /// </summary>
        /// <remarks>
        /// The receive loop awaits an async handler before it processes the next message. With the
        /// method executed inline, one long-running fire-and-forget call therefore blocked every
        /// later server-to-client message on the connection — including <c>CancelTokenFromServer</c>
        /// for its own token, so exactly the calls one wants to cancel were the ones that could
        /// not be. (The invoke path never had the problem: SignalR dispatches client-result
        /// invocations off the loop.) Messages are chained, not parallelized: fire-and-forget
        /// calls keep executing in arrival order, matching SignalR's own sequential default.
        /// </remarks>
        internal void QueueServerMessage(ServerRequestMessage message) {
            lock (_fireAndForgetChainLock) {
                _fireAndForgetChain = RunAfter(_fireAndForgetChain, message);
            }

            async Task RunAfter(Task previous, ServerRequestMessage next) {
                await previous.ConfigureAwait(false);
                // InvokeServerMessage catches everything (fire-and-forget errors are logged, not
                // propagated), so the chain cannot fault and stall later messages.
                await InvokeServerMessage(next).ConfigureAwait(false);
            }
        }

        public async Task InvokeServerMessage(ServerRequestMessage message) {

            try {
                message = PrepareServerRequestMessage(message);
                using var logScope = BeginServerRequestLogScope(message);
                using var activity = SignalARRRClientTelemetry.StartIncomingCall(message);
                try {
                    if (message.StreamId.HasValue) {
                        await InvokeAndStreamBackAsync(message);
                    } else {
                        await InvokeAsync(message);
                    }
                } catch (Exception ex) {
                    SignalARRRClientTelemetry.RecordFailure(activity, ex);
                    throw;
                }
            } catch (Exception ex) {
                // Fire-and-forget methods don't propagate errors to the server,
                // but log them so developers can diagnose failed server-to-client pushes.
                _logger.LogError(ex, "Failed to handle server message '{Method}'", message.Method);
            }
        }

        private async Task InvokeAndStreamBackAsync(ServerRequestMessage message) {
            var streamId = message.StreamId!.Value;
            var hubConnection = _connectionContext.GetHubConnection();
            try {
                var result = await InvokeAsync(message);
                await StreamResultToServer(hubConnection, streamId, result);
                await hubConnection.SendCoreAsync(MethodNames.StreamCompleteToServer, new object?[] { streamId, (string?)null });
            } catch (Exception ex) {
                try {
                    await hubConnection.SendCoreAsync(MethodNames.StreamCompleteToServer, new object[] { streamId, ex.GetBaseException().Message });
                } catch {
                    // Best effort — connection may be gone
                }
            }
        }

        private static async Task StreamResultToServer(HubConnection hubConnection, Guid streamId, object result) {
            if (result == null) return;

            // Try to enumerate as IAsyncEnumerable<T> using reflection-free helper
            var enumerateMethod = typeof(MessageHandler)
                .GetMethod(nameof(EnumerateAsyncEnumerable), BindingFlags.NonPublic | BindingFlags.Static);

            // Find the IAsyncEnumerable<T> interface to get T
            var asyncEnumInterface = result.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));

            if (asyncEnumInterface == null && result.GetType().IsGenericType && result.GetType().GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)) {
                asyncEnumInterface = result.GetType();
            }

            if (asyncEnumInterface != null) {
                var elementType = asyncEnumInterface.GetGenericArguments()[0];
                var genericMethod = enumerateMethod!.MakeGenericMethod(elementType);
                await (Task)genericMethod.Invoke(null, new object[] { hubConnection, streamId, result })!;
            } else {
                // Single result — send as one item
                await hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, result });
            }
        }

        private static async Task EnumerateAsyncEnumerable<T>(HubConnection hubConnection, Guid streamId, IAsyncEnumerable<T> source) {
            await foreach (var item in source) {
                await hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, item! });
            }
        }


        public void RegisterInterface<TInterface, TClass>() where TClass : class, TInterface {
            InterfaceCollection.RegisterInterface<TInterface, TClass>();
        }
        public void RegisterInterface<TInterface, TClass>(TClass instance) where TClass : class, TInterface {

            InterfaceCollection.RegisterInterface<TInterface, TClass>(instance);
        }

        public void RegisterInterface<TInterface, TClass>(Func<IServiceProvider, TClass> factory)
            where TClass : class, TInterface {

            InterfaceCollection.RegisterInterface<TInterface, TClass>(factory);
        }


        public void RegisterInterface(Type interfaceType, Type instanceType) {

            InterfaceCollection.RegisterInterface(interfaceType, instanceType);
        }

        public void RegisterInterface(Type interfaceType, object instance) {
            InterfaceCollection.RegisterInterface(interfaceType, instance);
        }

        public void RegisterInterface(Type interfaceType, Func<IServiceProvider, object> factory) {
            InterfaceCollection.RegisterInterface(interfaceType, factory);
        }








        private Task<object> InvokeAsync(ServerRequestMessage serverRequestMessage) {

            if (serverRequestMessage.Method.Contains("|")) {
                return InvokeInterfaceMethodAsync(serverRequestMessage);
            }

            return InvokeMethodAsync(serverRequestMessage);
        }
        private async Task<object> InvokeMethodAsync(ServerRequestMessage serverRequestMessage) {



            var methodCallInfo = MethodsCollection.GetMethodInformations(serverRequestMessage.Method, serverRequestMessage.Arguments.Length);

            var instance = methodCallInfo.Factory.DynamicInvoke(_connectionContext.GetHubConnection().GetServiceProvider())!;

            return InvokeMethodInfoAsync(instance, methodCallInfo.MethodInfo, serverRequestMessage.Arguments, serverRequestMessage.GenericArguments, serverRequestMessage.CancellationGuid);

        }

        private Task<object> InvokeInterfaceMethodAsync(ServerRequestMessage serverRequestMessage) {

            var invokeInfos = InterfaceCollection.GetInvokeInformation(serverRequestMessage.Method, serverRequestMessage.Arguments.Length);
            var instance = invokeInfos.Factory.DynamicInvoke(_connectionContext.GetHubConnection().GetServiceProvider())!;
            return InvokeMethodInfoAsync(instance, invokeInfos.MethodInfo, serverRequestMessage.Arguments, serverRequestMessage.GenericArguments, serverRequestMessage.CancellationGuid);

        }


        private async Task<object> InvokeMethodInfoAsync(object instance, MethodInfo methodInfo, IEnumerable<object> arguments, IEnumerable<string> genericArguments, Guid? cancellationTokenGuid) {

            // Every linked source this invocation creates, so the finally can let go of all of
            // them: the call-level one below, plus one per CancellationToken parameter bound in
            // BuildExecuteMethodParameters.
            var ownedTokenIds = new List<Guid>(1);
            var ownershipTransferred = false;

            try {
                // Connection lifetime rather than None: the handler notices its caller's world ending
                // even when the server never sends an explicit cancellation.
                CancellationToken cancellationToken = _connectionLifetime.Token;
                if (cancellationTokenGuid.HasValue) {
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_connectionLifetime.Token);
                    cancellationTokenSources.TryAdd(cancellationTokenGuid.Value, cancellation);
                    ownedTokenIds.Add(cancellationTokenGuid.Value);
                    cancellationToken = cancellation.Token;
                }


                var parameters = await BuildExecuteMethodParameters(methodInfo, arguments, cancellationToken, ownedTokenIds);

                if (genericArguments?.Any() == true) {

                    var arrType = genericArguments.Select(TypeHelper.FindType).ToList();
                    methodInfo = methodInfo.MakeGenericMethod(arrType.ToArray()!);
                }

                object? result = null;
                if (methodInfo.ReturnType == typeof(void) || methodInfo.ReturnType == typeof(Task)) {
                    await InvokeHelper.InvokeVoidMethodAsync(instance, methodInfo, parameters);
                } else if (IsAsyncEnumerableType(methodInfo.ReturnType)) {
                    // IAsyncEnumerable<T> — invoke directly, don't try to await as Task
                    result = methodInfo.Invoke(instance, parameters);

                    // The stream is consumed after this method returns, so its token has to outlive
                    // us. Cleaning up here would hand the caller an already-cancelled enumeration.
                    // These sources are released when the connection ends, as they were before.
                    ownershipTransferred = true;
                } else {
                    result = await InvokeHelper.InvokeMethodAsync<object>(instance, methodInfo, parameters);
                }

                return result!;
            }
            finally {
                // Dispose, not merely remove. CreateLinkedTokenSource registers a callback on the
                // parent, and the parent here is the connection lifetime — so a source that is
                // dropped without being disposed leaves that registration attached for as long as
                // the connection lives. On a long-lived connection taking a server push every few
                // seconds, that grows without bound and nothing fails while it happens. The
                // per-parameter sources were worse: nothing removed them at all unless the server
                // happened to cancel them.
                if (!ownershipTransferred) {
                    foreach (var id in ownedTokenIds) {
                        if (cancellationTokenSources.TryRemove(id, out var cts)) {
                            cts.Dispose();
                        }
                    }
                }
            }
        }

        private ConcurrentDictionary<Guid, CancellationTokenSource> cancellationTokenSources = new ConcurrentDictionary<Guid, CancellationTokenSource>();

        /// <summary>
        /// How many linked cancellation sources are still held. Test seam: a leak here is invisible
        /// from the outside — nothing fails, the process just grows — so the regression test needs
        /// a way to see that finished invocations let go of theirs.
        /// </summary>
        internal int TrackedCancellationSourceCount => cancellationTokenSources.Count;

        /// <summary>
        /// Fires when the underlying connection closes. Every token bound into a client method
        /// links to it — without this, a handler with a <see cref="CancellationToken"/> parameter
        /// got <c>CancellationToken.None</c> and kept running into the void after the connection
        /// died (N-2), while a server method in the same situation observed
        /// <c>ConnectionAborted</c>. No own abort source: SignalR's Closed event *is* the signal,
        /// and Stateful Reconnect extends exactly that lifetime.
        /// </summary>
        private CancellationTokenSource _connectionLifetime = new CancellationTokenSource();

        internal void OnConnectionClosed() {
            // A fresh lifetime begins for a potential restart of the same connection object.
            var previous = Interlocked.Exchange(ref _connectionLifetime, new CancellationTokenSource());
            previous.Cancel();
            previous.Dispose();
        }

        // GetParameters() clones a fresh ParameterInfo[] on every call; the registry's MethodInfo
        // instances live for the process, so the clone is cached per method (P-6).
        private static readonly ConcurrentDictionary<MethodInfo, ParameterInfo[]> ParameterCache = new();

        private async Task<object[]> BuildExecuteMethodParameters(MethodInfo methodInfo, IEnumerable<object> parameters, CancellationToken cancellation = default, ICollection<Guid>? ownedTokenIds = null) {

            int paramsPosition = 0;
            var @params = parameters as IList<object> ?? parameters.ToList();

            var methodParameters = ParameterCache.GetOrAdd(methodInfo, static m => m.GetParameters());
            var bound = new object[methodParameters.Length];

            for (var i = 0; i < methodParameters.Length; i++) {
                var parameterInfo = methodParameters[i];
                // `<=`, and before the index. The guard used to read `@params.Count < paramsPosition`
                // *and* sat above the access anyway, so it could never fire: the position advances by
                // one per parameter, so it reaches Count but never passes it. The bare index threw an
                // IndexOutOfRangeException with nothing to say about which parameter was missing.
                if (paramsPosition >= @params.Count) {
                    throw new ArgumentException(
                        $"Parameter '{parameterInfo.Name}' (position {paramsPosition}) of '{methodInfo.Name}': " +
                        $"not enough arguments provided. Expected at least {paramsPosition + 1}, got {@params.Count}.");
                }

                var par = @params[paramsPosition];
                paramsPosition++;

                if (parameterInfo.ParameterType == typeof(CancellationToken)) {
                    // Check if the argument is a CancellationTokenReference (per-parameter cancellation from server)
                    var tokenFromRef = TryGetCancellationTokenFromReference(par, ownedTokenIds);
                    bound[i] = tokenFromRef ?? cancellation;
                    continue;
                }

                par = await PrepareArgumentForType(parameterInfo.ParameterType, par);

                if (par == null) {
                    bound[i] = null!;
                    continue;
                }

                if (parameterInfo.ParameterType != par.GetType()) {
                    if (par.Reflect().TryTo(parameterInfo.ParameterType, out var pt)) {
                        par = pt;
                    } else {
                        par = _serializer.ConvertTo(par, parameterInfo.ParameterType);
                    }
                }

                bound[i] = par!;
            }

            return bound;
        }

        private async Task<object?> PrepareArgumentForType(Type type, object argument) {

            if (argument == null) {
                if (!type.IsValueType || type.IsNullableType()) {
                    // Reference types (string, classes, etc.) and Nullable<T> accept null
                    return null;
                } else {
                    // Non-nullable value types (int, Guid, etc.) get their default value
                    return Activator.CreateInstance(type);
                }
            }

            if (type == typeof(Stream)) {
                var streamReference = _serializer.TryConvertTo<StreamReference>(argument);
                if (streamReference != null && !string.IsNullOrEmpty(streamReference.Uri)) {
                    var resolver = new StreamReferenceResolver(streamReference, _connectionContext);
                    return await resolver.ProcessStreamArgument();
                }
            }

            return argument;
        }




        private ServerRequestMessage PrepareServerRequestMessage(ServerRequestMessage message) {
            // Re-deserialize to ensure consistent typing (e.g., JsonElement → typed objects)
            var converted = _serializer.TryConvertTo<ServerRequestMessage>(message);
            return converted ?? message;
        }

        private static bool IsAsyncEnumerableType(Type type) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
                return true;
            return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
        }

        private CancellationToken? TryGetCancellationTokenFromReference(object argument, ICollection<Guid>? ownedTokenIds = null) {
            if (argument == null) return null;

            var reference = _serializer.TryConvertTo<CancellationTokenReference>(argument);

            if (reference == null || reference.Id == Guid.Empty) return null;

            // Linked to the connection lifetime: a per-token cancellation the server can no longer
            // deliver (the connection is gone) must still fire.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_connectionLifetime.Token);
            cancellationTokenSources.TryAdd(reference.Id, cts);
            ownedTokenIds?.Add(reference.Id);
            return cts.Token;
        }

        public void CancelTokenFromServer(ServerRequestMessage requestMessage) {

            if (requestMessage.CancellationGuid.HasValue) {
                // Look up rather than remove: the invocation that created this source owns it and
                // disposes it when it finishes. Taking it out here would leave two parties each
                // believing they hold the last reference — and one of them disposing it while the
                // handler still reads the token.
                if (cancellationTokenSources.TryGetValue(requestMessage.CancellationGuid.Value, out var token)) {
                    try {
                        token.Cancel();
                    }
                    catch (ObjectDisposedException) {
                        // The invocation finished between the lookup and the cancel. There is
                        // nothing left to cancel, and the source is already gone.
                    }
                }
            }

        }
    }
}
