using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {

    internal class DefaultTransportAuthRevalidationService : ITransportAuthRevalidationService {

        private readonly SignalARRRServerOptions _options;

        public DefaultTransportAuthRevalidationService(IServiceProvider serviceProvider) {
            _options = serviceProvider.GetService<SignalARRRServerOptions>() ?? new SignalARRRServerOptions();
        }

        public Task<bool> RevalidateAsync(ClientContext clientContext, CancellationToken cancellationToken = default) {
            if (clientContext.ClientCertificate != null) {
                return ValidateCertificateAsync(clientContext.ClientCertificate);
            }

            // Non-cert transport auth (cookies, Negotiate): check principal is still authenticated
            return Task.FromResult(clientContext.User.Identity?.IsAuthenticated == true);
        }

        private Task<bool> ValidateCertificateAsync(X509Certificate2 cert) {
            // Basic expiry check (NotAfter/NotBefore are in local time)
            var now = DateTime.Now;
            if (cert.NotAfter < now || cert.NotBefore > now) {
                return Task.FromResult(false);
            }

            // Custom validator takes precedence
            if (_options.CustomCertificateValidator != null) {
                return Task.FromResult(_options.CustomCertificateValidator(cert));
            }

            if (!_options.ValidateCertificateRevocation) {
                return Task.FromResult(true);
            }

            // X509Chain is not thread-safe — create per call.
            // Chain.Build() may perform network I/O (CRL/OCSP), so run on thread pool.
            return Task.Run(() => {
                using var chain = new X509Chain();
                chain.ChainPolicy.RevocationMode = _options.CertificateRevocationMode;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EndCertificateOnly;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                return chain.Build(cert);
            });
        }
    }
}
