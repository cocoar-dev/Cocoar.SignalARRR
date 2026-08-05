using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Common;

namespace Cocoar.SignalARRR.Client.FullFramework {
    internal class ClientProxyCreatorHelper : ProxyCreatorHelper {
        private readonly HARRRConnection _harrrConnection;

        public ClientProxyCreatorHelper(HARRRConnection harrrConnection) {
            _harrrConnection = harrrConnection;
        }

        public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, WithoutCancellationTokens(arguments));
            msg.GenericArguments = genericArguments.ToArray();
            return SimpleAsyncHelper.RunSync(() => _harrrConnection.InvokeCoreAsync<T>(msg, cancellationToken));
        }

        public override Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, WithoutCancellationTokens(arguments));
            msg.GenericArguments = genericArguments.ToArray();
            return _harrrConnection.InvokeCoreAsync<T>(msg, cancellationToken);
        }

        public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, WithoutCancellationTokens(arguments));
            msg.GenericArguments = genericArguments.ToArray();
            SimpleAsyncHelper.RunSync(() => _harrrConnection.SendCoreAsync(msg, cancellationToken));
        }

        public override Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, WithoutCancellationTokens(arguments));
            msg.GenericArguments = genericArguments.ToArray();
            return _harrrConnection.SendCoreAsync(msg, cancellationToken);
        }

        public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, WithoutCancellationTokens(arguments));
            msg.GenericArguments = genericArguments.ToArray();
            return _harrrConnection.StreamAsyncCore<TResult>(msg, cancellationToken);
        }

        /// <summary>
        /// Drops <see cref="CancellationToken"/> arguments before the message goes to the server.
        /// </summary>
        /// <remarks>
        /// The proxy reports every parameter it was given, because whether a token belongs on the
        /// wire depends on the direction and the proxy is shared by both. In this one it does not:
        /// the server binds its own token for a <c>CancellationToken</c> parameter, taking it from
        /// the invocation rather than from the arguments, and skips that parameter without consuming
        /// an argument slot. Sending one would therefore shift every following argument by one --
        /// and serialize a token that means nothing on the other side.
        /// </remarks>
        private static IEnumerable<object> WithoutCancellationTokens(IEnumerable<object> arguments)
            => arguments.Where(argument => argument is not CancellationToken);

    }
}
