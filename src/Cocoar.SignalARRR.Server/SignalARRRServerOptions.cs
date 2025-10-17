using System;
using System.Collections.Generic;
using System.Reflection;

namespace Cocoar.SignalARRR.Server {
    public class SignalARRRServerOptions {

        public List<Assembly> AssembliesContainingServerMethods { get; }= new List<Assembly>()
        {
            Assembly.GetEntryAssembly()
        };

        public List<Type> PreBuiltClientMethods { get; } = new List<Type>();

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

        public static implicit operator SignalARRRServerOptions(SignalARRRServerOptionsBuilder builder) {
            return builder._options;
        }
    }
}
