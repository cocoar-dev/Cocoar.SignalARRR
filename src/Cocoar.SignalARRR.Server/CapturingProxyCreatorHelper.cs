using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.ProxyGenerator;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// A ProxyCreatorHelper that captures the method name and arguments from a single proxy call
    /// instead of actually sending. Used to extract call info from Action&lt;T&gt; / Func&lt;T, TResult&gt; lambdas.
    /// </summary>
    internal class CapturingProxyCreatorHelper : ProxyCreatorHelper {

        public string? CapturedMethodName { get; private set; }
        public object[]? CapturedArguments { get; private set; }
        public string[]? CapturedGenericArguments { get; private set; }

        private void Capture(string methodName, IEnumerable<object> arguments, string[] genericArguments) {
            CapturedMethodName = methodName;
            CapturedArguments = arguments.ToArray();
            CapturedGenericArguments = genericArguments;
        }

        public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            Capture(methodName, arguments, genericArguments);
        }

        public override Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            Capture(methodName, arguments, genericArguments);
            return Task.CompletedTask;
        }

        public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            Capture(methodName, arguments, genericArguments);
            return default!;
        }

        public override Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            Capture(methodName, arguments, genericArguments);
            return Task.FromResult(default(T)!);
        }

        public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            Capture(methodName, arguments, genericArguments);
            return null!;
        }
    }
}
