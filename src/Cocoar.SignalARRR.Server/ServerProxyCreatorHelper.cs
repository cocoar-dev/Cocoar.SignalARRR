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
            RegisterCallCancellation(msg, arguments, cancellationToken);

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
            RegisterCallCancellation(msg, arguments, cancellationToken);
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

            RegisterCallCancellation(msg, arguments, cancellationToken);

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



        /// <summary>
        /// Gives the call itself a cancellation id, when it needs one of its own.
        /// </summary>
        /// <remarks>
        /// This is the id for cancelling the invocation as a whole — what a caller needs when it
        /// passes a token to a method that declares no token parameter. Token <em>parameters</em>
        /// are handled separately, each with its own id, so that two of them stay independently
        /// cancellable.
        /// <para>
        /// Skipped when this same token already travels as an argument, which is the common case:
        /// the generated proxy hands the token to the helper and puts it in the arguments. Without
        /// the check both would fire on cancellation and send two messages for one event.
        /// </para>
        /// <para>
        /// The registration is discarded, which leaks on a long-lived token (DI-6). Tracked
        /// separately; this change deliberately does not widen into it.
        /// </para>
        /// </remarks>
        private void RegisterCallCancellation(
            ServerRequestMessage message, IEnumerable<object> originalArguments, CancellationToken cancellationToken) {

            if (cancellationToken == CancellationToken.None) {
                return;
            }

            if (originalArguments.Any(a => a is CancellationToken argumentToken && argumentToken == cancellationToken)) {
                return;
            }

            var callId = Guid.NewGuid();
            message.CancellationGuid = callId;

            // Not an async lambda: Register takes an Action, so one would compile to async void and
            // take the process down when it throws — which is the normal case here, because the
            // token usually fires precisely because the client has gone. The send is best-effort
            // and already swallows downstream in HARRRContext.CancelToken.
            cancellationToken.Register(() => _ = _clientContext.CancelToken(callId));
        }
    }
}
