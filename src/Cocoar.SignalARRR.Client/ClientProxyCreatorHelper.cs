using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.ProxyGenerator;

namespace Cocoar.SignalARRR.Client {
    public class ClientProxyCreatorHelper : ProxyCreatorHelper {
        private readonly HARRRConnection _harrrConnection;


        public ClientProxyCreatorHelper(HARRRConnection harrrConnection) {
            _harrrConnection = harrrConnection;
        }

        public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, arguments);
            msg.GenericArguments = genericArguments.ToArray();
            return SimpleAsyncHelper.RunSync(() => _harrrConnection.InvokeCoreAsync<T>(msg, cancellationToken));
        }

        public override Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, arguments);
            msg.GenericArguments = genericArguments.ToArray();
            return _harrrConnection.InvokeCoreAsync<T>(msg, cancellationToken);
        }

        //public override object Invoke(Type returnType, string methodName, IEnumerable<object> arguments, string[] genericArguments,
        //    CancellationToken cancellationToken = default) {

        //    var methodInfo = typeof(ClientProxyCreatorHelper).GetMethods()
        //        .WithName(nameof(Invoke)).First(p => p.HasGenericArgumentsLengthOf(1));
        //    var generic = methodInfo.MakeGenericMethod(returnType);

        //    var parameters = new object[] {methodName, arguments, genericArguments, cancellationToken};
        //    var res = generic.Invoke(this, parameters);
        //    //var res = InvokeHelper.InvokeMethod<object>(this, generic, parameters);
        //    return res;

        //}

        //public override async Task<object> InvokeAsync(Type returnType, string methodName, IEnumerable<object> arguments, string[] genericArguments,
        //    CancellationToken cancellationToken = default) {
        //    var methodInfo = typeof(ClientProxyCreatorHelper).GetMethods()
        //        .WithName(nameof(InvokeAsync)).First(p => p.HasGenericArgumentsLengthOf(1));
        //    var generic = methodInfo.MakeGenericMethod(returnType);

        //    var parameters = new object[] {methodName, arguments, genericArguments, cancellationToken};
        //    //var res = generic.Invoke(this, parameters);
        //    var res = await InvokeHelper.InvokeMethodAsync(this, generic, parameters);
        //    return res;
        //}

        public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, arguments);
            msg.GenericArguments = genericArguments.ToArray();
            SimpleAsyncHelper.RunSync(() => _harrrConnection.SendCoreAsync(msg, cancellationToken));
        }

        public override Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, arguments);
            msg.GenericArguments = genericArguments.ToArray();
            return _harrrConnection.SendCoreAsync(msg, cancellationToken);
        }

        public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
            var msg = new ClientRequestMessage(methodName, arguments);
            msg.GenericArguments = genericArguments.ToArray();
            return _harrrConnection.StreamAsyncCore<TResult>(msg, cancellationToken);
        }
    }
}
