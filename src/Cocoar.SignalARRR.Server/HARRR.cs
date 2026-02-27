using System;
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
            Logger = NullLogger.Instance;

            // Get hub-specific method collections registered via AddSignalARRR
            MethodsCollection = serviceProvider.GetKeyedService<ISignalARRRMethodsCollection>(GetType().FullName) 
                ?? new SignalARRRMethodsCollection();

            InterfaceCollection = serviceProvider.GetKeyedService<ISignalARRRInterfaceCollection>(GetType().FullName) 
                ?? new SignalARRRInterfaceCollection();
        }

        /// <summary>
        /// Called when a new connection is established to the hub.
        /// Registers the client in the ClientManager and initializes the ClientContext.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public override async Task OnConnectedAsync() {
            try {
                var client = ClientManager.Register(this, Context);

                await base.OnConnectedAsync().ConfigureAwait(false);
                
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
                var client = ClientManager.UnRegister(Context.ConnectionId);
                
                await base.OnDisconnectedAsync(exception).ConfigureAwait(false);

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
        /// <returns>A completed task.</returns>
        public Task SendMessage(ClientRequestMessage clientMessage) {
            try {
                if (Logger.IsEnabled(LogLevel.Debug)) {
                    Logger.LogDebug(
                        "SendMessage: {Method} from ConnectionId: {ConnectionId}", 
                        clientMessage.Method, 
                        Context.ConnectionId);
                }

                var messageHandler = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider, InterfaceCollection);
                
                // Fire and forget - don't await
                _ = Task.Run(async () => {
                    try {
                        await messageHandler.InvokeAsync(clientMessage).ConfigureAwait(false);
                    } catch (Exception ex) {
                        Logger.LogError(
                            ex, 
                            "Error in fire-and-forget message '{Method}' from ConnectionId: {ConnectionId}", 
                            clientMessage.Method, 
                            Context.ConnectionId);
                    }
                });
                
                return Task.CompletedTask;
                
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
        public void StreamItemToServer(Guid streamId, object item) {
            try {
                var streamManager = ServiceProvider.GetRequiredService<ServerStreamManager>();
                streamManager.WriteItem(streamId, item);
            } catch (Exception ex) {
                Logger.LogError(ex, "Error writing stream item for StreamId: {StreamId}", streamId);
            }
        }

        /// <summary>
        /// Signals completion of a client-to-server stream.
        /// </summary>
        /// <param name="streamId">The stream correlation identifier.</param>
        /// <param name="error">Optional error message if the stream failed.</param>
        public void StreamCompleteToServer(Guid streamId, string error = null) {
            try {
                var streamManager = ServiceProvider.GetRequiredService<ServerStreamManager>();
                streamManager.CompleteStream(streamId, error);
            } catch (Exception ex) {
                Logger.LogError(ex, "Error completing stream for StreamId: {StreamId}", streamId);
            }
        }

        // Note: ReplyServerRequest hub method was removed during ASP.NET Core 3.x → .NET 8 migration.
        // 
        // In the old implementation (SignalR Core 1.x/2.x), server-to-client RPC required a workaround:
        //   1. Server sent InvokeServerRequest message to client
        //   2. Client processed the request
        //   3. Client called ReplyServerRequest hub method with the result
        //   4. ServerRequestManager completed the TaskCompletionSource
        // 
        // SignalR Core 3.0+ added InvokeCoreAsync, which handles bidirectional RPC natively.
        // The server can now directly await client responses without HTTP POST workarounds.
        // See HARRRContext.InvokeClientMessageAsync for the modern implementation using InvokeCoreAsync.
        // 
        // Bidirectional RPC remains fully functional via ClientContext.GetTypedClient<T>().
    }
}
