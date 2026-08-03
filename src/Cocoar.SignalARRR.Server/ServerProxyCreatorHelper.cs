using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    public class ServerProxyCreatorHelper : ProxyCreatorHelper {
        private readonly ClientContext _clientContext;
        private readonly HttpContext? _httpContext;
        private readonly MethodArgumentPreperator _methodArgumentPreperator;

        public ServerProxyCreatorHelper(ClientContext clientContext, HttpContext? httpContext) {
            _clientContext = clientContext;
            _methodArgumentPreperator = new MethodArgumentPreperator(_clientContext);
            _httpContext = httpContext;
        }

        public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            return SimpleAsyncHelper.RunSync(() => InvokeAsync<T>(methodName, arguments, genericArguments, cancellationToken));
        }

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

            // If the return type is Stream, the client sends a StreamReference instead.
            // We need to invoke as StreamReference and then resolve the upload.
            if (typeof(T) == typeof(Stream)) {
                var streamRef = await harrrContext.InvokeClientAsync<StreamReference>(_clientContext.Id, msg, cancellationToken);
                if (streamRef != null && !string.IsNullOrEmpty(streamRef.Uri)) {
                    var streamManager = _clientContext.ServiceProvider.GetRequiredService<ServerPushStreamManager>();
                    var timeout = _clientContext.ServiceProvider.GetService<SignalARRRServerOptions>()?.StreamUploadTimeout
                        ?? TimeSpan.FromMinutes(2);

                    var stream = await streamManager.WaitForUpload(streamRef.Uri, timeout, cancellationToken);
                    return (T)(object)stream;
                }
                return default!;
            }

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
            // Only the client this stream was requested from may feed it.
            serverStreamManager.CreateStream(streamId, _clientContext.Id);

            // Send in background — scope must outlive the send
            var serviceProviderScope = _clientContext.ServiceProvider.CreateScope();
            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(_clientContext.HARRRType);
            var harrrContext = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            _ = harrrContext.SendClientAsync(_clientContext.Id, msg, cancellationToken)
                .ContinueWith(_ => serviceProviderScope.Dispose());

            var serializer = _clientContext.ServiceProvider.GetService<Common.Serialization.IProtocolSerializer>();
            return serverStreamManager.ReadStream<TResult>(streamId, cancellationToken, serializer);
        }


    }
}
