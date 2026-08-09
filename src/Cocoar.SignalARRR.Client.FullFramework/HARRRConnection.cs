using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Common.Interfaces;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.SignalARRR.Client.FullFramework {

    /// <summary>
    /// SignalARRR client connection for .NET Framework 4.6.2+.
    /// Supports typed invoke/send, server-to-client handlers, authorization, cancellation, and file transfer.
    /// Does NOT support streaming (IAsyncEnumerable, ChannelReader) — use Cocoar.SignalARRR.Client for that.
    /// </summary>
    public class HARRRConnection {
        private readonly HubConnection _hubConnection;
        private readonly Func<Task<string>> _accessTokenProvider;
        private readonly Common.Serialization.IProtocolSerializer _serializer;
        private readonly ILogger _logger;

        private readonly ISignalARRRInterfaceCollection _interfaceCollection = new SignalARRRInterfaceCollection();
        private readonly ISignalARRRMethodsCollection _methodsCollection = new SignalARRRMethodsCollection();
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokenSources = new ConcurrentDictionary<Guid, CancellationTokenSource>();

        // Fires when the underlying connection closes; every token bound into a client method
        // links to it, so a handler notices its caller's world ending even when the server never
        // sends an explicit cancellation (N-2). Mirrors the .NET client.
        private CancellationTokenSource _connectionLifetime = new CancellationTokenSource();

        private void OnConnectionClosed() {
            var previous = Interlocked.Exchange(ref _connectionLifetime, new CancellationTokenSource());
            previous.Cancel();
            previous.Dispose();
        }

        public event EventHandler<ServerRequestEventArgs> OnServerRequestMessage;

        public HARRRConnection(HubConnection hubConnection, Func<Task<string>> accessTokenProvider = null) {
            _hubConnection = hubConnection;
            _accessTokenProvider = accessTokenProvider ?? (() => Task.FromResult<string>(null));

            // Auto-detect protocol: use MessagePackProtocolSerializer if MessagePack is configured
            var serviceProvider = _hubConnection.GetServiceProvider();
            var hubProtocol = serviceProvider?.GetService<IHubProtocol>();
            _serializer = hubProtocol?.Name == "messagepack"
                ? (Common.Serialization.IProtocolSerializer)new Common.Serialization.MessagePackProtocolSerializer()
                : new Common.Serialization.JsonProtocolSerializer();
            _logger = serviceProvider?.GetService<ILoggerFactory>()?.CreateLogger<HARRRConnection>() ?? (ILogger)NullLogger.Instance;

            // Native client results — return values are sent back to the server automatically by SignalR
            _hubConnection.On<ServerRequestMessage, string>(MethodNames.ChallengeAuthentication,
                (requestMessage) => _accessTokenProvider());

            _hubConnection.On<ServerRequestMessage, object>(MethodNames.InvokeServerRequest,
                async (requestMessage) => {
                    OnServerRequestMessage?.Invoke(null, new ServerRequestEventArgs(requestMessage));
                    return await InvokeServerRequest(requestMessage);
                });

            // Fire-and-forget — no return value
            _hubConnection.On<ServerRequestMessage>(MethodNames.CancelTokenFromServer, CancelTokenFromServer);

            _hubConnection.On<ServerRequestMessage>(MethodNames.InvokeServerMessage,
                (requestMessage) => {
                    OnServerRequestMessage?.Invoke(null, new ServerRequestEventArgs(requestMessage));
                    // Queued rather than awaited: an awaited handler occupies the receive loop, so
                    // a long-running fire-and-forget call would block every later server-to-client
                    // message — including the cancellation of its own token.
                    QueueServerMessage(requestMessage);
                });

            _hubConnection.Closed += _ => {
                OnConnectionClosed();
                return Task.CompletedTask;
            };
        }

        #region Typed Proxies

        public T GetTypedMethods<T>() where T : class {
            return SignalARRRDispatchProxy.Create<T>(new ClientProxyCreatorHelper(this));
        }

        #endregion

        #region Core Methods

        public async Task<TResult> InvokeCoreAsync<TResult>(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            await PrepareStreamArguments(message);
            message = (await message.WithAuthorizationAsync(_accessTokenProvider)).WithInvocationId();
            try {
                return await _hubConnection.InvokeCoreAsync<TResult>(MethodNames.InvokeMessageResultOnServer, new object[] { message }, cancellationToken);
            } catch (HubException hubException) when (!(hubException is HARRRRemoteException)) {
                // The structured error the server serialized, surfaced as a typed exception — the
                // message becomes the human-readable remote message instead of raw JSON.
                throw HARRRRemoteException.FromReceived(hubException);
            }
        }

        public Task<TResult> InvokeCoreAsync<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args);
            return InvokeCoreAsync<TResult>(msg, cancellationToken);
        }

        public async Task SendCoreAsync(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            await PrepareStreamArguments(message);
            message = (await message.WithAuthorizationAsync(_accessTokenProvider)).WithInvocationId();
            try {
                await _hubConnection.SendCoreAsync(MethodNames.SendMessageToServer, new object[] { message }, cancellationToken);
            } catch (HubException hubException) when (!(hubException is HARRRRemoteException)) {
                throw HARRRRemoteException.FromReceived(hubException);
            }
        }

        public Task SendCoreAsync(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args);
            return SendCoreAsync(msg, cancellationToken);
        }

        public async IAsyncEnumerable<TResult> StreamAsyncCore<TResult>(ClientRequestMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
            message = await message.WithAuthorizationAsync(_accessTokenProvider);
            await foreach (var item in _hubConnection.StreamAsyncCore<TResult>(MethodNames.StreamMessageFromServer, new object[] { message }, cancellationToken)) {
                yield return item;
            }
        }

        public IAsyncEnumerable<TResult> StreamAsyncCore<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args);
            return StreamAsyncCore<TResult>(msg, cancellationToken);
        }

        public async Task<ChannelReader<TResult>> StreamAsChannelCoreAsync<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = await new ClientRequestMessage(methodName, args).WithAuthorizationAsync(_accessTokenProvider);
            return await _hubConnection.StreamAsChannelCoreAsync<TResult>(MethodNames.StreamMessageFromServer, new object[] { msg }, cancellationToken);
        }

        #endregion

        #region On / Handler Registration

        public IDisposable On(string methodName, Type[] parameterTypes, Func<object[], object, Task> handler, object state) {
            return _hubConnection.On(methodName, parameterTypes, handler, state);
        }

        public void RegisterInterface<TInterface, TClass>() where TClass : class, TInterface {
            _interfaceCollection.RegisterInterface<TInterface, TClass>();
        }

        public void RegisterInterface<TInterface, TClass>(TClass instance) where TClass : class, TInterface {
            _interfaceCollection.RegisterInterface<TInterface, TClass>(instance);
        }

        public void RegisterInterface<TInterface, TClass>(Func<IServiceProvider, TClass> factory)
            where TClass : class, TInterface {
            _interfaceCollection.RegisterInterface<TInterface, TClass>(factory);
        }

        public void RegisterInterface(Type interfaceType, Type instanceType) {
            _interfaceCollection.RegisterInterface(interfaceType, instanceType);
        }

        public void RegisterInterface(Type interfaceType, object instance) {
            _interfaceCollection.RegisterInterface(interfaceType, instance);
        }

        #endregion

        #region Server→Client Message Handling

        private async Task<object> InvokeServerRequest(ServerRequestMessage message) {
            message = PrepareServerRequestMessage(message);
            var result = await InvokeAsync(message);

            if (result is Stream stream) {
                return await UploadStreamAndReturnReference(stream);
            }

            return result;
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
        /// for its own token. Messages are chained, not parallelized: fire-and-forget calls keep
        /// executing in arrival order.
        /// </remarks>
        private void QueueServerMessage(ServerRequestMessage message) {
            lock (_fireAndForgetChainLock) {
                _fireAndForgetChain = RunAfter(_fireAndForgetChain, message);
            }
        }

        private async Task RunAfter(Task previous, ServerRequestMessage next) {
            await previous.ConfigureAwait(false);
            // InvokeServerMessage catches everything (fire-and-forget errors are logged, not
            // propagated), so the chain cannot fault and stall later messages.
            await InvokeServerMessage(next).ConfigureAwait(false);
        }

        private async Task InvokeServerMessage(ServerRequestMessage message) {
            try {
                message = PrepareServerRequestMessage(message);
                if (message.StreamId.HasValue) {
                    await InvokeAndStreamBackAsync(message);
                } else {
                    await InvokeAsync(message);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to handle server message '{Method}'", message.Method);
            }
        }

        private async Task InvokeAndStreamBackAsync(ServerRequestMessage message) {
            var streamId = message.StreamId.Value;
            try {
                var result = await InvokeAsync(message);
                await StreamResultToServer(streamId, result);
                await _hubConnection.SendCoreAsync(MethodNames.StreamCompleteToServer, new object[] { streamId, (string)null });
            } catch (Exception ex) {
                try {
                    await _hubConnection.SendCoreAsync(MethodNames.StreamCompleteToServer, new object[] { streamId, ex.GetBaseException().Message });
                } catch { }
            }
        }

        private async Task StreamResultToServer(Guid streamId, object result) {
            if (result == null) return;

            var enumerateMethod = typeof(HARRRConnection)
                .GetMethod(nameof(EnumerateAsyncEnumerable), BindingFlags.NonPublic | BindingFlags.Static);

            var asyncEnumInterface = result.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));

            if (asyncEnumInterface == null && result.GetType().IsGenericType && result.GetType().GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)) {
                asyncEnumInterface = result.GetType();
            }

            if (asyncEnumInterface != null) {
                var elementType = asyncEnumInterface.GetGenericArguments()[0];
                var genericMethod = enumerateMethod.MakeGenericMethod(elementType);
                await (Task)genericMethod.Invoke(null, new object[] { _hubConnection, streamId, result });
            } else {
                await _hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, result });
            }
        }

        private static async Task EnumerateAsyncEnumerable<T>(HubConnection hubConnection, Guid streamId, IAsyncEnumerable<T> source) {
            await foreach (var item in source) {
                await hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, item });
            }
        }

        private async Task<object> InvokeAsync(ServerRequestMessage serverRequestMessage) {
            if (serverRequestMessage.Method.Contains("|")) {
                return await InvokeInterfaceMethodAsync(serverRequestMessage);
            }
            return await InvokeMethodAsync(serverRequestMessage);
        }

        private async Task<object> InvokeMethodAsync(ServerRequestMessage serverRequestMessage) {
            var methodCallInfo = _methodsCollection.GetMethodInformations(serverRequestMessage.Method, serverRequestMessage.Arguments.Length);
            var instance = methodCallInfo.Factory.DynamicInvoke(_hubConnection.GetServiceProvider());
            return await InvokeMethodInfoAsync(instance, methodCallInfo.MethodInfo, serverRequestMessage.Arguments, serverRequestMessage.GenericArguments, serverRequestMessage.CancellationGuid);
        }

        private async Task<object> InvokeInterfaceMethodAsync(ServerRequestMessage serverRequestMessage) {
            var invokeInfos = _interfaceCollection.GetInvokeInformation(serverRequestMessage.Method, serverRequestMessage.Arguments.Length);
            var instance = invokeInfos.Factory.DynamicInvoke(_hubConnection.GetServiceProvider());
            return await InvokeMethodInfoAsync(instance, invokeInfos.MethodInfo, serverRequestMessage.Arguments, serverRequestMessage.GenericArguments, serverRequestMessage.CancellationGuid);
        }

        private async Task<object> InvokeMethodInfoAsync(object instance, MethodInfo methodInfo, IEnumerable<object> arguments, IEnumerable<string> genericArguments, Guid? cancellationTokenGuid) {
            // Connection lifetime rather than None — see _connectionLifetime.
            CancellationToken cancellationToken = _connectionLifetime.Token;
            if (cancellationTokenGuid.HasValue) {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_connectionLifetime.Token);
                _cancellationTokenSources.TryAdd(cancellationTokenGuid.Value, cts);
                cancellationToken = cts.Token;
            }

            var parameters = await BuildExecuteMethodParameters(methodInfo, arguments, cancellationToken);

            if (genericArguments?.Any() == true) {
                var arrType = genericArguments.Select(Common.Helper.TypeHelper.FindType).ToList();
                methodInfo = methodInfo.MakeGenericMethod(arrType.ToArray());
            }

            object result = null;
            if (methodInfo.ReturnType == typeof(void) || methodInfo.ReturnType == typeof(Task)) {
                await InvokeHelper.InvokeVoidMethodAsync(instance, methodInfo, parameters);
            } else if (IsAsyncEnumerableType(methodInfo.ReturnType)) {
                result = methodInfo.Invoke(instance, parameters);
            } else {
                result = await InvokeHelper.InvokeMethodAsync<object>(instance, methodInfo, parameters);
            }

            if (cancellationTokenGuid.HasValue) {
                _cancellationTokenSources.TryRemove(cancellationTokenGuid.Value, out _);
            }

            return result;
        }

        private async Task<object[]> BuildExecuteMethodParameters(MethodInfo methodInfo, IEnumerable<object> parameters, CancellationToken cancellation = default) {
            int paramsPosition = 0;
            var paramsList = parameters.ToList();
            var argumentList = new List<object>();

            foreach (var parameterInfo in methodInfo.GetParameters()) {
                if (paramsList.Count <= paramsPosition) {
                    throw new IndexOutOfRangeException();
                }
                var par = paramsList[paramsPosition];
                paramsPosition++;

                if (parameterInfo.ParameterType == typeof(CancellationToken)) {
                    var tokenFromRef = TryGetCancellationTokenFromReference(par);
                    if (tokenFromRef.HasValue) {
                        argumentList.Add(tokenFromRef.Value);
                        continue;
                    }
                    argumentList.Add(cancellation);
                    continue;
                }

                par = await PrepareArgumentForType(parameterInfo.ParameterType, par);

                if (par == null) {
                    argumentList.Add(null);
                    continue;
                }

                if (parameterInfo.ParameterType != par.GetType()) {
                    if (par.Reflect().TryTo(parameterInfo.ParameterType, out var pt)) {
                        par = pt;
                    } else {
                        par = _serializer.ConvertTo(par, parameterInfo.ParameterType);
                    }
                }

                argumentList.Add(par);
            }

            return argumentList.ToArray();
        }

        private async Task<object> PrepareArgumentForType(Type type, object argument) {
            if (argument == null) {
                if (!type.IsValueType || type.IsNullableType()) {
                    // Reference types (string, classes, etc.) and Nullable<T> accept null
                    return null;
                }
                // Non-nullable value types (int, Guid, etc.) get their default value
                return Activator.CreateInstance(type);
            }

            if (type == typeof(Stream)) {
                var streamReference = _serializer.TryConvertTo<StreamReference>(argument);
                if (streamReference != null && !string.IsNullOrEmpty(streamReference.Uri)) {
                    var httpClient = new HttpClient();
                    var res = await httpClient.GetAsync(new Uri(streamReference.Uri), HttpCompletionOption.ResponseHeadersRead);
                    return await res.Content.ReadAsStreamAsync();
                }
            }

            return argument;
        }

        private ServerRequestMessage PrepareServerRequestMessage(ServerRequestMessage message) {
            var converted = _serializer.TryConvertTo<ServerRequestMessage>(message);
            return converted ?? message;
        }

        private CancellationToken? TryGetCancellationTokenFromReference(object argument) {
            if (argument == null) return null;
            var reference = _serializer.TryConvertTo<CancellationTokenReference>(argument);
            if (reference == null || reference.Id == Guid.Empty) return null;
            // Linked to the connection lifetime: a cancellation the server can no longer deliver
            // (the connection is gone) must still fire.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_connectionLifetime.Token);
            _cancellationTokenSources.TryAdd(reference.Id, cts);
            return cts.Token;
        }

        private void CancelTokenFromServer(ServerRequestMessage requestMessage) {
            if (requestMessage.CancellationGuid.HasValue) {
                if (_cancellationTokenSources.TryRemove(requestMessage.CancellationGuid.Value, out var token)) {
                    token.Cancel();
                }
            }
        }

        #endregion

        private static bool IsAsyncEnumerableType(Type type) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
                return true;
            return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
        }

        #region Stream Upload

        private async Task PrepareStreamArguments(ClientRequestMessage message) {
            if (message.Arguments == null || message.Arguments.Length == 0) return;

            bool hasStream = false;
            for (int i = 0; i < message.Arguments.Length; i++) {
                if (message.Arguments[i] is Stream) { hasStream = true; break; }
            }
            if (!hasStream) return;

            var args = message.Arguments.ToList();
            for (int i = 0; i < args.Count; i++) {
                if (args[i] is Stream stream) {
                    var uploadUrl = await _hubConnection.InvokeCoreAsync<string>(
                        "RequestUploadSlot", Array.Empty<object>(), default);

                    using (var httpClient = new HttpClient()) {
                        using (var content = new StreamContent(stream)) {
                            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                            var response = await httpClient.PostAsync(uploadUrl, content);
                            response.EnsureSuccessStatusCode();
                        }
                    }

                    args[i] = new StreamReference { Uri = uploadUrl };
                }
            }
            message.Arguments = args.ToArray();
        }

        private async Task<StreamReference> UploadStreamAndReturnReference(Stream stream) {
            var uploadUrl = await _hubConnection.InvokeCoreAsync<string>("RequestUploadSlot", Array.Empty<object>(), default);

            using (var httpClient = new HttpClient()) {
                using (var content = new StreamContent(stream)) {
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    var response = await httpClient.PostAsync(uploadUrl, content);
                    response.EnsureSuccessStatusCode();
                }
            }

            return new StreamReference { Uri = uploadUrl };
        }

        #endregion

        #region Factory

        public static HARRRConnection Create(Action<IHubConnectionBuilder> builder, Func<Task<string>> accessTokenProvider = null) {
            var hubConnectionBuilder = new HubConnectionBuilder();
            builder(hubConnectionBuilder);
            var hubConnection = hubConnectionBuilder.Build();
            return new HARRRConnection(hubConnection, accessTokenProvider);
        }

        public static HARRRConnection Create(HubConnection hubConnection, Func<Task<string>> accessTokenProvider = null) {
            return new HARRRConnection(hubConnection, accessTokenProvider);
        }

        #endregion

        #region HubConnection Decorator

        public HubConnection AsSignalRHubConnection() => _hubConnection;

        public event Func<Exception, Task> Closed {
            add => _hubConnection.Closed += value;
            remove => _hubConnection.Closed -= value;
        }

        public event Func<Exception, Task> Reconnecting {
            add => _hubConnection.Reconnecting += value;
            remove => _hubConnection.Reconnecting -= value;
        }

        public event Func<string, Task> Reconnected {
            add => _hubConnection.Reconnected += value;
            remove => _hubConnection.Reconnected -= value;
        }

        public string ConnectionId => _hubConnection.ConnectionId;
        public HubConnectionState State => _hubConnection.State;

        public Task StartAsync(CancellationToken cancellation = default) => _hubConnection.StartAsync(cancellation);
        public Task StopAsync(CancellationToken cancellation = default) => _hubConnection.StopAsync(cancellation);

        public async Task DisposeAsync() => await _hubConnection.DisposeAsync();

        #endregion
    }

    public class ServerRequestEventArgs : EventArgs {
        public ServerRequestMessage ServerRequestMessage { get; }

        public ServerRequestEventArgs(ServerRequestMessage serverRequestMessage) {
            ServerRequestMessage = serverRequestMessage;
        }
    }
}
