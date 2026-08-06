using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Cocoar.SignalARRR.Client.ExtensionMethods;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cocoar.SignalARRR.Client {
    public partial class HARRRConnection {
        private HubConnection HubConnection { get; }
        private HARRRContext _harrrContext { get; }



        public HARRRConnection(HARRRContext harrrContext) {

            _harrrContext = harrrContext;
            HubConnection = harrrContext.GetHubConnection();


            // Native client results — return values are sent back to the server automatically by SignalR
            HubConnection.On<ServerRequestMessage, string?>(MethodNames.ChallengeAuthentication,
                (requestMessage) => _harrrContext.MessageHandler.ChallengeAuthentication(requestMessage));

            HubConnection.On<ServerRequestMessage, object?>(MethodNames.InvokeServerRequest,
                async (requestMessage) => {
                    OnServerRequestMessage?.Invoke(null, new ServerRequestEventArgs(requestMessage));
                    return await _harrrContext.MessageHandler.InvokeServerRequest(requestMessage);
                });

            // Fire-and-forget — no return value
            this.On<ServerRequestMessage>(MethodNames.CancelTokenFromServer, (requestMessage) => _harrrContext.MessageHandler.CancelTokenFromServer(requestMessage));

            this.On<ServerRequestMessage>(MethodNames.InvokeServerMessage,
                 async (requestMessage) => {
                     OnServerRequestMessage?.Invoke(null, new ServerRequestEventArgs(requestMessage));
                     await _harrrContext.MessageHandler.InvokeServerMessage(requestMessage);
                 });
        }


        public T GetTypedMethods<T>() where T : class {
            var instance = ProxyCreator.CreateInstanceFromInterface<T>(new ClientProxyCreatorHelper(this));
            return instance;
        }

        public IDisposable On(string methodName, Type[] parameterTypes, Func<object?[], object, Task> handler, object state) {
            return HubConnection.On(methodName, parameterTypes, handler, state);
        }

        public async Task<object> InvokeCoreAsync(ClientRequestMessage message, Type returnType, CancellationToken cancellationToken = default) {
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider).WithInvocationId();
            using var activity = SignalARRRClientTelemetry.StartOutgoingCall(message);
            try {
                return await HubConnection.InvokeCoreAsync(MethodNames.InvokeMessageResultOnServer, returnType, new object[] { message }, cancellationToken) ?? null!;
            } catch (HubException hubException) when (hubException is not HARRRRemoteException) {
                SignalARRRClientTelemetry.RecordFailure(activity, hubException);
                throw HARRRRemoteException.FromReceived(hubException);
            } catch (Exception ex) {
                SignalARRRClientTelemetry.RecordFailure(activity, ex);
                throw;
            }
        }

        public async Task InvokeCoreAsync(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider).WithInvocationId();
            using var activity = SignalARRRClientTelemetry.StartOutgoingCall(message);
            try {
                await HubConnection.InvokeCoreAsync(MethodNames.InvokeMessageOnServer, new object[] { message }, cancellationToken);
            } catch (HubException hubException) when (hubException is not HARRRRemoteException) {
                SignalARRRClientTelemetry.RecordFailure(activity, hubException);
                throw HARRRRemoteException.FromReceived(hubException);
            } catch (Exception ex) {
                SignalARRRClientTelemetry.RecordFailure(activity, ex);
                throw;
            }
        }

        public Task<object> InvokeCoreAsync(string methodName, Type returnType, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args);
            return InvokeCoreAsync(msg, returnType, cancellationToken);
        }

        public Task InvokeCoreAsync(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args);
            return InvokeCoreAsync(msg, cancellationToken);
        }

        public async Task<TResult> InvokeCoreAsync<TResult>(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            await PrepareStreamArguments(message);
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider).WithInvocationId();
            using var activity = SignalARRRClientTelemetry.StartOutgoingCall(message);
            try {
                var resultMsg = await HubConnection.InvokeCoreAsync<TResult>(MethodNames.InvokeMessageResultOnServer, new object[] { message }, cancellationToken);
                return resultMsg;
            } catch (HubException hubException) when (hubException is not HARRRRemoteException) {
                SignalARRRClientTelemetry.RecordFailure(activity, hubException);
                throw HARRRRemoteException.FromReceived(hubException);
            } catch (Exception ex) {
                SignalARRRClientTelemetry.RecordFailure(activity, ex);
                throw;
            }
        }
        public Task<TResult> InvokeCoreAsync<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args);
            return InvokeCoreAsync<TResult>(msg, cancellationToken);
        }

        public async Task SendCoreAsync(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            await PrepareStreamArguments(message);
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider).WithInvocationId();
            using var activity = SignalARRRClientTelemetry.StartOutgoingCall(message);
            try {
                await HubConnection.SendCoreAsync(MethodNames.SendMessageToServer, new object[] { message }, cancellationToken);
            } catch (HubException hubException) when (hubException is not HARRRRemoteException) {
                SignalARRRClientTelemetry.RecordFailure(activity, hubException);
                throw HARRRRemoteException.FromReceived(hubException);
            } catch (Exception ex) {
                SignalARRRClientTelemetry.RecordFailure(activity, ex);
                throw;
            }
        }

        public Task SendCoreAsync(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args);
            return SendCoreAsync(msg, cancellationToken);
        }

        public IAsyncEnumerable<TResult> StreamAsyncCore<TResult>(ClientRequestMessage message, CancellationToken cancellationToken = default) {
            // No span around a stream: it lives for as long as the consumer reads. The trace
            // context still travels with the message so the server span joins the caller's trace.
            message = message.WithAuthorization(_harrrContext.AccessTokenProvider).WithInvocationId().WithTraceContext();
            return HubConnection.StreamAsyncCore<TResult>(MethodNames.StreamMessageFromServer, new object[] { message }, cancellationToken);
        }

        public IAsyncEnumerable<TResult> StreamAsyncCore<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args);
            return StreamAsyncCore<TResult>(msg, cancellationToken);
        }

        public async Task<ChannelReader<object>> StreamAsChannelCoreAsync(string methodName, Type returnType, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider).WithInvocationId().WithTraceContext();
            return (await HubConnection.StreamAsChannelCoreAsync(MethodNames.StreamMessageFromServer, returnType, new object[] { msg }, cancellationToken))!;
        }

        public async Task<ChannelReader<TResult>> StreamAsChannelCoreAsync<TResult>(string methodName, object[] args, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, args).WithAuthorization(_harrrContext.AccessTokenProvider).WithInvocationId().WithTraceContext();
            return await HubConnection.StreamAsChannelCoreAsync<TResult>(MethodNames.StreamMessageFromServer, new object[] { msg }, cancellationToken);
        }




        /// <summary>
        /// Uploads any Stream arguments via HTTP and replaces them with StreamReferences.
        /// Called BEFORE the actual hub method invocation — no nested SignalR calls.
        /// </summary>
        private async Task PrepareStreamArguments(ClientRequestMessage message) {
            if (message.Arguments == null || message.Arguments.Length == 0) return;

            bool hasStream = false;
            for (int i = 0; i < message.Arguments.Length; i++) {
                if (message.Arguments[i] is System.IO.Stream) {
                    hasStream = true;
                    break;
                }
            }
            if (!hasStream) return;

            var args = message.Arguments.ToList();
            for (int i = 0; i < args.Count; i++) {
                if (args[i] is System.IO.Stream stream) {
                    // Request upload URL
                    var uploadUrl = await HubConnection.InvokeCoreAsync<string>(
                        "RequestUploadSlot", System.Array.Empty<object>(), default);

                    // Upload via HTTP POST
                    using var httpClient = new System.Net.Http.HttpClient();
                    using var content = new System.Net.Http.StreamContent(stream);
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                    var response = await httpClient.PostAsync(uploadUrl, content);
                    response.EnsureSuccessStatusCode();

                    args[i] = new Common.RemoteReferenceTypes.StreamReference { Uri = uploadUrl };
                }
            }
            message.Arguments = args.ToArray();
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
