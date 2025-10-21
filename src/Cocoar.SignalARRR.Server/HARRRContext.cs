using System;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server {
    internal class ClientContextDispatcher<T> : IClientContextDispatcher where T : HARRR {


        private IHubContext<T> HubContext { get; }

        


        public ClientContextDispatcher(IHubContext<T> hubContext) {
            HubContext = hubContext;
        }



        public Task<TResult> InvokeClientAsync<TResult>(string clientId, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {
            return InvokeClientMessageAsync<TResult>(clientId, MethodNames.InvokeServerRequest, serverRequestMessage, cancellationToken);
        }

        public Task SendClientAsync(string clientId, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {
            return SendClientMessageAsync(clientId, MethodNames.InvokeServerMessage, serverRequestMessage, cancellationToken);
        }

        public async Task<string> Challenge(string clientId) {

            try {
                var msg = new ServerRequestMessage(MethodNames.ChallengeAuthentication);
                var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                return await InvokeClientMessageAsync<string>(clientId, MethodNames.ChallengeAuthentication, msg, ct.Token);
            } catch (Exception e) {
                Console.WriteLine(e);
                throw;
            }
            
        }

        public async Task CancelToken(string clientId, Guid id) {

            try {
                var msg = new ServerRequestMessage(MethodNames.CancelTokenFromServer);
                msg.CancellationGuid = id;
                await SendClientMessageAsync(clientId, MethodNames.CancelTokenFromServer, msg, CancellationToken.None);
            } catch (Exception e) {
                Console.WriteLine(e);
                throw;
            }

        }

        internal async Task<TResult> InvokeClientMessageAsync<TResult>(string clientId, string methodName, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {
            // Modern implementation using SignalR Core 3.0+ InvokeCoreAsync
            // This directly awaits the client's response without manual TaskCompletionSource management
            return await HubContext.Clients.Client(clientId).InvokeCoreAsync<TResult>(methodName, new[] { serverRequestMessage }, cancellationToken);
        }

        internal async Task SendClientMessageAsync(string clientId, string methodName, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {
            // Send message to client without awaiting a response (fire-and-forget)
            await HubContext.Clients.Client(clientId).SendCoreAsync(methodName, new[] { serverRequestMessage }, cancellationToken);
        }


    }


}
