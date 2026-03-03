using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    public static class ClientContextExtensions {

        public static async Task<ClientCollectionResult<TResult>> Invoke<TResult>(this ClientContext clientContext, string method, object[] arguments, CancellationToken cancellationToken) {

            using var serviceProviderScope = clientContext.ServiceProvider.CreateScope();

            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(clientContext.HARRRType);
            var harrrContext = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            var msg = new ServerRequestMessage(method, arguments);
            var res = await harrrContext.InvokeClientAsync<TResult>(clientContext.Id, msg, cancellationToken);
            return new ClientCollectionResult<TResult>(clientContext.Id, res);

        }

        public static async Task CancelToken(this ClientContext clientContext, Guid tokenReference) {

            using var serviceProviderScope = clientContext.ServiceProvider.CreateScope();

            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(clientContext.HARRRType);
            var harrrContext = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            var msg = new ServerRequestMessage(MethodNames.CancelTokenFromServer, tokenReference);

            await harrrContext.CancelToken(clientContext.Id, tokenReference);
        }

        public static async Task<IEnumerable<ClientCollectionResult<TResult>>> InvokeAllAsync<TResult>(this IEnumerable<ClientContext> clientContext, string method, object[] arguments, CancellationToken cancellationToken) {

            var tasks = new List<Task<ClientCollectionResult<TResult>>>();

            foreach (var context in clientContext) {
                tasks.Add(context.Invoke<TResult>(method, arguments, cancellationToken));
            }

            var result = await Task.WhenAll(tasks);

            return result;
        }

        public static async Task<ClientCollectionResult<TResult>> InvokeOneAsync<TResult>(this IEnumerable<ClientContext> clientContext, string method, object[] arguments, CancellationToken cancellationToken) {


            ClientCollectionResult<TResult>? result = default;
            foreach (var context in clientContext) {

                try {

                    result = await context.Invoke<TResult>(method, arguments, cancellationToken);
                    break;
                } catch (Exception e) {
                    Console.WriteLine(e);
                }

            }

            return result!;
        }


        public static T? InvokeSingle<T>(this IEnumerable<ClientContext> clientContext) {

            return default;
        }

        public static IEnumerable<ClientContext> WithAttribute(this IEnumerable<ClientContext> clientContexts, string key) {
            return clientContexts.Where(c => c.Attributes.Has(key));
        }

        public static IEnumerable<ClientContext> WithAttribute(this IEnumerable<ClientContext> clientContexts, string key, string value) {
            return clientContexts.Where(c => c.Attributes.Has(key, value));
        }


    }

    public class ClientCollectionResult<TResult> {

        public string ClientId { get; }

        public TResult Value { get; }

        public ClientCollectionResult(string clientId, TResult value) {
            ClientId = clientId;
            Value = value;
        }
    }

}
