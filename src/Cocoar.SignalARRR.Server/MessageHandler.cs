using System;
using System.Collections.Generic;
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

            object instance;
            if (methodInformations.MethodInfo.DeclaringType == HARRR.GetType()) {
                instance = ActivatorUtilities.CreateInstance(_serviceProvider, HARRR.GetType());
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

            var parameters = BuildExecuteMethodParameters(methodInfo, arguments, cancellationToken);
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
                instance = ActivatorUtilities.CreateInstance(_serviceProvider, HARRR.GetType());
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
                instance = ActivatorUtilities.CreateInstance(_serviceProvider, HARRR.GetType());
            } else {
                instance = invokeInfos.Factory.DynamicInvoke(_serviceProvider)!;
            }




            return await InvokeMethodInfoAsync(instance, invokeInfos.MethodInfo, clientMessage.Arguments, clientMessage.GenericArguments);

        }

        public async Task<object> InvokeMethodInfoAsync(object instance, MethodInfo methodInfo, IEnumerable<object> arguments, IEnumerable<string> genericArguments) {

            var parameters = BuildExecuteMethodParameters(methodInfo, arguments);

            SetInvokingInstanceProperties(instance);

            if (genericArguments?.Any() == true) {

                var arrType = genericArguments.Select(TypeHelper.FindType).ToList();
                methodInfo = methodInfo.MakeGenericMethod(arrType.ToArray()!);
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
                instance = ActivatorUtilities.CreateInstance(_serviceProvider, HARRR.GetType());
            } else {
                instance = _serviceProvider.GetRequiredService(methodInfo.ReflectedType!);
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


        private object[] BuildExecuteMethodParameters(MethodInfo methodInfo, IEnumerable<object> parameters, CancellationToken cancellation = default) {

            int paramsPosition = 0;
            var @params = parameters.ToList();
            return methodInfo.GetParameters().Select<ParameterInfo, object>(p => {

                if (p.ParameterType == typeof(CancellationToken)) {
                    return cancellation;
                }

                var fromServices = p.GetCustomAttribute<FromServicesAttribute>();

                if (fromServices != null) {
                    return _serviceProvider.GetRequiredService(p.ParameterType);
                }

                if (@params.Count < paramsPosition) {
                    throw new IndexOutOfRangeException();
                }

                var par = @params[paramsPosition];

                if (par != null && p.ParameterType != par.GetType()) {
                    if (par is JsonElement je) {
                        par = je.Deserialize(p.ParameterType)!;
                    } else {
                        par = par.Reflect().To(p.ParameterType)!;
                    }
                }

                paramsPosition++;
                return par!;

            }).ToArray();

        }


    }
}
