using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Common.Interfaces;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ObservableExtensions = Cocoar.SignalARRR.Server.ExtensionMethods.ObservableExtensions;
using TypeHelper = Cocoar.SignalARRR.Common.Helper.TypeHelper;

namespace Cocoar.SignalARRR.Server {
    internal class MessageHandler {

        private ISignalARRRMethodsCollection MethodsCollection { get; }

        private ISignalARRRInterfaceCollection InterfaceCollection { get; }

        private ILogger Logger { get; }

        private ClientContext ClientContext { get; }

        private HARRR HARRR { get; }

        private IServiceProvider _serviceProvider;

        public MessageHandler(HARRR harrr, ClientContext clientContext, ISignalARRRMethodsCollection methodsCollection, IServiceProvider serviceProvider, ISignalARRRInterfaceCollection signalARRRInterfaceCollection) {
            HARRR = harrr;
            MethodsCollection = methodsCollection;
            InterfaceCollection = signalARRRInterfaceCollection;
            ClientContext = clientContext;
            _serviceProvider = serviceProvider;
            Logger = _serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(GetType().FullName!) ?? NullLogger.Instance;
        }



        public async Task<IAsyncEnumerable<object>> InvokeStreamAsync(ClientRequestMessage clientMessage, CancellationToken cancellationToken) {


            if (clientMessage.Method.Contains("|")) {
                return await InvokeInterfaceStreamAsync(clientMessage, cancellationToken);

            }

            return await InvokeMethodStreamAsync(clientMessage, cancellationToken);
        }

        public async Task<IAsyncEnumerable<object>> InvokeMethodStreamAsync(ClientRequestMessage clientMessage, CancellationToken cancellationToken) {

            var methodInformations = MethodsCollection.GetMethodInformations(clientMessage.Method);


            var authentication = new SignalARRRAuthentication(_serviceProvider);
            var result = await authentication.Authorize(ClientContext, clientMessage.Authorization, methodInformations.MethodInfo);

            if (!result.Succeeded) {
                throw new UnauthorizedException();
            }

            // Methods declared on the hub run on the hub instance SignalR created for this
            // invocation. Constructing a second one via ActivatorUtilities bypassed IHubActivator
            // and any hub filters, re-ran the hub constructor on every message, made state
            // established in OnConnectedAsync invisible to the method, and leaked: Hub is
            // IDisposable, but ActivatorUtilities does not register the instance for scope disposal.
            object instance;
            if (methodInformations.MethodInfo.DeclaringType == HARRR.GetType()) {
                instance = HARRR;
            } else {
                instance = _serviceProvider.GetRequiredService(methodInformations.MethodInfo.ReflectedType!);
            }

            return await InvokeStreamMethodInfoAsync(instance, methodInformations.MethodInfo, clientMessage.Arguments, cancellationToken);

        }

        public async Task<IAsyncEnumerable<object>> InvokeInterfaceStreamAsync(ClientRequestMessage clientMessage, CancellationToken cancellationToken) {

            var invokeInfos = InterfaceCollection.GetInvokeInformation(clientMessage.Method);

            var authentication = new SignalARRRAuthentication(_serviceProvider);
            var result = await authentication.Authorize(ClientContext, clientMessage.Authorization, invokeInfos.MethodInfo);

            if (!result.Succeeded) {
                throw new UnauthorizedException();
            }

            var instance = invokeInfos.Factory.DynamicInvoke(_serviceProvider)!;


            return await InvokeStreamMethodInfoAsync(instance, invokeInfos.MethodInfo, clientMessage.Arguments,
                cancellationToken);

        }

        public async Task<IAsyncEnumerable<object>> InvokeStreamMethodInfoAsync(object instance, MethodInfo methodInfo, IEnumerable<object> arguments, CancellationToken cancellationToken) {

            var taskType = methodInfo.ReturnType;
            if (taskType.IsGenericTypeOf(typeof(Task<>))) {
                taskType = methodInfo.ReturnType.GenericTypeArguments[0];
            }

            var parameters = await BuildExecuteMethodParametersAsync(methodInfo, arguments, cancellationToken).ConfigureAwait(false);
            SetInvokingInstanceProperties(instance);

            if (taskType.IsGenericTypeOf(typeof(ChannelReader<>))) {
                return await InvokeStreamingMethodAsync(instance, methodInfo, parameters).ConfigureAwait(false);
            }

            if (taskType.IsGenericTypeOf(typeof(IAsyncEnumerable<>))) {
                return await InvokeStreamingMethodAsync(instance, methodInfo, parameters).ConfigureAwait(false);
            }

            if (taskType.IsGenericTypeOf(typeof(IObservable<>))) {
                return await InvokeIObservableMethodAsync(instance, methodInfo, cancellationToken, parameters).ConfigureAwait(false);
            }


            throw new NotSupportedException();

        }



        public async Task<object> InvokeAsync(ClientRequestMessage clientMessage) {

            if (clientMessage.Method.Contains("|")) {
                return await InvokeInterfaceAsync(clientMessage);
            }

            return await InvokeMethodAsync(clientMessage);


        }

        public async Task<object> InvokeMethodAsync(ClientRequestMessage clientMessage) {

            var methodInformations = MethodsCollection.GetMethodInformations(clientMessage.Method);

            var authentication = new SignalARRRAuthentication(_serviceProvider);
            var result = await authentication.Authorize(ClientContext, clientMessage.Authorization, methodInformations.MethodInfo);

            if (!result.Succeeded) {
                throw new UnauthorizedException();
            }

            object instance;
            if (methodInformations.MethodInfo.DeclaringType == HARRR.GetType()) {
                instance = HARRR;
            } else {
                instance = _serviceProvider.GetRequiredService(methodInformations.MethodInfo.ReflectedType!);
            }

            return await InvokeMethodInfoAsync(instance, methodInformations.MethodInfo, clientMessage.Arguments, clientMessage.GenericArguments);

        }

        public async Task<object> InvokeInterfaceAsync(ClientRequestMessage clientMessage) {

            var invokeInfos = InterfaceCollection.GetInvokeInformation(clientMessage.Method);

            var authentication = new SignalARRRAuthentication(_serviceProvider);
            var result = await authentication.Authorize(ClientContext, clientMessage.Authorization, invokeInfos.MethodInfo);

            if (!result.Succeeded) {
                throw new UnauthorizedException();
            }

            object instance;
            if (invokeInfos.MethodInfo.DeclaringType == HARRR.GetType()) {
                instance = HARRR;
            } else {
                instance = invokeInfos.Factory.DynamicInvoke(_serviceProvider)!;
            }




            return await InvokeMethodInfoAsync(instance, invokeInfos.MethodInfo, clientMessage.Arguments, clientMessage.GenericArguments);

        }

        /// <summary>
        /// Closes an open generic method over the type arguments named by the caller.
        /// </summary>
        /// <remarks>
        /// The names come off the wire, so they are validated rather than trusted. Previously they
        /// went straight into <see cref="MethodInfo.MakeGenericMethod"/>: an unresolvable name became
        /// a <c>null</c> element and surfaced as an opaque reflection error, a wrong arity produced
        /// another, and every distinct value-type instantiation permanently JITs native code that is
        /// never reclaimed — so the caller both chose which type a generic method ran on and could
        /// grow the process without bound.
        /// </remarks>
        private static MethodInfo MakeGenericMethodChecked(MethodInfo methodInfo, IEnumerable<string> genericArguments) {

            if (!methodInfo.IsGenericMethodDefinition) {
                throw new HARRRException(new ArgumentException(
                    $"Method '{methodInfo.Name}' is not generic, but {genericArguments.Count()} type argument(s) were supplied."));
            }

            var expected = methodInfo.GetGenericArguments().Length;
            var names = genericArguments.ToList();

            if (names.Count != expected) {
                throw new HARRRException(new ArgumentException(
                    $"Method '{methodInfo.Name}' expects {expected} type argument(s), but {names.Count} were supplied."));
            }

            var resolved = new Type[names.Count];
            for (var i = 0; i < names.Count; i++) {
                resolved[i] = TypeHelper.FindType(names[i])
                    ?? throw new HARRRException(new ArgumentException(
                        $"Type argument '{names[i]}' for method '{methodInfo.Name}' could not be resolved."));
            }

            try {
                // Enforces the generic constraints declared on the method; without this any resolvable
                // type was accepted and the violation only showed up as an obscure failure later.
                return methodInfo.MakeGenericMethod(resolved);
            } catch (ArgumentException ex) {
                throw new HARRRException(new ArgumentException(
                    $"Type arguments for method '{methodInfo.Name}' do not satisfy its constraints.", ex));
            }
        }

        public async Task<object> InvokeMethodInfoAsync(object instance, MethodInfo methodInfo, IEnumerable<object> arguments, IEnumerable<string> genericArguments) {

            // ConnectionAborted rather than None: a server method declaring a CancellationToken now
            // actually observes the client going away, and an upload wait is released with it
            // instead of running to its timeout.
            var parameters = await BuildExecuteMethodParametersAsync(methodInfo, arguments, HARRR.Context.ConnectionAborted).ConfigureAwait(false);

            SetInvokingInstanceProperties(instance);

            if (genericArguments?.Any() == true) {
                methodInfo = MakeGenericMethodChecked(methodInfo, genericArguments);
            }

            if (methodInfo.ReturnType == typeof(void) || methodInfo.ReturnType == typeof(Task)) {
                await InvokeHelper.InvokeVoidMethodAsync(instance, methodInfo, parameters);
                return null!;
            } else {
                return await InvokeHelper.InvokeMethodAsync<object>(instance, methodInfo, parameters) ?? null!;
            }


        }



        private object BuildInvokeTypeInstance(MethodInfo methodInfo) {

            object instance;
            if (methodInfo.DeclaringType == HARRR.GetType()) {
                instance = HARRR;
            } else {
                instance = _serviceProvider.GetRequiredService(methodInfo.ReflectedType!);
            }

            // See SetInvokingInstanceProperties: the hub is already fully populated.
            if (ReferenceEquals(instance, HARRR)) {
                return instance;
            }

            var reflectInstance = instance.Reflect();
            reflectInstance.SetPropertyValue("ClientContext", ClientContext);
            reflectInstance.SetPropertyValue("Context", HARRR.Context);
            reflectInstance.SetPropertyValue("Clients", HARRR.Clients);
            reflectInstance.SetPropertyValue("Groups", HARRR.Groups);
            var logger = _serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(instance.GetType().FullName!) ?? NullLogger.Instance;
            reflectInstance.SetPropertyValue("Logger", logger);

            return instance;
        }

        private object SetInvokingInstanceProperties(object instance) {

            // The hub already has all of these -- SignalR populated Context/Clients/Groups, and the
            // ctor wired ClientContext and Logger. Re-setting them reflectively would be five
            // pointless lookups per message and would replace the hub's own logger.
            if (ReferenceEquals(instance, HARRR)) {
                return instance;
            }

            var reflectInstance = instance.Reflect();
            reflectInstance.SetPropertyValue("ClientContext", ClientContext);
            reflectInstance.SetPropertyValue("Context", HARRR.Context);
            reflectInstance.SetPropertyValue("Clients", HARRR.Clients);
            reflectInstance.SetPropertyValue("Groups", HARRR.Groups);
            var logger = _serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(instance.GetType().FullName!) ?? NullLogger.Instance;
            reflectInstance.SetPropertyValue("Logger", logger);

            return instance;
        }

        private async Task<StreamingResult> InvokeStreamingMethodAsync(object instance, MethodInfo methodInfo, params object[] parameters) {

            var ch = await InvokeHelper.InvokeMethodAsync<object>(instance, methodInfo, parameters).ConfigureAwait(false);

            Type taskType = methodInfo.ReturnType;
            if (taskType.GetGenericTypeDefinition() == typeof(Task<>)) {
                taskType = methodInfo.ReturnType.GenericTypeArguments[0];
            }


            var convType = typeof(StreamingResult<>).MakeGenericType(taskType.GenericTypeArguments[0]);

            var conv = (StreamingResult)Activator.CreateInstance(convType, ch, ClientContext, methodInfo)!;

            return conv;
        }

        private async Task<StreamingResult> InvokeIObservableMethodAsync(object instance, MethodInfo methodInfo, CancellationToken cancellationToken, params object[] parameters) {

            var ch = await InvokeHelper.InvokeMethodAsync<object>(instance, methodInfo, parameters).ConfigureAwait(false);

            Type taskType = methodInfo.ReturnType;
            if (taskType.GetGenericTypeDefinition() == typeof(Task<>)) {
                taskType = methodInfo.ReturnType.GenericTypeArguments[0];
            }

            var obsGenType = taskType.GenericTypeArguments[0];
            // ReSharper disable once PossibleNullReferenceException
            var convMethod = typeof(ObservableExtensions).GetMethod("AsChannelReaderInternal", BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(obsGenType);
            var channelReader = convMethod.Invoke(null, new[] { ch, cancellationToken });

            var convType = typeof(StreamingResult<>).MakeGenericType(taskType.GenericTypeArguments[0]);
            var conv = (StreamingResult)Activator.CreateInstance(convType, channelReader, ClientContext, methodInfo)!;

            return conv;
        }


        /// <summary>
        /// Binds the incoming arguments to the method's parameters.
        /// </summary>
        /// <remarks>
        /// Asynchronous because a <see cref="Stream"/> parameter waits for the client to upload it.
        /// That wait used to be bridged with a blocking <c>RunSync</c>, and on the non-streaming
        /// invoke path it was handed <see cref="CancellationToken.None"/> with no timeout — so a
        /// client that requested an upload slot and never uploaded parked a thread pool thread
        /// permanently. Repeated, that is a remote denial of service against the whole server.
        /// </remarks>
        private async Task<object[]> BuildExecuteMethodParametersAsync(MethodInfo methodInfo, IEnumerable<object> parameters, CancellationToken cancellation = default) {

            var @params = parameters as IList<object> ?? parameters.ToList();
            var methodParameters = methodInfo.GetParameters();
            var bound = new object[methodParameters.Length];

            var paramsPosition = 0;
            Common.Serialization.IProtocolSerializer? serializer = null;

            for (var i = 0; i < methodParameters.Length; i++) {
                var p = methodParameters[i];

                if (p.ParameterType == typeof(CancellationToken)) {
                    bound[i] = cancellation;
                    continue;
                }

                var fromServices = p.GetCustomAttribute<FromServicesAttribute>();

                if (fromServices != null) {
                    bound[i] = _serviceProvider.GetRequiredService(p.ParameterType);
                    continue;
                }

                if (paramsPosition >= @params.Count) {
                    throw new ArgumentException(
                        $"Parameter '{p.Name}' (position {paramsPosition}): " +
                        $"not enough arguments provided. Expected at least {paramsPosition + 1}, got {@params.Count}.");
                }

                var par = @params[paramsPosition];
                // Resolved once per message rather than once per parameter.
                serializer ??= _serviceProvider.GetRequiredService<Common.Serialization.IProtocolSerializer>();

                // If the parameter type is Stream, resolve StreamReference via HTTP upload
                if (typeof(Stream).IsAssignableFrom(p.ParameterType) && par != null) {
                    var streamRef = serializer.TryConvertTo<StreamReference>(par);
                    if (streamRef != null && !string.IsNullOrEmpty(streamRef.Uri)) {
                        var streamManager = _serviceProvider.GetRequiredService<ServerPushStreamManager>();
                        var timeout = _serviceProvider.GetService<SignalARRRServerOptions>()?.StreamUploadTimeout
                            ?? TimeSpan.FromMinutes(2);

                        par = await streamManager
                            .WaitForUpload(streamRef.Uri, timeout, cancellation)
                            .ConfigureAwait(false);
                    }
                } else if (par != null && p.ParameterType != par.GetType()) {
                    try {
                        par = serializer.ConvertTo(par, p.ParameterType)!;
                    } catch (Exception ex) {
                        // The received value is deliberately not echoed: a credential that fails to
                        // deserialize would otherwise land in the server log in clear text.
                        throw new ArgumentException(
                            $"Parameter '{p.Name}' (position {paramsPosition}): " +
                            $"failed to deserialize {par.GetType().Name} to {p.ParameterType.Name}.", ex);
                    }
                }

                bound[i] = par!;
                paramsPosition++;
            }

            return bound;
        }


    }
}
