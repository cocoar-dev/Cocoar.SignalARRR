using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Client {
    public partial class HARRRConnection {

        // Every overload ends in the generated dispatcher of TInterface, so the contract has to be a
        // [SignalARRRContract] interface; registering anything else fails at once with a message
        // saying so, rather than at the first call from the server.

        /// <summary>Receives the server's calls on <typeparamref name="TInterface"/> with <paramref name="instance"/>.</summary>
        public void RegisterInterface<TInterface>(TInterface instance) where TInterface : class {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            _connectionContext.MessageHandler.RegisterInterface<TInterface>(_ => instance);
        }

        /// <summary>Receives the server's calls on <typeparamref name="TInterface"/> with an instance <paramref name="factory"/> creates per call.</summary>
        public void RegisterInterface<TInterface>(Func<IServiceProvider, TInterface> factory) where TInterface : class {
            _connectionContext.MessageHandler.RegisterInterface(factory);
        }

        /// <summary>
        /// Receives the server's calls on <typeparamref name="TInterface"/> with a
        /// <typeparamref name="TClass"/> from the connection's service provider, or created with its
        /// dependencies from there (<see cref="ActivatorUtilities"/>), per call.
        /// </summary>
        public void RegisterInterface<TInterface, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClass>()
            where TInterface : class
            where TClass : class, TInterface {
            _connectionContext.MessageHandler.RegisterInterface<TInterface>(
                sp => sp.GetService<TClass>() ?? ActivatorUtilities.CreateInstance<TClass>(sp));
        }

        /// <summary>Receives the server's calls on <typeparamref name="TInterface"/> with <paramref name="instance"/>.</summary>
        public void RegisterInterface<TInterface, TClass>(TClass instance)
            where TInterface : class
            where TClass : class, TInterface {
            RegisterInterface<TInterface>(instance);
        }

        /// <summary>Receives the server's calls on <typeparamref name="TInterface"/> with an instance <paramref name="factory"/> creates per call.</summary>
        public void RegisterInterface<TInterface, TClass>(Func<IServiceProvider, TClass> factory)
            where TInterface : class
            where TClass : class, TInterface {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _connectionContext.MessageHandler.RegisterInterface<TInterface>(sp => factory(sp));
        }
    }
}
