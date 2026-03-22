using System;
using System.Collections.Generic;
using System.Reflection;

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

        public static implicit operator SignalARRRServerOptions(SignalARRRServerOptionsBuilder builder) {
            return builder._options;
        }
    }
}
