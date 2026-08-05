using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Client.ExtensionMethods;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.Common.Interfaces;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.SignalARRR.Client {
    public class MessageHandler {
        private readonly HARRRContext _harrrContext;
        private readonly Common.Serialization.IProtocolSerializer _serializer;
        private readonly ILogger _logger;
        private ISignalARRRMethodsCollection MethodsCollection { get; set; } = new SignalARRRMethodsCollection();

        private ISignalARRRInterfaceCollection InterfaceCollection { get; set; } = new SignalARRRInterfaceCollection();

        public MessageHandler(HARRRContext harrrContext, Common.Serialization.IProtocolSerializer? serializer = null, ILogger? logger = null) {
            _harrrContext = harrrContext;
            _serializer = serializer ?? new Common.Serialization.JsonProtocolSerializer();
            _logger = logger ?? NullLogger.Instance;
        }

        public async Task<string?> ChallengeAuthentication(ServerRequestMessage message) {
            return await _harrrContext.AccessTokenProvider();
        }

        public async Task<object?> InvokeServerRequest(ServerRequestMessage message) {
            message = PrepareServerRequestMessage(message);
            var result = await InvokeAsync(message);

            // If the result is a Stream, upload it to the server and return a StreamReference
            if (result is Stream stream) {
                return await UploadStreamAndReturnReference(stream);
            }

            return result;
        }

        private async Task<StreamReference> UploadStreamAndReturnReference(Stream stream) {
            var hubConnection = _harrrContext.GetHubConnection();

            // Ask server for an upload URL
            var uploadUrl = await hubConnection.InvokeCoreAsync<string>("RequestUploadSlot", Array.Empty<object>(), default);

            // Upload the stream via HTTP POST
            using var httpClient = new HttpClient();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var response = await httpClient.PostAsync(uploadUrl, content);
            response.EnsureSuccessStatusCode();

            return new StreamReference { Uri = uploadUrl };
        }

        public async Task InvokeServerMessage(ServerRequestMessage message) {

            try {
                message = PrepareServerRequestMessage(message);
                if (message.StreamId.HasValue) {
                    await InvokeAndStreamBackAsync(message);
                } else {
                    await InvokeAsync(message);
                }
            } catch (Exception ex) {
                // Fire-and-forget methods don't propagate errors to the server,
                // but log them so developers can diagnose failed server-to-client pushes.
                _logger.LogError(ex, "Failed to handle server message '{Method}'", message.Method);
            }
        }

        private async Task InvokeAndStreamBackAsync(ServerRequestMessage message) {
            var streamId = message.StreamId!.Value;
            var hubConnection = _harrrContext.GetHubConnection();
            try {
                var result = await InvokeAsync(message);
                await StreamResultToServer(hubConnection, streamId, result);
                await hubConnection.SendCoreAsync(MethodNames.StreamCompleteToServer, new object?[] { streamId, (string?)null });
            } catch (Exception ex) {
                try {
                    await hubConnection.SendCoreAsync(MethodNames.StreamCompleteToServer, new object[] { streamId, ex.GetBaseException().Message });
                } catch {
                    // Best effort — connection may be gone
                }
            }
        }

        private static async Task StreamResultToServer(HubConnection hubConnection, Guid streamId, object result) {
            if (result == null) return;

            // Try to enumerate as IAsyncEnumerable<T> using reflection-free helper
            var enumerateMethod = typeof(MessageHandler)
                .GetMethod(nameof(EnumerateAsyncEnumerable), BindingFlags.NonPublic | BindingFlags.Static);

            // Find the IAsyncEnumerable<T> interface to get T
            var asyncEnumInterface = result.GetType().GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));

            if (asyncEnumInterface == null && result.GetType().IsGenericType && result.GetType().GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)) {
                asyncEnumInterface = result.GetType();
            }

            if (asyncEnumInterface != null) {
                var elementType = asyncEnumInterface.GetGenericArguments()[0];
                var genericMethod = enumerateMethod!.MakeGenericMethod(elementType);
                await (Task)genericMethod.Invoke(null, new object[] { hubConnection, streamId, result })!;
            } else {
                // Single result — send as one item
                await hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, result });
            }
        }

        private static async Task EnumerateAsyncEnumerable<T>(HubConnection hubConnection, Guid streamId, IAsyncEnumerable<T> source) {
            await foreach (var item in source) {
                await hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, item! });
            }
        }


        public void RegisterInterface<TInterface, TClass>() where TClass : class, TInterface {
            InterfaceCollection.RegisterInterface<TInterface, TClass>();
        }
        public void RegisterInterface<TInterface, TClass>(TClass instance) where TClass : class, TInterface {

            InterfaceCollection.RegisterInterface<TInterface, TClass>(instance);
        }

        public void RegisterInterface<TInterface, TClass>(Func<IServiceProvider, TClass> factory)
            where TClass : class, TInterface {

            InterfaceCollection.RegisterInterface<TInterface, TClass>(factory);
        }


        public void RegisterInterface(Type interfaceType, Type instanceType) {

            InterfaceCollection.RegisterInterface(interfaceType, instanceType);
        }

        public void RegisterInterface(Type interfaceType, object instance) {
            InterfaceCollection.RegisterInterface(interfaceType, instance);
        }

        public void RegisterInterface(Type interfaceType, Func<IServiceProvider, object> factory) {
            InterfaceCollection.RegisterInterface(interfaceType, factory);
        }








        private Task<object> InvokeAsync(ServerRequestMessage serverRequestMessage) {

            if (serverRequestMessage.Method.Contains("|")) {
                return InvokeInterfaceMethodAsync(serverRequestMessage);
            }

            return InvokeMethodAsync(serverRequestMessage);
        }
        private async Task<object> InvokeMethodAsync(ServerRequestMessage serverRequestMessage) {



            var methodCallInfo = MethodsCollection.GetMethodInformations(serverRequestMessage.Method);

            var instance = methodCallInfo.Factory.DynamicInvoke(_harrrContext.GetHubConnection().GetServiceProvider())!;

            return InvokeMethodInfoAsync(instance, methodCallInfo.MethodInfo, serverRequestMessage.Arguments, serverRequestMessage.GenericArguments, serverRequestMessage.CancellationGuid);

        }

        private Task<object> InvokeInterfaceMethodAsync(ServerRequestMessage serverRequestMessage) {

            var invokeInfos = InterfaceCollection.GetInvokeInformation(serverRequestMessage.Method);
            var instance = invokeInfos.Factory.DynamicInvoke(_harrrContext.GetHubConnection().GetServiceProvider())!;
            return InvokeMethodInfoAsync(instance, invokeInfos.MethodInfo, serverRequestMessage.Arguments, serverRequestMessage.GenericArguments, serverRequestMessage.CancellationGuid);

        }


        private async Task<object> InvokeMethodInfoAsync(object instance, MethodInfo methodInfo, IEnumerable<object> arguments, IEnumerable<string> genericArguments, Guid? cancellationTokenGuid) {

            CancellationToken cancellationToken = default;
            if (cancellationTokenGuid.HasValue) {
                var cancellation = new CancellationTokenSource();
                cancellationTokenSources.TryAdd(cancellationTokenGuid.Value, cancellation);
                cancellationToken = cancellation.Token;
            }


            var parameters = await BuildExecuteMethodParameters(methodInfo, arguments, cancellationToken);

            if (genericArguments?.Any() == true) {

                var arrType = genericArguments.Select(TypeHelper.FindType).ToList();
                methodInfo = methodInfo.MakeGenericMethod(arrType.ToArray()!);
            }

            object? result = null;
            if (methodInfo.ReturnType == typeof(void) || methodInfo.ReturnType == typeof(Task)) {
                await InvokeHelper.InvokeVoidMethodAsync(instance, methodInfo, parameters);
            } else if (IsAsyncEnumerableType(methodInfo.ReturnType)) {
                // IAsyncEnumerable<T> — invoke directly, don't try to await as Task
                result = methodInfo.Invoke(instance, parameters);
            } else {
                result = await InvokeHelper.InvokeMethodAsync<object>(instance, methodInfo, parameters);
            }

            if (cancellationTokenGuid.HasValue) {
                cancellationTokenSources.TryRemove(cancellationTokenGuid.Value, out var token);
            }

            return result!;
        }

        private ConcurrentDictionary<Guid, CancellationTokenSource> cancellationTokenSources = new ConcurrentDictionary<Guid, CancellationTokenSource>();

        private async Task<object[]> BuildExecuteMethodParameters(MethodInfo methodInfo, IEnumerable<object> parameters, CancellationToken cancellation = default) {

            int paramsPosition = 0;
            var @params = parameters.ToList();

            var argumentList = new List<object>();

            foreach (var parameterInfo in methodInfo.GetParameters()) {
                // `<=`, and before the index. The guard used to read `@params.Count < paramsPosition`
                // *and* sat above the access anyway, so it could never fire: the position advances by
                // one per parameter, so it reaches Count but never passes it. The bare index threw an
                // IndexOutOfRangeException with nothing to say about which parameter was missing.
                if (paramsPosition >= @params.Count) {
                    throw new ArgumentException(
                        $"Parameter '{parameterInfo.Name}' (position {paramsPosition}) of '{methodInfo.Name}': " +
                        $"not enough arguments provided. Expected at least {paramsPosition + 1}, got {@params.Count}.");
                }

                var par = @params[paramsPosition];
                paramsPosition++;

                if (parameterInfo.ParameterType == typeof(CancellationToken)) {
                    // Check if the argument is a CancellationTokenReference (per-parameter cancellation from server)
                    var tokenFromRef = TryGetCancellationTokenFromReference(par);
                    if (tokenFromRef.HasValue) {
                        argumentList.Add(tokenFromRef.Value);
                        continue;
                    }
                    argumentList.Add(cancellation);
                    continue;
                }

                par = await PrepareArgumentForType(parameterInfo.ParameterType, par);

                if (par == null) {
                    argumentList.Add(null!);
                    continue;
                }

                if (parameterInfo.ParameterType != par.GetType()) {
                    if (par.Reflect().TryTo(parameterInfo.ParameterType, out var pt)) {
                        par = pt;
                    } else {
                        par = _serializer.ConvertTo(par, parameterInfo.ParameterType);
                    }
                }

                argumentList.Add(par!);

            }

            return argumentList.ToArray();
        }

        private async Task<object?> PrepareArgumentForType(Type type, object argument) {

            if (argument == null) {
                if (!type.IsValueType || type.IsNullableType()) {
                    // Reference types (string, classes, etc.) and Nullable<T> accept null
                    return null;
                } else {
                    // Non-nullable value types (int, Guid, etc.) get their default value
                    return Activator.CreateInstance(type);
                }
            }

            if (type == typeof(Stream)) {
                var streamReference = _serializer.TryConvertTo<StreamReference>(argument);
                if (streamReference != null && !string.IsNullOrEmpty(streamReference.Uri)) {
                    var resolver = new StreamReferenceResolver(streamReference, _harrrContext);
                    return await resolver.ProcessStreamArgument();
                }
            }

            return argument;
        }




        private ServerRequestMessage PrepareServerRequestMessage(ServerRequestMessage message) {
            // Re-deserialize to ensure consistent typing (e.g., JsonElement → typed objects)
            var converted = _serializer.TryConvertTo<ServerRequestMessage>(message);
            return converted ?? message;
        }

        private static bool IsAsyncEnumerableType(Type type) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
                return true;
            return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
        }

        private CancellationToken? TryGetCancellationTokenFromReference(object argument) {
            if (argument == null) return null;

            var reference = _serializer.TryConvertTo<CancellationTokenReference>(argument);

            if (reference == null || reference.Id == Guid.Empty) return null;

            var cts = new CancellationTokenSource();
            cancellationTokenSources.TryAdd(reference.Id, cts);
            return cts.Token;
        }

        public void CancelTokenFromServer(ServerRequestMessage requestMessage) {

            if (requestMessage.CancellationGuid.HasValue) {
                if (cancellationTokenSources.TryRemove(requestMessage.CancellationGuid.Value, out var token)) {
                    token.Cancel();
                }
            }

        }
    }
}
