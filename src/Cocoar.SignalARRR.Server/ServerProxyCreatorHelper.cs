using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private readonly MethodArgumentPreparer _methodArgumentPreparer;

        public ServerProxyCreatorHelper(ClientContext clientContext, HttpContext? httpContext) {
            _clientContext = clientContext;
            _methodArgumentPreparer = new MethodArgumentPreparer(_clientContext);
            _httpContext = httpContext;
        }

        public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            return SimpleAsyncHelper.RunSync(() => InvokeAsync<T>(methodName, arguments, genericArguments, cancellationToken));
        }

        public override async Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {

            using var registrations = new CancellationRegistrations();
            var preparedArguments = _methodArgumentPreparer.PrepareArguments(arguments, registrations).ToList();

            var msg = new ServerRequestMessage(methodName, preparedArguments);
            registrations.Add(RegisterCallCancellation(msg, arguments, cancellationToken));

            msg.GenericArguments = genericArguments;
            using var serviceProviderScope = _clientContext.ServiceProvider.CreateScope();

            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(_clientContext.HARRRType);
            var dispatcher = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            // If the return type is Stream, the client sends a StreamReference instead.
            // We need to invoke as StreamReference and then resolve the upload.
            if (typeof(T) == typeof(Stream)) {
                var streamRef = await dispatcher.InvokeClientAsync<StreamReference>(_clientContext.Id, msg, cancellationToken);
                if (streamRef != null && !string.IsNullOrEmpty(streamRef.Uri)) {
                    var streamManager = _clientContext.ServiceProvider.GetRequiredService<ServerPushStreamManager>();
                    var timeout = _clientContext.ServiceProvider.GetService<SignalARRRServerOptions>()?.StreamUploadTimeout
                        ?? TimeSpan.FromMinutes(2);

                    var stream = await streamManager.WaitForUpload(streamRef.Uri, _clientContext.Id, timeout, cancellationToken);
                    return (T)(object)stream;
                }
                return default!;
            }

            return await dispatcher.InvokeClientAsync<T>(_clientContext.Id, msg, cancellationToken);
        }

        public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            SimpleAsyncHelper.RunSync(() => SendAsync(methodName, arguments, genericArguments, cancellationToken));
        }

        public override async Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            using var registrations = new CancellationRegistrations();
            var preparedArguments = _methodArgumentPreparer.PrepareArguments(arguments, registrations).ToList();

            var msg = new ServerRequestMessage(methodName, preparedArguments);
            registrations.Add(RegisterCallCancellation(msg, arguments, cancellationToken));
            msg.GenericArguments = genericArguments;
            using var serviceProviderScope = _clientContext.ServiceProvider.CreateScope();

            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(_clientContext.HARRRType);
            var dispatcher = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);
            await dispatcher.SendClientAsync(_clientContext.Id, msg, cancellationToken);
            if (_httpContext != null) {
                _httpContext.Response.StatusCode = StatusCodes.Status200OK;
                await _httpContext.Response.Body.FlushAsync();
            }

        }

        public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            // Not a `using`: unlike an invoke or a send, this method returns before the work is over.
            // The callbacks have to stay hooked for as long as the stream can still be cancelled, so
            // the enumerable below owns them and unhooks on completion.
            var registrations = new CancellationRegistrations();
            var preparedArguments = _methodArgumentPreparer.PrepareArguments(arguments, registrations).ToList();

            var streamId = Guid.NewGuid();
            var msg = new ServerRequestMessage(methodName, preparedArguments);
            msg.GenericArguments = genericArguments;
            msg.StreamId = streamId;

            registrations.Add(RegisterCallCancellation(msg, arguments, cancellationToken));

            var serverStreamManager = _clientContext.ServiceProvider.GetRequiredService<ServerStreamManager>();
            // Only the client this stream was requested from may feed it.
            serverStreamManager.CreateStream(streamId, _clientContext.Id);

            // Send in background — scope must outlive the send
            var serviceProviderScope = _clientContext.ServiceProvider.CreateScope();
            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(_clientContext.HARRRType);
            var dispatcher = (IClientContextDispatcher)serviceProviderScope.ServiceProvider.GetRequiredService(hubContextType);

            _ = dispatcher.SendClientAsync(_clientContext.Id, msg, cancellationToken)
                .ContinueWith(_ => serviceProviderScope.Dispose());

            var serializer = _clientContext.ServiceProvider.GetService<Common.Serialization.IProtocolSerializer>();
            return UnhookWhenFinished(serverStreamManager.ReadStream<TResult>(streamId, cancellationToken, serializer), registrations);
        }

        /// <summary>
        /// Passes a stream through and unhooks the call's cancellation callbacks once it ends,
        /// however it ends — completed, faulted or abandoned mid-enumeration.
        /// </summary>
        /// <remarks>
        /// A caller that takes the stream and never enumerates it keeps the callbacks, exactly as it
        /// keeps the stream itself; that case is the idle sweeper's, not this method's.
        /// </remarks>
        private static async IAsyncEnumerable<T> UnhookWhenFinished<T>(
            IAsyncEnumerable<T> source,
            CancellationRegistrations registrations,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) {

            try {
                await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                    yield return item;
                }
            } finally {
                registrations.Dispose();
            }
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
        /// The registration is returned rather than dropped: on a long-lived token — the kind server
        /// code builds for "cancel everything for this connection" — a dropped one keeps its callback,
        /// and the <see cref="ClientContext"/> it closes over, attached for the life of the token
        /// (DI-6). The caller unhooks it when the call is over.
        /// </para>
        /// </remarks>
        private CancellationTokenRegistration RegisterCallCancellation(
            ServerRequestMessage message, IEnumerable<object> originalArguments, CancellationToken cancellationToken) {

            if (cancellationToken == CancellationToken.None) {
                return default;
            }

            if (originalArguments.Any(a => a is CancellationToken argumentToken && argumentToken == cancellationToken)) {
                return default;
            }

            var callId = Guid.NewGuid();
            message.CancellationGuid = callId;

            // Not an async lambda: Register takes an Action, so one would compile to async void and
            // take the process down when it throws — which is the normal case here, because the
            // token usually fires precisely because the client has gone. The send is best-effort
            // and already swallows downstream in ClientContextExtensions.CancelToken.
            return cancellationToken.Register(() => _ = _clientContext.CancelToken(callId));
        }
    }
}
