using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;

namespace Cocoar.SignalARRR.Client.FullFramework {
    /// <summary>
    /// Convenience extension methods for HARRRConnection — InvokeAsync, SendAsync with method name strings.
    /// </summary>
    public static class HARRRConnectionExtensions {

        public static Task<TResult> InvokeAsync<TResult>(this HARRRConnection connection, string methodName, CancellationToken cancellationToken = default) {
            return connection.InvokeCoreAsync<TResult>(methodName, new object[0], cancellationToken);
        }

        public static Task<TResult> InvokeAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, CancellationToken cancellationToken = default) {
            return connection.InvokeCoreAsync<TResult>(methodName, new object[] { arg1 }, cancellationToken);
        }

        public static Task<TResult> InvokeAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, CancellationToken cancellationToken = default) {
            return connection.InvokeCoreAsync<TResult>(methodName, new object[] { arg1, arg2 }, cancellationToken);
        }

        public static async Task SendAsync(this HARRRConnection connection, string methodName, CancellationToken cancellationToken = default) {
            await connection.SendCoreAsync(methodName, new object[0], cancellationToken);
        }

        public static async Task SendAsync(this HARRRConnection connection, string methodName, object arg1, CancellationToken cancellationToken = default) {
            await connection.SendCoreAsync(methodName, new object[] { arg1 }, cancellationToken);
        }
    }
}
