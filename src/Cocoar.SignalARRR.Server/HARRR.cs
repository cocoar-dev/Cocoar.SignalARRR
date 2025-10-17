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
using NamedServices.Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    public abstract class HARRR : Hub {

        private IHARRRClientManager ClientManager { get; }
        private ISignalARRRMethodsCollection MethodsCollection { get; }
        private ISignalARRRInterfaceCollection InterfaceCollection { get; }
        //private ServerRequestManager ServerRequestManager { get; }

        protected IServiceProvider ServiceProvider { get; }
        public ILogger Logger { get; set; }


        private ClientContext? _clientContext;
        public ClientContext ClientContext {
            get => _clientContext ?? ClientManager.GetClient(Context.ConnectionId);
            set => _clientContext = value;
        }

        protected HARRR(IServiceProvider serviceProvider) {
            ServiceProvider = serviceProvider;
            

            ClientManager = serviceProvider.GetRequiredService<IHARRRClientManager>();
            Logger =  NullLogger.Instance;

            MethodsCollection = serviceProvider.GetNamedService<ISignalARRRMethodsCollection>(this.GetType().FullName) ?? new SignalARRRMethodsCollection();
            //(ISignalARRRServerMethodsCollection)serviceProvider.GetRequiredService(
            //        typeof(SignalARRRServerMethodsCollection<>).MakeGenericType(this.GetType()));

            InterfaceCollection = serviceProvider.GetNamedService<ISignalARRRInterfaceCollection>(this.GetType().FullName) ?? new SignalARRRInterfaceCollection();

            //ServerRequestManager = serviceProvider.GetService<ServerRequestManager>();
        }

        public override async Task OnConnectedAsync() {
            try {


                var cl = ClientManager.Register(this, Context);

                await base.OnConnectedAsync().ConfigureAwait(false);
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("HARRR '{Name}' connected - {Ip}({ConnectionId})", this.GetType().Name, cl.RemoteIp, Context.ConnectionId);

            } catch (Exception ex) {
                Logger.LogError(ex, "Error in HARRR '{HubName}' on Method \"OnConnect()\" from  - {Ip}({ConnectionId})", this.GetType().Name);
            }
        }

        public override async Task OnDisconnectedAsync(Exception exception) {

            try {

                var cl = ClientManager.UnRegister(Context.ConnectionId);
                await base.OnDisconnectedAsync(exception).ConfigureAwait(false);

                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("HARRR {Name} disconnected - {Ip}({ConnectionId})", this.GetType().Name, cl.RemoteIp, Context.ConnectionId);

            } catch (Exception ex) {
                Logger.LogError(ex, "Error in HARRR '{HubName}' on Method \"OnDisconnected(bool stopCalled)\" from - {Ip}({ConnectionId})",
                    this.GetType().Name);
            }

        }



        public async Task InvokeMessage(ClientRequestMessage clientMessage) {
            try {
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("InvokeMessage {Method}", clientMessage.Method);

                var strHelper = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider, InterfaceCollection);


                await strHelper.InvokeAsync(clientMessage).ConfigureAwait(false);
            } catch (Exception e) {
                throw new HARRRException(e);
            }

        }

        public async Task<object> InvokeMessageResult(ClientRequestMessage clientMessage) {
            try {
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("InvokeMessageResult {Method}", clientMessage.Method);

                var strHelper = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider, InterfaceCollection);




                return await strHelper.InvokeAsync(clientMessage);
            } catch (Exception e) {

                throw new HARRRException(e);

            }

        }


        public Task SendMessage(ClientRequestMessage clientMessage) {

            try {
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("SendMessage {Method}", clientMessage.Method);

                var strHelper = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider, InterfaceCollection);
                _ = strHelper.InvokeAsync(clientMessage).ConfigureAwait(false);
                return Task.CompletedTask;
            } catch (Exception e) {
                throw new HARRRException(e);
            }

        }

        public async Task<IAsyncEnumerable<object>> StreamMessage(ClientRequestMessage clientMessage, CancellationToken cancellationToken) {

            try {
                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("StreamMessage {Method}", clientMessage.Method);

                var strHelper = new MessageHandler(this, ClientContext, MethodsCollection, ServiceProvider,InterfaceCollection);
                return await strHelper.InvokeStreamAsync(clientMessage, cancellationToken).ConfigureAwait(false);
            } catch (Exception e) {
                throw new HARRRException(e);
            }
        }

        //public async Task ReplyServerRequest(Guid id, object payload, string error) {

        //    var responseType = ServerRequestManager.GetResponseType(id);
        //    var jtoken = Json.Converter.ToJToken(payload);
        //    switch (responseType) {
        //        case RequestType.Default: {
                        
        //                ServerRequestManager.CompleteRequest(id, jtoken, error);
        //                return;
        //            }
        //        case RequestType.Proxy: {
        //                var httpContext = ServerRequestManager.GetHttpContext(id);

        //                if (error != null) {
        //                    await httpContext.BadRequest(error);
        //                } else {
        //                    await httpContext.Ok(jtoken);
        //                }
        //                ServerRequestManager.CompleteProxyRequest(id);
        //                return;
        //            }
        //    }


        //}


    }


}
