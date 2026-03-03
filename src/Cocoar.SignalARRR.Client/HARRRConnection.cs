using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Cocoar.SignalARRR.Client.ExtensionMethods;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cocoar.SignalARRR.Client {
    public partial class HARRRConnection {
        private HubConnection HubConnection { get; }
        private ConcurrentDictionary<string, Delegate> ServerRequestHandlers { get; } = new ConcurrentDictionary<string, Delegate>();
        private HARRRContext _harrrContext { get; }



        public HARRRConnection(HARRRContext harrrContext) {

            _harrrContext = harrrContext;
            HubConnection = harrrContext.GetHubConnection();


            this.On<ServerRequestMessage>(MethodNames.ChallengeAuthentication, (requestMessage) => {
                return _harrrContext.MessageHandler.ChallengeAuthentication(requestMessage);
            });

            this.On<ServerRequestMessage>(MethodNames.CancelTokenFromServer, (requestMessage) => _harrrContext.MessageHandler.CancelTokenFromServer(requestMessage));

#pragma warning disable 4014
            this.On<ServerRequestMessage>(MethodNames.InvokeServerRequest,
                 (requestMessage) => {
                     OnServerRequestMessage?.Invoke(null, new ServerRequestEventArgs(requestMessage));
                     _harrrContext.MessageHandler.InvokeServerRequest(requestMessage);
                 });

            this.On<ServerRequestMessage>(MethodNames.InvokeServerMessage,
                 (requestMessage) => {
                     OnServerRequestMessage?.Invoke(null, new ServerRequestEventArgs(requestMessage));
                     _harrrContext.MessageHandler.InvokeServerMessage(requestMessage);

                 });
#pragma warning restore 4014
        }


        public T GetTypedMethods<T>() where T : class {
            var instance = ProxyCreator.CreateInstanceFromInterface<T>(new ClientProxyCreatorHelper(this));
            return instance;
        }

        public IDisposable On(string methodName, Type[] parameterTypes, Func<object?[], object, Task> handler, object state) {
            return HubConnection.On(methodName, parameterTypes, handler, state);
        }

        public void OnServerRequest(string methodName, Delegate handler) {

            ServerRequestHandlers.TryAdd(methodName, handler);
        }

        public void OnServerRequest(string methodName, Func<object, object> handler) {

            OnServerRequest<object>(methodName, handler);
        }
        public void OnServerRequest<TIn>(string methodName, Func<TIn, object> handler) {

            ServerRequestHandlers.TryAdd(methodName, handler);
        }

        public void OnServerRequest<TIn1, TIn2>(string methodName, Func<TIn1, TIn2, object> handler) {

            ServerRequestHandlers.TryAdd(methodName, handler);
        }

        public void OnServerRequest<TIn1, TIn2, TIn3>(string methodName, Func<TIn1, TIn2, TIn3, object> handler) {

            ServerRequestHandlers.TryAdd(methodName, handler);
        }

        public void OnServerRequest<TIn1, TIn2, TIn3, TIn4>(string methodName, Func<TIn1, TIn2, TIn3, TIn4, object> handler) {

            ServerRequestHandlers.TryAdd(methodName, handler);
        }

        public async Task<object> InvokeCoreAsync(ClientRequestMessage message, Type returnType, CancellationToken cancellationToken = default) {
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider);
            return await HubConnection.InvokeCoreAsync(MethodNames.InvokeMessageResultOnServer, returnType, new object[] { message }, cancellationToken) ?? null!;
        }

        public async Task InvokeCoreAsync(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider);
            await HubConnection.InvokeCoreAsync(MethodNames.InvokeMessageOnServer, new object[] { message }, cancellationToken);
        }

        public async Task<object> InvokeCoreAsync(string methodName, Type returnType, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider);
            return await HubConnection.InvokeCoreAsync(MethodNames.InvokeMessageResultOnServer, returnType, new object[] { msg }, cancellationToken) ?? null!;
        }

        public async Task InvokeCoreAsync(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider);
            await HubConnection.InvokeCoreAsync(MethodNames.InvokeMessageOnServer, new object[] { msg }, cancellationToken);
        }

        public async Task<TResult> InvokeCoreAsync<TResult>(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider);
            var resultMsg = await HubConnection.InvokeCoreAsync<TResult>(MethodNames.InvokeMessageResultOnServer, new object[] { message }, cancellationToken);
            return resultMsg;
        }
        public async Task<TResult> InvokeCoreAsync<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider);
            var resultMsg = await HubConnection.InvokeCoreAsync<TResult>(MethodNames.InvokeMessageResultOnServer, new object[] { msg }, cancellationToken);
            return resultMsg;
        }

        public Task SendCoreAsync(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider);
            return HubConnection.SendCoreAsync(MethodNames.SendMessageToServer, new object[] { message }, cancellationToken);
        }

        public Task SendCoreAsync(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider);
            return HubConnection.SendCoreAsync(MethodNames.SendMessageToServer, new object[] { msg }, cancellationToken);
        }

        public IAsyncEnumerable<TResult> StreamAsyncCore<TResult>(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider);
            return HubConnection.StreamAsyncCore<TResult>(MethodNames.StreamMessageFromServer, new object[] { message }, cancellationToken);
        }

        public IAsyncEnumerable<TResult> StreamAsyncCore<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider);
            return HubConnection.StreamAsyncCore<TResult>(MethodNames.StreamMessageFromServer, new object[] { msg }, cancellationToken);
        }

        public async Task<ChannelReader<object>> StreamAsChannelCoreAsync(string methodName, Type returnType, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider);
            return (await HubConnection.StreamAsChannelCoreAsync(MethodNames.StreamMessageFromServer, returnType, new object[] { msg }, cancellationToken))!;
        }

        public async Task<ChannelReader<TResult>> StreamAsChannelCoreAsync<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider);
            return await HubConnection.StreamAsChannelCoreAsync<TResult>(MethodNames.StreamMessageFromServer, new object[] { msg }, cancellationToken);
        }




        public HubConnection AsSignalRHubConnection() {
            return HubConnection;
        }

        public static HARRRConnection Create(Action<HubConnectionBuilder> builder, Action<HARRRConnectionOptionsBuilder>? optionsBuilder = null) {
            var intermediateBuilder = builder.InvokeAction();
            var hubConnection = intermediateBuilder.Build();
            return Create(hubConnection, optionsBuilder);
        }

        public static HARRRConnection Create(HubConnection hubConnection, Action<HARRRConnectionOptionsBuilder>? optionsBuilder = null) {
            var harrrContext = new HARRRContext(hubConnection.GetServiceProvider(), optionsBuilder?.InvokeAction() ?? new HARRRConnectionOptionsBuilder());
            return new HARRRConnection(harrrContext);
        }

        #region HubConnectionDecorator

        public event Func<Exception, Task> Closed {
            add => HubConnection.Closed += value;
            remove => HubConnection.Closed -= value;
        }

        public event Func<Exception, Task> Reconnecting {
            add => HubConnection.Reconnecting += value;
            remove => HubConnection.Reconnecting -= value;
        }

        public event Func<string, Task> Reconnected {
            add => HubConnection.Reconnected += value;
            remove => HubConnection.Reconnected -= value;
        }

        public TimeSpan ServerTimeout {
            get => HubConnection.ServerTimeout;
            set => HubConnection.ServerTimeout = value;
        }

        public TimeSpan KeepAliveInterval {
            get => HubConnection.KeepAliveInterval;
            set => HubConnection.KeepAliveInterval = value;
        }

        public TimeSpan HandshakeTimeout {
            get => HubConnection.HandshakeTimeout;
            set => HubConnection.HandshakeTimeout = value;
        }

        public string? ConnectionId => HubConnection.ConnectionId;

        public HubConnectionState State => HubConnection.State;

        public Task StartAsync(CancellationToken cancellation = default) => HubConnection.StartAsync(cancellation);
        public Task StopAsync(CancellationToken cancellation = default) => HubConnection.StopAsync(cancellation);

        public ValueTask DisposeAsync() {
            return HubConnection.DisposeAsync();
        }

        #endregion

    }
}
