using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// Base class for SignalARRR hubs. Extends standard SignalR Hub with enhanced features:
    /// - Method organization via ServerMethods&lt;T&gt;
    /// - Bidirectional RPC (server can call client and await response)
    /// - Enhanced ClientContext with authentication tracking
    /// - Interface-based method registration
    /// - HTTP stream references for large file transfers
    /// </summary>
    /// <remarks>
    /// HARRR hubs are fully backward compatible with standard SignalR clients.
    /// Clients can use either HARRRConnection or standard HubConnection to connect.
    /// </remarks>
    public abstract class HARRR : Hub {

        private IHARRRClientManager ClientManager { get; }
        private ISignalARRRMethodsCollection MethodsCollection { get; }
        private ISignalARRRInterfaceCollection InterfaceCollection { get; }

        /// <summary>
        /// Gets the service provider for dependency injection.
        /// </summary>
        protected IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Gets or sets the logger instance. Defaults to NullLogger if not set.
        /// </summary>
        public ILogger Logger { get; set; }

        private ClientContext? _clientContext;

        /// <summary>
        /// Gets the enhanced client context for the current connection.
        /// Provides access to client IP, user claims, authentication state, and custom attributes.
        /// </summary>
        public ClientContext ClientContext {
            get => _clientContext ?? ClientManager.GetClient(Context.ConnectionId);
            set => _clientContext = value;
        }

        /// <summary>
        /// Initializes a new instance of the HARRR hub.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        protected HARRR(IServiceProvider serviceProvider) {
            ServiceProvider = serviceProvider;

            ClientManager = serviceProvider.GetRequiredService<IHARRRClientManager>();

            // Nothing else ever assigned this, so every Logger.LogError/LogDebug in this class wrote
            // to NullLogger: invocation failures, connect/disconnect failures and stream write errors
            // were all invisible unless the consumer happened to set Logger themselves.
            Logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(GetType()) ?? NullLogger.Instance;

            // Get hub-specific method collections registered via AddSignalARRR
            MethodsCollection = serviceProvider.GetKeyedService<ISignalARRRMethodsCollection>(GetType().FullName)
                ?? new SignalARRRMethodsCollection(ServerWireSlots.Policy);

            InterfaceCollection = serviceProvider.GetKeyedService<ISignalARRRInterfaceCollection>(GetType().FullName)
                ?? new SignalARRRInterfaceCollection(ServerWireSlots.Policy);
        }

        /// <summary>
        /// Called when a new connection is established to the hub.
        /// Registers the client in the ClientManager and initializes the ClientContext.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public override async Task OnConnectedAsync() {
            try {
                var totalStopwatch = Stopwatch.StartNew();

                var registerStopwatch = Stopwatch.StartNew();
                var client = ClientManager.Register(this, Context);
                registerStopwatch.Stop();

                var connectionRegistry = ServiceProvider.GetRequiredService<ISignalARRRConnectionRegistry>();
                var registryType = connectionRegistry.GetType().Name;
                SignalARRRDiagnostics.Write(
                    "ConnectionLifecycle",
                    $"OnConnectedAsync register-local hub={GetType().Name} connectionId={Context.ConnectionId} elapsedMs={registerStopwatch.ElapsedMilliseconds} registry={registryType}");

                var registryStopwatch = Stopwatch.StartNew();
                await connectionRegistry.RegisterConnectionAsync(client).ConfigureAwait(false);
                registryStopwatch.Stop();
                SignalARRRDiagnostics.Write(
                    "ConnectionLifecycle",
                    $"OnConnectedAsync register-registry hub={GetType().Name} connectionId={Context.ConnectionId} elapsedMs={registryStopwatch.ElapsedMilliseconds} registry={registryType}");

                var baseStopwatch = Stopwatch.StartNew();
                await base.OnConnectedAsync().ConfigureAwait(false);
                baseStopwatch.Stop();
                totalStopwatch.Stop();
                SignalARRRDiagnostics.Write(
                    "ConnectionLifecycle",
                    $"OnConnectedAsync completed hub={GetType().Name} connectionId={Context.ConnectionId} elapsedMs={totalStopwatch.ElapsedMilliseconds} baseElapsedMs={baseStopwatch.ElapsedMilliseconds} registry={registryType}");

                if (Logger.IsEnabled(LogLevel.Debug)) {
                    Logger.LogDebug(
                        "HARRR '{HubName}' connected - {ClientIp} (ConnectionId: {ConnectionId})",
                        GetType().Name,
                        client.RemoteIp,
                        Context.ConnectionId);
                }
            } catch (Exception ex) {
                Logger.LogError(
                    ex,
                    "Error in HARRR '{HubName}' on OnConnectedAsync - {ClientIp} (ConnectionId: {ConnectionId})",
                    GetType().Name,
                    Context.GetHttpContext()?.Connection.RemoteIpAddress,
                    Context.ConnectionId);
                throw;
            }
        }

        /// <summary>
        /// Called when a connection to the hub is terminated.
        /// Unregisters the client from the ClientManager.
        /// </summary>
        /// <param name="exception">The exception that caused the disconnect, if any.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public override async Task OnDisconnectedAsync(Exception? exception) {
            try {
                var totalStopwatch = Stopwatch.StartNew();

                // Anything this connection was still streaming to us can never complete now. Without
                // this the channel is never completed and the server task awaiting it stays parked
                // for the process lifetime, holding the channel and everything buffered in it.
                ServiceProvider.GetRequiredService<ServerStreamManager>()
                    .CompleteStreamsFor(Context.ConnectionId, "The client disconnected while streaming.");

                var unregisterStopwatch = Stopwatch.StartNew();
                var client = ClientManager.UnRegister(Context.ConnectionId);
                unregisterStopwatch.Stop();

                var connectionRegistry = ServiceProvider.GetRequiredService<ISignalARRRConnectionRegistry>();
                var registryType = connectionRegistry.GetType().Name;
                SignalARRRDiagnostics.Write(
                    "ConnectionLifecycle",
                    $"OnDisconnectedAsync unregister-local hub={GetType().Name} connectionId={Context.ConnectionId} elapsedMs={unregisterStopwatch.ElapsedMilliseconds} registry={registryType}");

                var registryStopwatch = Stopwatch.StartNew();
                await connectionRegistry.UnregisterConnectionAsync(Context.ConnectionId).ConfigureAwait(false);
                registryStopwatch.Stop();
                SignalARRRDiagnostics.Write(
                    "ConnectionLifecycle",
                    $"OnDisconnectedAsync unregister-registry hub={GetType().Name} connectionId={Context.ConnectionId} elapsedMs={registryStopwatch.ElapsedMilliseconds} registry={registryType}");

                var baseStopwatch = Stopwatch.StartNew();
                await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
                baseStopwatch.Stop();
                totalStopwatch.Stop();
                SignalARRRDiagnostics.Write(
                    "ConnectionLifecycle",
                    $"OnDisconnectedAsync completed hub={GetType().Name} connectionId={Context.ConnectionId} elapsedMs={totalStopwatch.ElapsedMilliseconds} baseElapsedMs={baseStopwatch.ElapsedMilliseconds} registry={registryType} exception={(exception == null ? "none" : exception.GetType().Name)}");

                if (Logger.IsEnabled(LogLevel.Debug)) {
                    if (exception != null) {
                        Logger.LogDebug(
                            exception,
                            "HARRR '{HubName}' disconnected with error - {ClientIp} (ConnectionId: {ConnectionId})",
                            GetType().Name,
                            client.RemoteIp,
                            Context.ConnectionId);
                    } else {
                        Logger.LogDebug(
                            "HARRR '{HubName}' disconnected - {ClientIp} (ConnectionId: {ConnectionId})",
                            GetType().Name,
                            client.RemoteIp,
                            Context.ConnectionId);
                    }
                }
            } catch (Exception ex) {
                Logger.LogError(
                    ex,
                    "Error in HARRR '{HubName}' on OnDisconnectedAsync - ConnectionId: {ConnectionId}",
                    GetType().Name,
                    Context.ConnectionId);
                throw;
            }
        }

        /// <summary>
        /// Invokes a hub method without returning a result (fire-and-forget).
        /// Used internally by SignalARRR protocol for void methods.
        /// </summary>
        /// <param name="clientMessage">The client request message containing method name and arguments.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task InvokeMessage(ClientRequestMessage clientMessage) {
            try {
                if (Logger.IsEnabled(LogLevel.Debug)) {
                    Logger.LogDebug(
                        "InvokeMessage: {Method} from ConnectionId: {ConnectionId}",
                        clientMessage.Method,
                        Context.ConnectionId);
                }

                var messageHandler = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider, InterfaceCollection);
                await messageHandler.InvokeAsync(clientMessage).ConfigureAwait(false);

            } catch (Exception ex) {
                Logger.LogError(
                    ex,
                    "Error invoking message '{Method}' from ConnectionId: {ConnectionId}",
                    clientMessage.Method,
                    Context.ConnectionId);
                throw new HARRRException(ex);
            }
        }

        /// <summary>
        /// Invokes a hub method and returns the result.
        /// Used internally by SignalARRR protocol for methods with return values.
        /// </summary>
        /// <param name="clientMessage">The client request message containing method name and arguments.</param>
        /// <returns>The result of the method invocation.</returns>
        public async Task<object> InvokeMessageResult(ClientRequestMessage clientMessage) {
            try {
                if (Logger.IsEnabled(LogLevel.Debug)) {
                    Logger.LogDebug(
                        "InvokeMessageResult: {Method} from ConnectionId: {ConnectionId}",
                        clientMessage.Method,
                        Context.ConnectionId);
                }

                var messageHandler = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider, InterfaceCollection);
                return await messageHandler.InvokeAsync(clientMessage).ConfigureAwait(false);

            } catch (Exception ex) {
                Logger.LogError(
                    ex,
                    "Error invoking message result '{Method}' from ConnectionId: {ConnectionId}",
                    clientMessage.Method,
                    Context.ConnectionId);
                throw new HARRRException(ex);
            }
        }

        /// <summary>
        /// Sends a message to invoke a hub method without waiting for completion (fire-and-forget).
        /// Used internally by SignalARRR protocol for async void methods.
        /// </summary>
        /// <param name="clientMessage">The client request message containing method name and arguments.</param>
        /// <returns>A task that completes when the target method has run. The client does not await a result.</returns>
        public async Task SendMessage(ClientRequestMessage clientMessage) {
            try {
                if (Logger.IsEnabled(LogLevel.Debug)) {
                    Logger.LogDebug(
                        "SendMessage: {Method} from ConnectionId: {ConnectionId}",
                        clientMessage.Method,
                        Context.ConnectionId);
                }

                var messageHandler = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider, InterfaceCollection);

                // Await within the hub invocation so the Hub instance (and its Context/Clients/Groups,
                // which MessageHandler injects into the server method) stays alive for the duration of
                // the call. The previous Task.Run fire-and-forget ran after SignalR had already disposed
                // the Hub, so any Context/Groups access (e.g. group joins) silently failed.
                // The client-side `send` is already non-blocking at the SignalR layer (no invocationId),
                // so awaiting here is transparent to the caller — it just doesn't return a result.
                await messageHandler.InvokeAsync(clientMessage).ConfigureAwait(false);

            } catch (Exception ex) {
                Logger.LogError(
                    ex,
                    "Error sending message '{Method}' from ConnectionId: {ConnectionId}",
                    clientMessage.Method,
                    Context.ConnectionId);
                throw new HARRRException(ex);
            }
        }

        /// <summary>
        /// Invokes a streaming hub method that returns an async enumerable.
        /// Used internally by SignalARRR protocol for streaming operations.
        /// Supports IAsyncEnumerable&lt;T&gt;, ChannelReader&lt;T&gt;, and IObservable&lt;T&gt;.
        /// </summary>
        /// <param name="clientMessage">The client request message containing method name and arguments.</param>
        /// <param name="cancellationToken">Cancellation token to stop the stream.</param>
        /// <returns>An async enumerable stream of results.</returns>
        public async Task<IAsyncEnumerable<object>> StreamMessage(ClientRequestMessage clientMessage, CancellationToken cancellationToken) {
            try {
                if (Logger.IsEnabled(LogLevel.Debug)) {
                    Logger.LogDebug(
                        "StreamMessage: {Method} from ConnectionId: {ConnectionId}",
                        clientMessage.Method,
                        Context.ConnectionId);
                }

                var messageHandler = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider, InterfaceCollection);
                return await messageHandler.InvokeStreamAsync(clientMessage, cancellationToken).ConfigureAwait(false);

            } catch (Exception ex) {
                Logger.LogError(
                    ex,
                    "Error streaming message '{Method}' from ConnectionId: {ConnectionId}",
                    clientMessage.Method,
                    Context.ConnectionId);
                throw new HARRRException(ex);
            }
        }

        /// <summary>
        /// Receives a single stream item from the client for a server-initiated stream request.
        /// </summary>
        /// <param name="streamId">The stream correlation identifier.</param>
        /// <param name="item">The streamed item.</param>
        public async Task StreamItemToServer(Guid streamId, object item) {
            try {
                var streamManager = ServiceProvider.GetRequiredService<ServerStreamManager>();

                // Awaited, so a full buffer throttles this connection instead of letting it grow the
                // heap. The connection id is passed so that only the owner can feed the stream.
                if (!await streamManager.WriteItemAsync(streamId, item, Context.ConnectionId, Context.ConnectionAborted).ConfigureAwait(false)) {
                    Logger.LogWarning(
                        "Rejected a stream item for StreamId {StreamId} from connection {ConnectionId}: the stream is unknown, already finished, or owned by another connection.",
                        streamId, Context.ConnectionId);
                }
            } catch (Exception ex) {
                Logger.LogError(ex, "Error writing stream item for StreamId: {StreamId}", streamId);
            }
        }

        /// <summary>
        /// Signals completion of a client-to-server stream.
        /// </summary>
        /// <param name="streamId">The stream correlation identifier.</param>
        /// <param name="error">Optional error message if the stream failed.</param>
        public void StreamCompleteToServer(Guid streamId, string? error = null) {
            try {
                var streamManager = ServiceProvider.GetRequiredService<ServerStreamManager>();

                if (!streamManager.CompleteStream(streamId, Context.ConnectionId, error)) {
                    Logger.LogWarning(
                        "Rejected a stream completion for StreamId {StreamId} from connection {ConnectionId}: the stream is unknown, already finished, or owned by another connection.",
                        streamId, Context.ConnectionId);
                }
            } catch (Exception ex) {
                Logger.LogError(ex, "Error completing stream for StreamId: {StreamId}", streamId);
            }
        }

        /// <summary>
        /// Called by clients to request an upload URL for sending a Stream to the server.
        /// The client uploads the stream data via HTTP POST to the returned URL,
        /// then sends a StreamReference with that URL as a return value or argument.
        /// </summary>
        /// <returns>The upload URL where the client should POST the stream data.</returns>
        public string RequestUploadSlot() {
            var streamManager = ServiceProvider.GetRequiredService<ServerPushStreamManager>();
            return streamManager.CreateUploadSlot(ClientContext.ConnectedTo);
        }

    }
}
