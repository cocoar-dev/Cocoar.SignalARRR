using System;
using System.Globalization;
using System.Security.Claims;
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

            // Only credentials bound to the connection may be revalidated against the cached
            // principal. For anything else -- a bearer token in particular -- "the principal says it
            // is authenticated" is tautological: it was authenticated once, and asking it again can
            // never say otherwise, so the credential's own lifetime would never be enforced.
            if (!clientContext.HasTransportLevelCredentials()) {
                return Task.FromResult(false);
            }

            return Task.FromResult(!TransportCredentialPolicy.IsExpired(clientContext.User));
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
