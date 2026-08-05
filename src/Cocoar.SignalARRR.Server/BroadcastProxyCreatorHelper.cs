using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// ProxyCreatorHelper that sends to an IClientProxy (Group, All, Clients(ids), etc.).
    /// All calls are fire-and-forget via SendCoreAsync. Return values from Invoke methods
    /// are discarded — clients execute the method but don't send a response.
    /// </summary>
    internal class BroadcastProxyCreatorHelper : ProxyCreatorHelper {
        private readonly IClientProxy _clientProxy;
        private readonly ILogger? _logger;

        public BroadcastProxyCreatorHelper(IClientProxy clientProxy, ILogger? logger = null) {
            _clientProxy = clientProxy;
            _logger = logger;
        }

        public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            Cocoar.Reflectensions.Helper.SimpleAsyncHelper.RunSync(() => SendAsync(methodName, arguments, genericArguments, cancellationToken));
        }

        public override Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            ValidateNoStreamArguments(methodName, arguments);
            var msg = new ServerRequestMessage(methodName, arguments.ToList());
            msg.GenericArguments = genericArguments;
            return _clientProxy.SendCoreAsync(MethodNames.InvokeServerMessage, new object[] { msg }, cancellationToken);
        }

        public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            ValidateNoStreamReturn<T>(methodName);
            ValidateNoStreamArguments(methodName, arguments);
            _logger?.LogWarning("Broadcast invoke '{Method}': return value will be discarded by clients", methodName);
            Send(methodName, arguments, genericArguments, cancellationToken);
            return default!;
        }

        public override Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            ValidateNoStreamReturn<T>(methodName);
            ValidateNoStreamArguments(methodName, arguments);
            _logger?.LogWarning("Broadcast invoke '{Method}': return value will be discarded by clients", methodName);
            return SendAsync(methodName, arguments, genericArguments, cancellationToken).ContinueWith(_ => default(T)!);
        }

        private static void ValidateNoStreamReturn<T>(string methodName) {
            if (typeof(Stream).IsAssignableFrom(typeof(T))) {
                throw new NotSupportedException(
                    $"Method '{methodName}' returns a Stream. Stream return values are not supported for broadcast/multi-client operations. Use single-client GetTypedMethods<T>() instead.");
            }
        }

        private static void ValidateNoStreamArguments(string methodName, IEnumerable<object> arguments) {
            if (arguments.Any(a => a is Stream)) {
                throw new NotSupportedException(
                    $"Method '{methodName}' has a Stream argument. Stream arguments are not supported for broadcast/multi-client operations. Use single-client GetTypedMethods<T>() instead.");
            }

            BroadcastArgumentRules.RejectCancellationTokens(methodName, arguments);
        }

        public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            throw new NotSupportedException("Streaming is not supported for broadcast targets (Group/All). Use Send instead.");
        }
    }
}
