using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    public class ServerProxyCreatorHelper : ProxyCreatorHelper {
        private readonly ClientContext _clientContext;
        //private readonly ServerPushStreamManager _pushStreamManager;
        private readonly HttpContext? _httpContext;
        private readonly MethodArgumentPreperator _methodArgumentPreperator;

        public ServerProxyCreatorHelper(ClientContext clientContext, HttpContext? httpContext) {
            _clientContext = clientContext;
            //_pushStreamManager = clientContext.ServiceProvider.GetRequiredService<ServerPushStreamManager>();
            _methodArgumentPreperator = new MethodArgumentPreperator(_clientContext);
            _httpContext = httpContext;
        }

        //public override object Invoke(Type returnType, string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
        //    var methodInfo = typeof(ServerProxyCreatorHelper).GetMethods()
        //        .WithName(nameof(InvokeAsync)).First(p => p.HasGenericArgumentsLengthOf(1));
        //    var generic = methodInfo.MakeGenericMethod(returnType);

        //    var parameters = new object[] {methodName, arguments, genericArguments, cancellationToken};
        //    return InvokeHelper.InvokeMethod(this, generic, new List<Type>() {returnType}, parameters);
        //}

        public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            return SimpleAsyncHelper.RunSync(() => InvokeAsync<T>(methodName, arguments, genericArguments, cancellationToken));
        }

        //public override Task<object> InvokeAsync(Type returnType, string methodName, IEnumerable<object> arguments,
        //    string[] genericArguments, CancellationToken cancellationToken = default) {

        //    var methodInfo = typeof(ServerProxyCreatorHelper).GetMethods()
        //        .WithName(nameof(InvokeAsync)).First(p => p.HasGenericArgumentsLengthOf(1));
        //    var generic = methodInfo.MakeGenericMethod(returnType);

        //    var parameters = new object[] {methodName, arguments, genericArguments, cancellationToken};
        //    return InvokeHelper.InvokeMethodAsync(this, generic, new List<Type>() {returnType}, parameters);
        //}


        public override async Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {


            var preparedArguments = _methodArgumentPreperator.PrepareArguments(arguments).ToList();

            var msg = new ServerRequestMessage(methodName, preparedArguments);
            if (cancellationToken != CancellationToken.None) {
                msg.CancellationGuid = Guid.NewGuid();
                cancellationToken.Register(() => {
#pragma warning disable 4014
                    _clientContext.CancelToken(msg.CancellationGuid.Value);
#pragma warning restore 4014
                });
            }

            msg.GenericArguments = genericArguments;
            using var serviceProviderScope = _clientContext.ServiceProvider.CreateScope();

            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(_clientContext.HARRRType);
            var harrrContext = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            //if(_httpContext != null)
            //{
            //    await harrrContext.ProxyClientAsync(_clientContext.Id, msg, _httpContext);
            //    return default;
            //}

            return await harrrContext.InvokeClientAsync<T>(_clientContext.Id, msg, cancellationToken);
        }

        public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            SimpleAsyncHelper.RunSync(() => SendAsync(methodName, arguments, genericArguments, cancellationToken));
        }

        public override async Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var preparedArguments = _methodArgumentPreperator.PrepareArguments(arguments).ToList();

            var msg = new ServerRequestMessage(methodName, preparedArguments);
            if (cancellationToken != CancellationToken.None) {
                msg.CancellationGuid = Guid.NewGuid();
#pragma warning disable 4014
                cancellationToken.Register(() => _clientContext.CancelToken(msg.CancellationGuid.Value));
#pragma warning restore 4014
            }
            msg.GenericArguments = genericArguments;
            using var serviceProviderScope = _clientContext.ServiceProvider.CreateScope();

            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(_clientContext.HARRRType);
            var harrrContext = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);
            await harrrContext.SendClientAsync(_clientContext.Id, msg, cancellationToken);
            if (_httpContext != null) {
                await _httpContext.Ok();
            }

        }

        public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var preparedArguments = _methodArgumentPreperator.PrepareArguments(arguments).ToList();

            var streamId = Guid.NewGuid();
            var msg = new ServerRequestMessage(methodName, preparedArguments);
            msg.GenericArguments = genericArguments;
            msg.StreamId = streamId;

            if (cancellationToken != CancellationToken.None) {
                msg.CancellationGuid = Guid.NewGuid();
                cancellationToken.Register(() => {
#pragma warning disable 4014
                    _clientContext.CancelToken(msg.CancellationGuid.Value);
#pragma warning restore 4014
                });
            }

            var serverStreamManager = _clientContext.ServiceProvider.GetRequiredService<ServerStreamManager>();
            serverStreamManager.CreateStream(streamId);

            // Send in background — scope must outlive the send
            var serviceProviderScope = _clientContext.ServiceProvider.CreateScope();
            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(_clientContext.HARRRType);
            var harrrContext = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            _ = harrrContext.SendClientAsync(_clientContext.Id, msg, cancellationToken)
                .ContinueWith(_ => serviceProviderScope.Dispose());

            return serverStreamManager.ReadStream<TResult>(streamId, cancellationToken);
        }


    }
}
