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

namespace Cocoar.SignalARRR.Client {
    public class MessageHandler {
        private readonly HARRRContext _harrrContext;
        private ISignalARRRMethodsCollection MethodsCollection { get; set; } = new SignalARRRMethodsCollection();

        private ISignalARRRInterfaceCollection InterfaceCollection { get; set; } = new SignalARRRInterfaceCollection();
        
        public MessageHandler(HARRRContext harrrContext) {
            _harrrContext = harrrContext;
        }

        public async Task ChallengeAuthentication(ServerRequestMessage message) {

            string payload = null;
            string error = null;
            try {
                payload = await _harrrContext.AccessTokenProvider();
            } catch (Exception e) {
                error = e.GetBaseException().Message;
            }
            

            await _harrrContext.GetHubConnection().SendCoreAsync(MethodNames.ReplyServerRequest, new object[] { message.Id, payload, error });

        }

        public async Task InvokeServerRequest(ServerRequestMessage message) {
            
            try {
                message = PrepareServerRequestMessage(message);
                var payload = await InvokeAsync(message);
                await SendResponse(message.Id, payload, null);
            } catch (Exception e) {
                await _harrrContext.GetHubConnection().SendCoreAsync(MethodNames.ReplyServerRequest, new object[] { message.Id, null, e.GetBaseException().Message });
            }

        }

        public async Task InvokeServerMessage(ServerRequestMessage message) {

            try {
                message = PrepareServerRequestMessage(message);
                await InvokeAsync(message);
            } catch {
                // ignored
            }
        }


        //public void RegisterMethods<TClass>(string prefix = null) where TClass : class {
        //    RegisterMethods(typeof(TClass), typeof(TClass), prefix);
        //}
        //public void RegisterMethods<TClass>(TClass instance, string prefix = null) where TClass : class {
        //    RegisterMethods(typeof(TClass), typeof(TClass), instance, prefix);
        //}
        //public void RegisterMethods<TClass>(Func<TClass> factory, string prefix = null) where TClass : class {
        //    RegisterMethods(typeof(TClass), typeof(TClass), factory, prefix);
        //}

        //public void RegisterMethods<TInterface, TClass>(string prefix = null) where TClass : class, TInterface {
        //    RegisterMethods(typeof(TInterface), typeof(TClass), prefix);
        //}
        //public void RegisterMethods<TInterface, TClass>(TClass instance, string prefix = null) where TClass : class, TInterface {
        //    RegisterMethods(typeof(TInterface), typeof(TClass), instance, prefix);
        //}
        //public void RegisterMethods<TInterface, TClass>(Func<TClass> factory, string prefix = null) where TClass : class, TInterface {
        //    RegisterMethods(typeof(TInterface), typeof(TClass), factory, prefix);
        //}

        //public void RegisterMethods(Type interfaceType, Type instanceType, string prefix = null) {
        //    Func<object> factory = () => {
        //        var fromServiceProvider = _harrrContext.GetHubConnection().GetServiceProvider().GetService(instanceType);
        //        if (fromServiceProvider != null) {
        //            return fromServiceProvider;
        //        }

        //        return Activator.CreateInstance(instanceType);
        //    };
        //    RegisterMethods(interfaceType, instanceType, factory, prefix);
        //}
        //public void RegisterMethods(Type interfaceType, Type instanceType, object instance, string prefix = null) {
        //    RegisterMethods(interfaceType, instanceType, () => instance, prefix);
        //}
        //public void RegisterMethods(Type interfaceType, Type instanceType, Func<object> factory, string prefix = null) {

        //    var rootName = instanceType.GetCustomAttribute<MessageNameAttribute>()?.Name ?? prefix.ToNull() ?? instanceType.Name;
        //    var methodsWithName = interfaceType.GetMethods().Select(m => (MethodInfo: m, Attribute: m.GetCustomAttribute<MessageNameAttribute>()));
        //    foreach (var (methodInfo, methodNameAttribute) in methodsWithName) {
        //        var methodName = methodNameAttribute?.Name ?? methodInfo.Name;
        //        var concatNames = $"{rootName}.{methodName}";
        //        MethodsCollection.AddMethod(interfaceType, methodInfo, factory);
        //    }
        //}


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


       


        //public void RegisterISignalARRRClientMethodsCollection(ISignalARRRClientMethodsCollection methodsCollection) {
        //    MethodsCollection = methodsCollection;
        //}




        private async Task SendResponse(Guid id, object payload, string error) {

            if (_harrrContext.UseHttpResponse) {
                var url = _harrrContext.GetResponseUri(id, error);
                var httpClient = new HttpClient();

                if (!string.IsNullOrEmpty(error)) {
                    await httpClient.PostAsync(url, null);
                } else {
                    var jsonPayload = JsonSerializer.Serialize(payload);
                    await httpClient.PostAsync(url, new StringContent(jsonPayload, Encoding.UTF8, "application/json"));
                }
                
            } else {
                await _harrrContext.GetHubConnection().SendCoreAsync(MethodNames.ReplyServerRequest, new object[] { id, payload, error });
            }
        }

        private Task<object> InvokeAsync(ServerRequestMessage serverRequestMessage) {

            if (serverRequestMessage.Method.Contains("|")) {
                return InvokeInterfaceMethodAsync(serverRequestMessage);
            }

            return InvokeMethodAsync(serverRequestMessage);
        }
        private async Task<object> InvokeMethodAsync(ServerRequestMessage serverRequestMessage) {

           

            var methodCallInfo = MethodsCollection.GetMethodInformations(serverRequestMessage.Method);
            
            var instance = methodCallInfo.Factory.DynamicInvoke(_harrrContext.GetHubConnection().GetServiceProvider());

            return InvokeMethodInfoAsync(instance, methodCallInfo.MethodInfo, serverRequestMessage.Arguments, serverRequestMessage.GenericArguments, serverRequestMessage.CancellationGuid);

        }

        private Task<object> InvokeInterfaceMethodAsync(ServerRequestMessage serverRequestMessage) {
            
            var invokeInfos = InterfaceCollection.GetInvokeInformation(serverRequestMessage.Method);
            var instance = invokeInfos.Factory.DynamicInvoke(_harrrContext.GetHubConnection().GetServiceProvider());
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
                methodInfo = methodInfo.MakeGenericMethod(arrType.ToArray());
            }

            object result = null;
            if (methodInfo.ReturnType == typeof(void) || methodInfo.ReturnType == typeof(Task)) {
                await InvokeHelper.InvokeVoidMethodAsync(instance, methodInfo, parameters);
            } else {
                result = await InvokeHelper.InvokeMethodAsync<object>(instance, methodInfo, parameters);
            }

            if (cancellationTokenGuid.HasValue) {
                cancellationTokenSources.TryRemove(cancellationTokenGuid.Value, out var token);
            }

            return result;
        }

        private ConcurrentDictionary<Guid, CancellationTokenSource> cancellationTokenSources = new ConcurrentDictionary<Guid, CancellationTokenSource>();

        private async Task<object[]> BuildExecuteMethodParameters(MethodInfo methodInfo, IEnumerable<object> parameters, CancellationToken cancellation = default) {

            int paramsPosition = 0;
            var @params = parameters.ToList();

            var argumentList = new List<object>();

            foreach (var parameterInfo in methodInfo.GetParameters())
            {
                if (@params.Count < paramsPosition) {
                    throw new IndexOutOfRangeException();
                }
                var par = @params[paramsPosition];
                paramsPosition++;

                if (parameterInfo.ParameterType == typeof(CancellationToken)) {
                    argumentList.Add(cancellation);
                    continue;
                }

                par = await PrepareArgumentForType(parameterInfo.ParameterType, par);

                if (par == null) {
                    argumentList.Add(null);
                    continue;
                }

                if (parameterInfo.ParameterType != par.GetType()) {

                    if (par.Reflect().TryTo(parameterInfo.ParameterType, out var pt)) {
                        par = pt;
                    } else {
                        var json = JsonSerializer.Serialize(par);
                        par = JsonSerializer.Deserialize(json, parameterInfo.ParameterType);
                    }
                   
                }

                argumentList.Add(par);

            }

            return argumentList.ToArray();
            //return methodInfo.GetParameters().Select(p => {

            //    if (p.ParameterType == typeof(CancellationToken)) {
            //        return cancellation;
            //    }

            //    if (@params.Count < paramsPosition) {
            //        throw new IndexOutOfRangeException();
            //    }

            //    var par = @params[paramsPosition];

            //    par = await PrepareArgumentForType(p.ParameterType, par);

            //    if (par != null && p.ParameterType != par.GetType()) {

            //        if (par is JToken jt) {
            //            par = jt.ToObject(p.ParameterType);
            //        } else {
            //            par = par.To(p.ParameterType);
            //        }

            //    }

            //    paramsPosition++;
            //    return par;

            //}).ToArray();

        }

        private async Task<object> PrepareArgumentForType(Type type, object argument) {

            if (argument == null) {
                if (type.IsNullableType()) {
                    return null;
                } else {
                    return Activator.CreateInstance(type);
                }
            }

            if (type == typeof(Stream)) {
                
                var json = JsonSerializer.Serialize(argument);
                var streamReference = JsonSerializer.Deserialize<StreamReference>(json);
                var resolver = new StreamReferenceResolver(streamReference, _harrrContext);
                return await resolver.ProcessStreamArgument();
            }

            return argument;
        }

        


        private ServerRequestMessage PrepareServerRequestMessage(ServerRequestMessage message) {
            switch (_harrrContext.HubProtocolType)
            {
                case HubProtocolType.JsonHubProtocol:
                {
                    var requestJson = JsonSerializer.Serialize(message);
                    message = JsonSerializer.Deserialize<ServerRequestMessage>(requestJson);
                    break;
                }
                case HubProtocolType.MessagePackHubProtocol:
                {
                    var requestJson = JsonSerializer.Serialize(message);
                    message = JsonSerializer.Deserialize<ServerRequestMessage>(requestJson);
                    break;
                }
            }

            return message;
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
