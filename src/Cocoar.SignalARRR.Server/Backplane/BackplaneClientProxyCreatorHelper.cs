using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.ProxyGenerator;

namespace Cocoar.SignalARRR.Server {
    internal sealed class BackplaneClientProxyCreatorHelper : ProxyCreatorHelper {
        private readonly Type? _hubType;
        private readonly string _connectionId;
        private readonly ISignalARRRBackplane _backplane;

        public BackplaneClientProxyCreatorHelper(Type? hubType, string connectionId, ISignalARRRBackplane backplane) {
            _hubType = hubType;
            _connectionId = connectionId;
            _backplane = backplane;
        }

        public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            return SimpleAsyncHelper.RunSync(() => InvokeAsync<T>(methodName, arguments, genericArguments, cancellationToken));
        }

        public override async Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            ValidateSupported(methodName, arguments, typeof(T));

            var message = CreateMessage(methodName, arguments, genericArguments);
            var result = await _backplane.InvokeConnectionAsync(_hubType, _connectionId, message, typeof(T), cancellationToken);
            return result == null ? default! : (T)result;
        }

        public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            SimpleAsyncHelper.RunSync(() => SendAsync(methodName, arguments, genericArguments, cancellationToken));
        }

        public override Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            ValidateSupported(methodName, arguments, resultType: null);

            return _backplane.PublishDispatchAsync(
                _hubType,
                SignalARRRBackplaneTargetKind.Connections,
                CreateMessage(methodName, arguments, genericArguments),
                new[] { _connectionId },
                cancellationToken: cancellationToken);
        }

        public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            throw new NotSupportedException("Streaming is not supported for remote backplane client calls in the first backplane slice.");
        }

        private static ServerRequestMessage CreateMessage(string methodName, IEnumerable<object> arguments, string[] genericArguments) {
            var message = new ServerRequestMessage(methodName, arguments.ToList());
            message.GenericArguments = genericArguments;
            return message;
        }

        private static void ValidateSupported(string methodName, IEnumerable<object> arguments, Type? resultType) {
            if (arguments.Any(a => a is Stream)) {
                throw new NotSupportedException(
                    $"Method '{methodName}' has a Stream argument. Stream arguments are not supported for remote backplane client calls.");
            }

            if (resultType != null && typeof(Stream).IsAssignableFrom(resultType)) {
                throw new NotSupportedException(
                    $"Method '{methodName}' returns a Stream. Stream return values are not supported for remote backplane client calls.");
            }
        }
    }
}
