using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Starts and stops a backplane with the host. A type of its own so that it can be registered
    /// with <c>TryAddEnumerable</c>: calling <c>AddSignalARRRPostgresBackplane</c> or
    /// <c>AddSignalARRRRedisBackplane</c> twice — once in the application, once more in a test
    /// setup — used to register the same backplane instance twice as a hosted service, so the host
    /// started and stopped it twice (#85).
    /// </summary>
    internal sealed class BackplaneHostedService<TBackplane> : IHostedService where TBackplane : SignalARRRBackplaneBase {
        private readonly TBackplane _backplane;

        public BackplaneHostedService(TBackplane backplane) {
            _backplane = backplane;
        }

        public Task StartAsync(CancellationToken cancellationToken) => _backplane.StartAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => _backplane.StopAsync(cancellationToken);
    }
}
