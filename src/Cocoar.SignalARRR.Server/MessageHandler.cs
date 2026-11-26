using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Runs one client call: resolves the method in the hub's generated table, authorizes it and
    /// calls it with the message's arguments.
    /// </summary>
    internal class MessageHandler {

        private readonly HARRR _hub;
        private readonly ClientContext _clientContext;
        private readonly ServerMethodTable _methods;
        private readonly IServiceProvider _serviceProvider;

        public MessageHandler(HARRR hub, ClientContext clientContext, ServerMethodTable methods, IServiceProvider serviceProvider) {
            _hub = hub;
            _clientContext = clientContext;
            _methods = methods;
            _serviceProvider = serviceProvider;
        }

        public async Task<IAsyncEnumerable<object>> InvokeStreamAsync(ClientRequestMessage clientMessage, CancellationToken cancellationToken) {
            var result = await InvokeCoreAsync(clientMessage, cancellationToken, cancellationToken).ConfigureAwait(false);
            return result as IAsyncEnumerable<object>
                   ?? throw new NotSupportedException($"Method '{clientMessage.Method}' does not return a stream.");
        }

        /// <param name="parameterCancellation">
        /// The token bound to the invoked method's <see cref="CancellationToken"/> parameters. The
        /// entry point chooses it (N-3): the invoke paths pass <c>ConnectionAborted</c> — the
        /// caller is gone, the answer reaches nobody — while the send path passes
        /// <c>ApplicationStopping</c>, because a fire-and-forget caller asked for the *work*, and
        /// "write to the DB" must not stop just because the connection dropped afterwards.
        /// </param>
        public Task<object?> InvokeAsync(ClientRequestMessage clientMessage, CancellationToken parameterCancellation) =>
            // The upload wait keeps ConnectionAborted regardless of what the method's own token is
            // bound to (V-2): waiting for an upload from a client that already disconnected parked
            // a thread-pool thread permanently.
            InvokeCoreAsync(clientMessage, parameterCancellation, _hub.Context.ConnectionAborted);

        private async Task<object?> InvokeCoreAsync(ClientRequestMessage clientMessage, CancellationToken parameterCancellation, CancellationToken uploadCancellation) {

            var method = _methods.Resolve(clientMessage.Method, clientMessage.Arguments.Length);

            var authentication = new SignalARRRAuthentication(_serviceProvider);
            var result = await authentication.Authorize(_clientContext, clientMessage.Authorization, method.Authorization).ConfigureAwait(false);
            if (!result.Succeeded) {
                throw new UnauthorizedException();
            }

            var invoke = SelectInvoker(method, clientMessage.GenericArguments);
            var call = new ServerCall(_hub, _clientContext, _serviceProvider, method, parameterCancellation, uploadCancellation);
            var options = _serviceProvider.GetRequiredService<ServerPayloadOptions>().Options;
            var arguments = new JsonArgumentReader(clientMessage.Arguments, options, call);

            return await invoke(call, arguments).ConfigureAwait(false);
        }

        /// <summary>
        /// The call to run: the method itself, or the instantiation of a generic method that the
        /// caller's type arguments name.
        /// </summary>
        /// <remarks>
        /// The names come off the wire, so they are matched against the instantiations the method
        /// declared with <c>[GenericArguments]</c> rather than resolved. Resolving them used to let
        /// the caller pick any loaded type, and every distinct value-type instantiation permanently
        /// generated native code.
        /// </remarks>
        private static ServerMethodInvoker SelectInvoker(ServerMethodDescriptor method, string[]? genericArguments) {
            var supplied = genericArguments?.Length ?? 0;

            if (method.GenericArity == 0) {
                if (supplied > 0) {
                    throw new HARRRException(new ArgumentException(
                        $"Method '{method.Name}' is not generic, but {supplied} type argument(s) were supplied."));
                }

                return method.Invoke!;
            }

            if (supplied != method.GenericArity) {
                throw new HARRRException(new ArgumentException(
                    $"Method '{method.Name}' expects {method.GenericArity} type argument(s), but {supplied} were supplied."));
            }

            return method.ResolveGeneric!(genericArguments!)
                   ?? throw new HARRRException(new ArgumentException(
                       $"Type argument(s) '{string.Join(", ", genericArguments!)}' for method '{method.Name}' are not among those declared with [GenericArguments]."));
        }
    }
}
