using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    internal class ClientContextDispatcher<T> : IClientContextDispatcher where T : HARRR {

        private IHubContext<T> HubContext { get; }
        private ILogger<ClientContextDispatcher<T>> Logger { get; }

        public ClientContextDispatcher(IHubContext<T> hubContext, ILogger<ClientContextDispatcher<T>> logger) {
            HubContext = hubContext;
            Logger = logger;
        }

        public Task<TResult> InvokeClientAsync<TResult>(string clientId, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {
            return InvokeClientMessageAsync<TResult>(clientId, MethodNames.InvokeServerRequest, serverRequestMessage, cancellationToken);
        }

        public Task SendClientAsync(string clientId, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {
            return SendClientMessageAsync(clientId, MethodNames.InvokeServerMessage, serverRequestMessage, cancellationToken);
        }

        public async Task<string> Challenge(string clientId) {
            var msg = new ServerRequestMessage(MethodNames.ChallengeAuthentication);
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try {
                return await InvokeClientMessageAsync<string>(clientId, MethodNames.ChallengeAuthentication, msg, ct.Token);
            } catch (Exception e) {
                // The caller decides what a failed challenge means (it is an authentication
                // decision), so this only records context and rethrows.
                Logger.LogDebug(e, "Authentication challenge to client {ConnectionId} failed.", clientId);
                throw;
            }
        }

        public async Task CancelToken(string clientId, Guid id) {
            var msg = new ServerRequestMessage(MethodNames.CancelTokenFromServer) {
                CancellationGuid = id
            };

            try {
                await SendClientMessageAsync(clientId, MethodNames.CancelTokenFromServer, msg, CancellationToken.None);
            } catch (Exception e) {
                // Best effort by design: this is a one-way notification raised from a cancellation
                // callback, so there is no caller left to handle a rethrow. Failing here normally
                // just means the client already disconnected -- which is why the token fired.
                Logger.LogDebug(e, "Could not notify client {ConnectionId} about cancellation of token {TokenId}.", clientId, id);
            }
        }

        internal async Task<TResult> InvokeClientMessageAsync<TResult>(string clientId, string methodName, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {
            using var activity = SignalARRRServerTelemetry.StartClientCall(clientId, serverRequestMessage);
            try {
                // Uses SignalR's native client results — the client handler returns the value directly
                return await HubContext.Clients.Client(clientId).InvokeCoreAsync<TResult>(methodName, new object[] { serverRequestMessage }, cancellationToken);
            } catch (HubException hubException) when (hubException is not Common.Exceptions.HARRRRemoteException) {
                RecordFailure(activity, hubException);
                // Same structured type as the backplane path rehydrates, so single-node and
                // multi-node report a failed client call identically.
                throw Common.Exceptions.HARRRRemoteException.FromReceived(hubException);
            } catch (Exception ex) {
                RecordFailure(activity, ex);
                throw;
            }
        }

        internal async Task SendClientMessageAsync(string clientId, string methodName, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {
            using var activity = SignalARRRServerTelemetry.StartClientCall(clientId, serverRequestMessage);
            try {
                await HubContext.Clients.Client(clientId).SendCoreAsync(methodName, new object[] { serverRequestMessage }, cancellationToken);
            } catch (Exception ex) {
                RecordFailure(activity, ex);
                throw;
            }
        }

        private static void RecordFailure(System.Diagnostics.Activity? activity, Exception exception) {
            // A cancelled call is the caller's choice, not a failure of the client.
            if (activity != null && exception is not OperationCanceledException) {
                activity.SetStatus(System.Diagnostics.ActivityStatusCode.Error, exception.Message);
                activity.SetTag("error.type", exception.GetType().FullName);
            }
        }
    }
}
