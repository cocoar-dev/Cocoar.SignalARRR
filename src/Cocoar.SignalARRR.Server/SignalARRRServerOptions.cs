using System;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace Cocoar.SignalARRR.Server {
    public class SignalARRRServerOptions {

        public List<Assembly> AssembliesContainingServerMethods { get; } = new List<Assembly>()
        {
            Assembly.GetEntryAssembly()!
        };

        public List<Type> PreBuiltClientMethods { get; } = new List<Type>();

        /// <summary>
        /// Duration for which a client's authentication result is cached after successful validation.
        /// Default: 3 minutes. Set to TimeSpan.Zero to disable caching (re-authenticate on every call).
        /// </summary>
        public TimeSpan AuthCacheDuration { get; set; } = TimeSpan.FromMinutes(3);

        /// <summary>
        /// Whether to check certificate revocation (CRL/OCSP) during transport-level auth revalidation.
        /// Default: true.
        /// </summary>
        public bool ValidateCertificateRevocation { get; set; } = true;

        /// <summary>
        /// The X509 revocation mode used when checking client certificates.
        /// Default: Online (queries CRL/OCSP endpoints).
        /// </summary>
        public X509RevocationMode CertificateRevocationMode { get; set; } = X509RevocationMode.Online;

        /// <summary>
        /// Optional custom certificate validation callback. When set, this is called instead of
        /// the default X509Chain check during transport-level cert revalidation.
        /// Return true if the certificate is still valid.
        /// </summary>
        public Func<X509Certificate2, bool>? CustomCertificateValidator { get; set; }

    }

    public class SignalARRRServerOptionsBuilder {
        private SignalARRRServerOptions _options = new SignalARRRServerOptions();

        public SignalARRRServerOptionsBuilder AddServerMethodsFrom(params Assembly[] assemblies) {
            foreach (var assembly in assemblies) {
                if (!_options.AssembliesContainingServerMethods.Contains(assembly))
                    _options.AssembliesContainingServerMethods.Add(assembly);
            }

            return this;
        }

        public SignalARRRServerOptionsBuilder PreBuiltClientMethods<T>() {
            if (!_options.PreBuiltClientMethods.Contains(typeof(T))) {
                _options.PreBuiltClientMethods.Add(typeof(T));
            }

            return this;
        }

        /// <summary>
        /// Set the duration for which authentication results are cached per client.
        /// Default: 3 minutes.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithAuthCacheDuration(TimeSpan duration) {
            _options.AuthCacheDuration = duration;
            return this;
        }

        /// <summary>
        /// Enable or disable certificate revocation checking during transport-level auth revalidation.
        /// Default: true.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithCertificateRevocationCheck(bool enabled) {
            _options.ValidateCertificateRevocation = enabled;
            return this;
        }

        /// <summary>
        /// Set the X509 revocation mode for certificate checking.
        /// Default: Online.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithCertificateRevocationMode(X509RevocationMode mode) {
            _options.CertificateRevocationMode = mode;
            return this;
        }

        /// <summary>
        /// Set a custom certificate validation callback. When set, this replaces the default
        /// X509Chain-based revocation check. Return true if the certificate is valid.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithCustomCertificateValidator(Func<X509Certificate2, bool> validator) {
            _options.CustomCertificateValidator = validator;
            return this;
        }

        public static implicit operator SignalARRRServerOptions(SignalARRRServerOptionsBuilder builder) {
            return builder._options;
        }
    }
}
