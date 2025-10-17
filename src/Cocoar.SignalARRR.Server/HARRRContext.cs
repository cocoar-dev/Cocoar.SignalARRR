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

        //public Task ProxyClientAsync(string clientId, ServerRequestMessage serverRequestMessage, HttpContext httpContext) {


        //    return ProxyClientMessageAsync(clientId, MethodNames.InvokeServerRequest, serverRequestMessage, httpContext);

        //}

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


            //var m = ServerRequestManager.AddRequest(serverRequestMessage.Id);
            //await HubContext.Clients.Client(clientId).SendCoreAsync(methodName, new[] { serverRequestMessage }, cancellationToken);
            return await HubContext.Clients.Client(clientId).InvokeCoreAsync<TResult>(methodName, new[] { serverRequestMessage }, cancellationToken);
            
            //await Task.Run(() => {
            //    try {
            //        Task.WaitAny(new Task[] { m.Task }, cancellationToken);
            //    } catch (Exception) {
            //        ServerRequestManager.CancelRequest(serverRequestMessage.Id);
            //        throw;
            //    }
            //});


            //var jToken = await m.Task;
            ////var z = jToken.ToString();
            ////var t = jToken.ToObject<TResult>();
            //return Json.Converter.ToObject<TResult>(jToken);

        }

        //internal async Task ProxyClientMessageAsync(string clientId, string methodName, ServerRequestMessage serverRequestMessage, HttpContext httpContext) {


        //    var m = ServerRequestManager.AddProxyRequest(serverRequestMessage.Id, httpContext);
        //    await HubContext.Clients.Client(clientId).SendCoreAsync(methodName, new[] { serverRequestMessage }, httpContext.RequestAborted);


        //    await Task.Run(() => {
        //        try {
        //            Task.WaitAny(new Task[] { m.Task }, httpContext.RequestAborted);
        //        } catch (Exception) {
        //            ServerRequestManager.CancelRequest(serverRequestMessage.Id);
        //            throw;
        //        }
        //    });

        //    //await m.Task;
        //}


        //private TResult Deserialize<TResult>(Stream s) {
        //    using (StreamReader reader = new StreamReader(s))
        //    using (JsonTextReader jsonReader = new JsonTextReader(reader)) {
        //        JsonSerializer ser = new JsonSerializer();
        //        return ser.Deserialize<TResult>(jsonReader);
        //    }
        //}

        internal async Task SendClientMessageAsync(string clientId, string methodName, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken) {


            //var m = ServerRequestManager.AddRequest(serverRequestMessage.Id);
            await HubContext.Clients.Client(clientId).SendCoreAsync(methodName, new[] { serverRequestMessage }, cancellationToken);
            
        }


    }


}
