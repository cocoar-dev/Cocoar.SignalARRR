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
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.AspNetCore.Http.Connections.Client;
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
        private readonly JsonSerializerOptions _payloadOptions;
        private readonly ILogger _logger;

        /// <summary>
        /// The registered contracts by the names that may appear on the wire: the allow-list for
        /// calls from the server. Each entry pairs the contract's generated dispatcher with where its
        /// implementation comes from.
        /// </summary>
        private readonly ConcurrentDictionary<string, (ContractDispatcher Dispatcher, Func<IServiceProvider, object> Factory)> _registrations =
            new ConcurrentDictionary<string, (ContractDispatcher Dispatcher, Func<IServiceProvider, object> Factory)>(StringComparer.Ordinal);
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

        /// <summary>Wraps a <c>HubConnection</c> that is already built, without a message credential.</summary>
        public HARRRConnection(HubConnection hubConnection) : this(hubConnection, (HARRRConnectionOptions)null) {
        }

        /// <summary>
        /// The message credential under its former name: <paramref name="accessTokenProvider"/> is
        /// what SignalARRR sends with every message, not SignalR's connection token.
        /// </summary>
        [Obsolete("Use HARRRConnection.Create(hubConnection, options => options.WithMessageCredential(...)).")]
        public HARRRConnection(HubConnection hubConnection, Func<Task<string>> accessTokenProvider = null)
            : this(hubConnection, new HARRRConnectionOptions { MessageCredential = accessTokenProvider }) {
        }

        private HARRRConnection(HubConnection hubConnection, HARRRConnectionOptions options) {
            _hubConnection = hubConnection;
            _accessTokenProvider = options?.MessageCredential ?? (() => Task.FromResult<string>(null));

            var serviceProvider = _hubConnection.GetServiceProvider();
            // Arguments are read with the hub protocol's JSON options. With the MessagePack protocol
            // they arrive as plain .NET objects and take a JSON round-trip through the same options.
            _payloadOptions = JsonPayloadOptions(serviceProvider);
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

        /// <summary>A proxy for the <c>[SignalARRRContract]</c> interface <typeparamref name="T"/>, generated at compile time.</summary>
        public T GetTypedMethods<T>() where T : class {
            return ProxyCreator.CreateInstanceFromInterface<T>(new ClientProxyCreatorHelper(this));
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

        // Every overload ends in the generated dispatcher of TInterface, so the contract has to be a
        // [SignalARRRContract] interface; registering anything else fails at once with a message
        // saying so, rather than at the first call from the server.

        /// <summary>Receives the server's calls on <typeparamref name="TInterface"/> with <paramref name="instance"/>.</summary>
        public void RegisterInterface<TInterface>(TInterface instance) where TInterface : class {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            RegisterInterface<TInterface>(_ => instance);
        }

        /// <summary>Receives the server's calls on <typeparamref name="TInterface"/> with an instance <paramref name="factory"/> creates per call.</summary>
        public void RegisterInterface<TInterface>(Func<IServiceProvider, TInterface> factory) where TInterface : class {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            var dispatcher = DispatcherRegistry.Find<TInterface>()
                ?? throw new InvalidOperationException(
                    $"'{typeof(TInterface).FullName}' has no generated dispatcher, so the client cannot receive calls on it. " +
                    "Mark the interface with [SignalARRRContract] and make sure the project that declares or uses it references Cocoar.SignalARRR.Client.FullFramework (or the source generator).");

            if (dispatcher.UnsupportedReason != null) {
                throw new InvalidOperationException(dispatcher.UnsupportedReason);
            }

            var registration = (dispatcher, (Func<IServiceProvider, object>)(sp => factory(sp)));
            foreach (var wireName in GetWireNames(dispatcher)) {
                if (_registrations.TryGetValue(wireName, out var existing) && existing.Dispatcher.ContractType != dispatcher.ContractType) {
                    throw new InvalidOperationException(
                        $"Cannot register interface '{dispatcher.ContractType.AssemblyQualifiedName}': " +
                        $"'{existing.Dispatcher.ContractType.AssemblyQualifiedName}' is already registered under the wire name '{wireName}'.");
                }

                _registrations[wireName] = registration;
            }
        }

        /// <summary>
        /// Receives the server's calls on <typeparamref name="TInterface"/> with a
        /// <typeparamref name="TClass"/> from the connection's service provider, or created with its
        /// dependencies from there, per call.
        /// </summary>
        public void RegisterInterface<TInterface, TClass>() where TInterface : class where TClass : class, TInterface {
            RegisterInterface<TInterface>(sp => sp.GetService<TClass>() ?? ActivatorUtilities.CreateInstance<TClass>(sp));
        }

        /// <summary>Receives the server's calls on <typeparamref name="TInterface"/> with <paramref name="instance"/>.</summary>
        public void RegisterInterface<TInterface, TClass>(TClass instance) where TInterface : class where TClass : class, TInterface {
            RegisterInterface<TInterface>(instance);
        }

        /// <summary>Receives the server's calls on <typeparamref name="TInterface"/> with an instance <paramref name="factory"/> creates per call.</summary>
        public void RegisterInterface<TInterface, TClass>(Func<IServiceProvider, TClass> factory) where TInterface : class where TClass : class, TInterface {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            RegisterInterface<TInterface>(sp => factory(sp));
        }

        private static IEnumerable<string> GetWireNames(ContractDispatcher dispatcher) {
            yield return dispatcher.WireName;

            // A declared [MessageName] replaces the type's identity on the wire. Without one, the
            // assembly-qualified name is accepted as well, as it always was.
            if (dispatcher.WireName == dispatcher.ContractType.FullName && dispatcher.ContractType.AssemblyQualifiedName != null) {
                yield return dispatcher.ContractType.AssemblyQualifiedName;
            }
        }

        #endregion

        #region Server→Client Message Handling

        private async Task<object> InvokeServerRequest(ServerRequestMessage message) {
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

            if (result is IStreamingResult streaming) {
                // The generated dispatcher knows the element type; no reflection to find it.
                await streaming.ForEachAsync(
                    item => _hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, item }),
                    CancellationToken.None);
            } else {
                await _hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, result });
            }
        }

        private async Task<object> InvokeAsync(ServerRequestMessage message) {
            var separator = message.Method.IndexOf(WireName.Separator);
            if (separator < 0) {
                throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound, $"Method '{message.Method}' not found!");
            }

            var interfaceName = message.Method.Substring(0, separator);
            var methodName = message.Method.Substring(separator + 1);

            if (!_registrations.TryGetValue(interfaceName, out var registration)) {
                throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound, $"Interface '{interfaceName}' is not registered.");
            }

            if (message.GenericArguments != null && message.GenericArguments.Length > 0) {
                // Older servers can still send these; resolving a type argument by name needs reflection.
                throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound,
                    $"Method '{methodName}' was called with generic arguments, which a client does not support.");
            }

            var instance = registration.Factory(_hubConnection.GetServiceProvider());

            // Every linked source this invocation creates — the call-level one and one per
            // CancellationToken parameter — so the finally can let go of all of them.
            var ownedTokenIds = new List<Guid>(1);
            var ownershipTransferred = false;

            try {
                // Connection lifetime rather than None — see _connectionLifetime.
                CancellationToken cancellationToken = _connectionLifetime.Token;
                if (message.CancellationGuid.HasValue) {
                    var cts = CancellationTokenSource.CreateLinkedTokenSource(_connectionLifetime.Token);
                    _cancellationTokenSources.TryAdd(message.CancellationGuid.Value, cts);
                    ownedTokenIds.Add(message.CancellationGuid.Value);
                    cancellationToken = cts.Token;
                }

                var arguments = new JsonArgumentReader(message.Arguments, _payloadOptions, new FullFrameworkCallContext(this, cancellationToken, ownedTokenIds));

                object result;
                try {
                    result = await registration.Dispatcher.InvokeUntypedAsync(instance, methodName, arguments);
                } catch (DispatchException dispatchException) {
                    throw new MethodResolutionException(dispatchException.Code, dispatchException.Message);
                }

                // A stream is consumed after this method returns, so its token has to outlive us.
                ownershipTransferred = result is IStreamingResult;
                return result;
            }
            finally {
                // Dispose, not merely remove: CreateLinkedTokenSource registers a callback on the
                // parent, which here is the connection lifetime, so dropping a source without
                // disposing leaves that registration attached for as long as the connection lives.
                // Mirrors the fix in the .NET client's MessageHandler.
                if (!ownershipTransferred) {
                    foreach (var id in ownedTokenIds) {
                        if (_cancellationTokenSources.TryRemove(id, out var cts)) {
                            cts.Dispose();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resolves the slots of one call that the connection, not JSON, has to fill: tokens the
        /// server can cancel, and streams that travel through file transfer. Mirrors the .NET client.
        /// </summary>
        private sealed class FullFrameworkCallContext : CallContext {
            private readonly HARRRConnection _connection;
            private readonly ICollection<Guid> _ownedTokenIds;

            public FullFrameworkCallContext(HARRRConnection connection, CancellationToken callCancellation, ICollection<Guid> ownedTokenIds) {
                _connection = connection;
                CallCancellation = callCancellation;
                _ownedTokenIds = ownedTokenIds;
            }

            public override CancellationToken CallCancellation { get; }

            public override CancellationToken ResolveCancellationToken(ArgumentReader reader, int index) {
                var reference = index < reader.Count ? TryRead<CancellationTokenReference>(reader, index) : null;
                if (reference == null || reference.Id == Guid.Empty) {
                    return CallCancellation;
                }

                // Linked to the connection lifetime: a cancellation the server can no longer deliver
                // (the connection is gone) must still fire.
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_connection._connectionLifetime.Token);
                _connection._cancellationTokenSources.TryAdd(reference.Id, cts);
                _ownedTokenIds.Add(reference.Id);
                return cts.Token;
            }

            public override async Task<Stream> ResolveStreamAsync(ArgumentReader reader, int index) {
                if (index >= reader.Count) return null;
                if (reader.GetRaw(index) is Stream stream) return stream;

                var reference = TryRead<StreamReference>(reader, index);
                if (reference == null || string.IsNullOrEmpty(reference.Uri)) return null;

                var httpClient = new HttpClient();
                using (var request = await FileTransferHttp.AuthorizeAsync(
                    new HttpRequestMessage(HttpMethod.Get, new Uri(reference.Uri)), _connection._accessTokenProvider)) {
                    var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStreamAsync();
                }
            }

            private static T TryRead<T>(ArgumentReader reader, int index) where T : class {
                try {
                    return reader.Read<T>(index);
                } catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is NotSupportedException) {
                    // Not a reference: an ordinary value in a slot the contract declares as token or stream.
                    return null;
                }
            }
        }

        private void CancelTokenFromServer(ServerRequestMessage requestMessage) {
            if (requestMessage.CancellationGuid.HasValue) {
                // Look up rather than remove: the invocation owns the source and disposes it when
                // it finishes. See the .NET client's MessageHandler for the reasoning.
                if (_cancellationTokenSources.TryGetValue(requestMessage.CancellationGuid.Value, out var token)) {
                    try {
                        token.Cancel();
                    }
                    catch (ObjectDisposedException) {
                        // The invocation finished between the lookup and the cancel.
                    }
                }
            }
        }

        #endregion

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
                            using (var request = await FileTransferHttp.AuthorizeAsync(
                                new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = content }, _accessTokenProvider)) {
                                var response = await httpClient.SendAsync(request);
                                response.EnsureSuccessStatusCode();
                            }
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
                    using (var request = await FileTransferHttp.AuthorizeAsync(
                        new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = content }, _accessTokenProvider)) {
                        var response = await httpClient.SendAsync(request);
                        response.EnsureSuccessStatusCode();
                    }
                }
            }

            return new StreamReference { Uri = uploadUrl };
        }

        #endregion

        #region Factory

        /// <summary>Builds the connection through <paramref name="builder"/>, without credentials.</summary>
        public static HARRRConnection Create(Action<IHubConnectionBuilder> builder) {
            return Create(builder, (Action<HARRRConnectionOptionsBuilder>)null);
        }

        /// <summary>
        /// Builds the connection through <paramref name="builder"/>. The credentials come from
        /// <paramref name="optionsBuilder"/>: <c>WithCredential</c>, <c>WithConnectionCredential</c>,
        /// <c>WithMessageCredential</c> — the same options as in every SignalARRR client.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The connection credential is set both through SignalR's <c>AccessTokenProvider</c> in
        /// <c>WithUrl</c> and through <c>WithConnectionCredential</c>/<c>WithCredential</c>.
        /// </exception>
        public static HARRRConnection Create(Action<IHubConnectionBuilder> builder, Action<HARRRConnectionOptionsBuilder> optionsBuilder) {
            var hubConnectionBuilder = new HubConnectionBuilder();
            builder(hubConnectionBuilder);
            var options = BuildOptions(optionsBuilder);
            var connectionCredential = options.ConnectionCredential;
            if (connectionCredential != null) {
                // Registered after the caller's WithUrl, so it runs after the options WithUrl set.
                hubConnectionBuilder.Services.Configure<HttpConnectionOptions>(httpOptions => {
                    if (httpOptions.AccessTokenProvider != null) {
                        throw new InvalidOperationException(
                            "The connection credential is set twice: through SignalR's AccessTokenProvider in WithUrl and through SignalARRR's ConnectionCredential. Set only one of them.");
                    }
                    httpOptions.AccessTokenProvider = connectionCredential;
                });
            }
            return new HARRRConnection(hubConnectionBuilder.Build(), options);
        }

        /// <summary>
        /// Wraps a <c>HubConnection</c> that is already built. Its connection credential is SignalR's
        /// own <c>AccessTokenProvider</c>; only the message credential can be set here.
        /// </summary>
        /// <exception cref="ArgumentException">A connection credential is set in <paramref name="optionsBuilder"/>.</exception>
        public static HARRRConnection Create(HubConnection hubConnection, Action<HARRRConnectionOptionsBuilder> optionsBuilder) {
            var options = BuildOptions(optionsBuilder);
            if (options.ConnectionCredential != null) {
                throw new ArgumentException(
                    "A connection credential cannot be applied to a HubConnection that is already built: SignalR fixed its AccessTokenProvider when it was built. " +
                    "Use Create(builder => builder.WithUrl(...), ...) instead, or set AccessTokenProvider in WithUrl yourself and only the message credential here.",
                    nameof(optionsBuilder));
            }
            return new HARRRConnection(hubConnection, options);
        }

        /// <summary>
        /// The message credential under its former name: <paramref name="accessTokenProvider"/> is what
        /// SignalARRR sends with every message, not SignalR's connection token.
        /// </summary>
        [Obsolete("Use Create(builder, options => options.WithMessageCredential(...)), or WithCredential for one credential covering the connection and every message.")]
        public static HARRRConnection Create(Action<IHubConnectionBuilder> builder, Func<Task<string>> accessTokenProvider = null) {
            var hubConnectionBuilder = new HubConnectionBuilder();
            builder(hubConnectionBuilder);
            return new HARRRConnection(hubConnectionBuilder.Build(), new HARRRConnectionOptions { MessageCredential = accessTokenProvider });
        }

        /// <summary>
        /// The message credential under its former name: <paramref name="accessTokenProvider"/> is what
        /// SignalARRR sends with every message, not SignalR's connection token.
        /// </summary>
        [Obsolete("Use Create(hubConnection, options => options.WithMessageCredential(...)).")]
        public static HARRRConnection Create(HubConnection hubConnection, Func<Task<string>> accessTokenProvider = null) {
            return new HARRRConnection(hubConnection, new HARRRConnectionOptions { MessageCredential = accessTokenProvider });
        }

        private static HARRRConnectionOptions BuildOptions(Action<HARRRConnectionOptionsBuilder> optionsBuilder) {
            var builder = new HARRRConnectionOptionsBuilder();
            optionsBuilder?.Invoke(builder);
            return builder;
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

        /// <summary>
        /// The hub protocol's JSON options with SignalARRR's contracts appended — the options the
        /// arguments were written with, so conversions agree with them.
        /// </summary>
        private static System.Text.Json.JsonSerializerOptions JsonPayloadOptions(IServiceProvider serviceProvider) {
            var options = serviceProvider?.GetService<Microsoft.Extensions.Options.IOptions<JsonHubProtocolOptions>>()?.Value.PayloadSerializerOptions
                ?? new System.Text.Json.JsonSerializerOptions();
            Common.Serialization.SignalARRRJson.AddTo(options);
            return options;
        }
    }

    public class ServerRequestEventArgs : EventArgs {
        public ServerRequestMessage ServerRequestMessage { get; }

        public ServerRequestEventArgs(ServerRequestMessage serverRequestMessage) {
            ServerRequestMessage = serverRequestMessage;
        }

    }
}