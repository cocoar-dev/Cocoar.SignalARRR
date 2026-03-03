using System;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;

namespace Cocoar.SignalARRR.Server {
    internal interface IClientContextDispatcher {

        Task<TResult> InvokeClientAsync<TResult>(string clientId, ServerRequestMessage serverRequestMessage,
            CancellationToken cancellationToken);

        Task SendClientAsync(string clientId, ServerRequestMessage serverRequestMessage, CancellationToken cancellationToken);

        Task<string> Challenge(string clientId);

        Task CancelToken(string clientId, Guid id);
    }
}
