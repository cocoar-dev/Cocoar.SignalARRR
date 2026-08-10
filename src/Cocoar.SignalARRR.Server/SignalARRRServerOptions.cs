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

        /// <summary>
        /// How long a server method with a <see cref="System.IO.Stream"/> parameter waits for the
        /// client to actually upload it. Default: 2 minutes.
        /// </summary>
        /// <remarks>
        /// There was previously no timeout at all, and on the non-streaming invoke path not even a
        /// cancellation token. A client could request an upload slot, invoke the method and simply
        /// never upload — parking the invocation indefinitely. Repeated, that exhausts the thread
        /// pool and the server stops answering anyone.
        /// </remarks>
        public TimeSpan StreamUploadTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Maximum accepted size of a client-to-server stream upload, in bytes. Default: 100 MB.
        /// Set to 0 to disable the limit.
        /// </summary>
        /// <remarks>
        /// Uploads are buffered in memory so they can outlive the HTTP request, so an unbounded body
        /// is an unbounded allocation.
        /// </remarks>
        public long MaxUploadSizeBytes { get; set; } = 100L * 1024 * 1024;

        /// <summary>
        /// How long an unused upload slot is kept before it is swept. Default: 10 minutes.
        /// </summary>
        public TimeSpan UploadSlotExpiration { get; set; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// How many unused upload slots one connection may hold at a time. Default: 32.
        /// Set to 0 to remove the limit.
        /// </summary>
        /// <remarks>
        /// Requesting a slot is an ordinary hub call, and each one pins a dictionary entry and a
        /// <c>TaskCompletionSource</c> for <see cref="UploadSlotExpiration"/> whether or not anything
        /// is ever uploaded — so without a cap one client in a loop can grow server memory until the
        /// sweep catches up, which it will not while the loop runs. The default is well above what a
        /// client needs (slots are consumed as soon as the upload completes) and well below what an
        /// abusive one wants. A client that hits it gets
        /// <see cref="Common.HARRRErrorCodes.UploadSlotLimitReached"/>.
        /// </remarks>
        public int MaxUploadSlotsPerConnection { get; set; } = 32;

        /// <summary>
        /// Whether client IP addresses appear in log messages. Default: false — an IP address is
        /// personal data under most privacy regimes, so logging it is a deliberate decision, not a
        /// side effect of enabling debug logging.
        /// </summary>
        public bool LogClientIpAddresses { get; set; }

        /// <summary>
        /// How long a client-to-server stream may exist without ever being consumed before it is
        /// reaped. Default: 10 minutes. Streams that are actually being read are never reaped.
        /// </summary>
        public TimeSpan StreamIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

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

        /// <summary>
        /// Opt in to client IP addresses in log messages. Default: off — an IP address is
        /// personal data.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithClientIpLogging(bool enabled = true) {
            _options.LogClientIpAddresses = enabled;
            return this;
        }

        /// <summary>
        /// Set how long a never-consumed client-to-server stream may exist before it is reaped.
        /// Default: 10 minutes.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithStreamIdleTimeout(TimeSpan timeout) {
            _options.StreamIdleTimeout = timeout;
            return this;
        }

        /// <summary>
        /// Set how long a server method waits for a client to upload a <see cref="System.IO.Stream"/>
        /// parameter. Default: 2 minutes.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithStreamUploadTimeout(TimeSpan timeout) {
            _options.StreamUploadTimeout = timeout;
            return this;
        }

        /// <summary>
        /// Set the maximum accepted upload size in bytes. Default: 100 MB. Use 0 to disable.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithMaxUploadSize(long bytes) {
            _options.MaxUploadSizeBytes = bytes;
            return this;
        }

        /// <summary>
        /// Set how long an unused upload slot is kept before being swept. Default: 10 minutes.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithUploadSlotExpiration(TimeSpan expiration) {
            _options.UploadSlotExpiration = expiration;
            return this;
        }

        /// <summary>
        /// Set how many unused upload slots one connection may hold. Default: 32. Use 0 to disable.
        /// </summary>
        public SignalARRRServerOptionsBuilder WithMaxUploadSlotsPerConnection(int maxSlots) {
            _options.MaxUploadSlotsPerConnection = maxSlots;
            return this;
        }

        public static implicit operator SignalARRRServerOptions(SignalARRRServerOptionsBuilder builder) {
            return builder._options;
        }
    }
}
