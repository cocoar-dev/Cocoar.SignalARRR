using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.ProxyGenerator;
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


        /// <summary>
        /// Typed invoke on all clients in the collection. Calls each client individually via InvokeCoreAsync,
        /// awaits all in parallel, and returns results per client.
        /// </summary>
        public static async Task<IEnumerable<ClientCollectionResult<TResult>>> InvokeAllAsync<TInterface, TResult>(
            this IEnumerable<ClientContext> clientContexts,
            Func<TInterface, TResult> action,
            CancellationToken cancellationToken = default)
            where TInterface : class {

            var (methodName, arguments) = CaptureCall<TInterface>(action, validateNoStreams: true);
            ValidateNoStreamResult<TResult>(methodName);
            return await clientContexts.InvokeAllAsync<TResult>(methodName, arguments, cancellationToken);
        }

        /// <summary>
        /// Typed invoke on all clients (async method overload).
        /// </summary>
        public static async Task<IEnumerable<ClientCollectionResult<TResult>>> InvokeAllAsync<TInterface, TResult>(
            this IEnumerable<ClientContext> clientContexts,
            Func<TInterface, Task<TResult>> action,
            CancellationToken cancellationToken = default)
            where TInterface : class {

            var (methodName, arguments) = CaptureCall<TInterface>(action, validateNoStreams: true);
            ValidateNoStreamResult<TResult>(methodName);
            return await clientContexts.InvokeAllAsync<TResult>(methodName, arguments, cancellationToken);
        }

        /// <summary>
        /// Typed invoke — calls clients one by one until the first succeeds.
        /// </summary>
        public static async Task<ClientCollectionResult<TResult>> InvokeOneAsync<TInterface, TResult>(
            this IEnumerable<ClientContext> clientContexts,
            Func<TInterface, TResult> action,
            CancellationToken cancellationToken = default)
            where TInterface : class {

            var (methodName, arguments) = CaptureCall<TInterface>(action, validateNoStreams: true);
            ValidateNoStreamResult<TResult>(methodName);
            return await clientContexts.InvokeOneAsync<TResult>(methodName, arguments, cancellationToken);
        }

        /// <summary>
        /// Typed invoke one (async method overload).
        /// </summary>
        public static async Task<ClientCollectionResult<TResult>> InvokeOneAsync<TInterface, TResult>(
            this IEnumerable<ClientContext> clientContexts,
            Func<TInterface, Task<TResult>> action,
            CancellationToken cancellationToken = default)
            where TInterface : class {

            var (methodName, arguments) = CaptureCall<TInterface>(action, validateNoStreams: true);
            ValidateNoStreamResult<TResult>(methodName);
            return await clientContexts.InvokeOneAsync<TResult>(methodName, arguments, cancellationToken);
        }

        private static (string methodName, object[] arguments) CaptureCall<TInterface>(Delegate action, bool validateNoStreams = false) where TInterface : class {
            var capturer = new CapturingProxyCreatorHelper();
            var proxy = ProxyCreator.CreateInstanceFromInterface<TInterface>(capturer);
            action.DynamicInvoke(proxy);

            if (capturer.CapturedMethodName == null)
                throw new InvalidOperationException("No method call was captured from the action.");

            var arguments = capturer.CapturedArguments ?? Array.Empty<object>();

            if (validateNoStreams && arguments.Any(a => a is Stream)) {
                throw new NotSupportedException(
                    $"Method '{capturer.CapturedMethodName}' has a Stream argument. Stream arguments are not supported for multi-client operations. Use single-client GetTypedMethods<T>() instead.");
            }

            return (capturer.CapturedMethodName, arguments);
        }

        private static void ValidateNoStreamResult<TResult>(string methodName) {
            if (typeof(Stream).IsAssignableFrom(typeof(TResult))) {
                throw new NotSupportedException(
                    $"Method '{methodName}' returns a Stream. Stream return values are not supported for multi-client operations. Use single-client GetTypedMethods<T>() instead.");
            }
        }

        public static IEnumerable<ClientContext> WithGroup(this IEnumerable<ClientContext> clientContexts, string groupName) {
            return clientContexts.Where(c => c.Groups.Contains(groupName));
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
