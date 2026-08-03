using System;
using System.Collections.Concurrent;
using System.Reflection;
using Cocoar.SignalARRR.Common.Helper;
using Cocoar.SignalARRR.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Common {
    public class SignalARRRInterfaceCollection : ISignalARRRInterfaceCollection {

        private ConcurrentDictionary<Type, ClientInterfaceMethodsCache> RegisteredTypes = new ConcurrentDictionary<Type, ClientInterfaceMethodsCache>();

        public void RegisterInterface<TInterface, TClass>() where TClass : class, TInterface {

            TClass Factory(IServiceProvider sp) {
                var fromServiceProvider = sp.GetService(typeof(TClass));
                if (fromServiceProvider != null) {
                    return (TClass)fromServiceProvider;
                }

                return Activator.CreateInstance<TClass>();
            }

            RegisterInterface<TInterface, TClass>((Func<IServiceProvider, TClass>)Factory);
        }
        public void RegisterInterface<TInterface, TClass>(TClass instance) where TClass : class, TInterface {

            RegisterInterface<TInterface, TClass>((sp) => instance);
        }
        public void RegisterInterface<TInterface, TClass>(Func<IServiceProvider, TClass> factory)
            where TClass : class, TInterface {

            RegisteredTypes.AddOrUpdate(typeof(TInterface),
                type => new ClientInterfaceMethodsCache(factory, type, typeof(TClass)),
                (type, del) => new ClientInterfaceMethodsCache(factory, type, typeof(TClass)));
        }

        public void RegisterInterface(Type interfaceType, Type instanceType) {

            object Factory(IServiceProvider sp) {
                var fromServiceProvider = sp.GetService(instanceType);
                if (fromServiceProvider != null) {
                    return fromServiceProvider;
                }

                return ActivatorUtilities.CreateInstance(sp, instanceType);

                //return Activator.CreateInstance(instanceType);
            }

            RegisterInterface(interfaceType, Factory, instanceType);
        }

        public void RegisterInterface(Type interfaceType, object instance) {
            RegisterInterface(interfaceType, (sp) => instance, instance.GetType());
        }

        public void RegisterInterface(Type interfaceType, Func<IServiceProvider, object> factory) {
            // No implementation type available on this overload, so the cache falls back to the
            // interface declarations and only attributes on the contract are visible.
            RegisterInterface(interfaceType, factory, implementationType: null);
        }

        public void RegisterInterface(Type interfaceType, Func<IServiceProvider, object> factory, Type? implementationType) {
            RegisteredTypes.AddOrUpdate(interfaceType,
                type => new ClientInterfaceMethodsCache(factory, type, implementationType),
                (type, del) => new ClientInterfaceMethodsCache(factory, type, implementationType));
        }


        public (Delegate Factory, MethodInfo MethodInfo) GetInvokeInformation(string name) {

            if (!name.Contains("|")) {
                throw new ArgumentException($"'{name}' has no Interface Information");
            }

            var splitted = name.Split("|".ToCharArray(), 2);
            var type = TypeHelper.FindType(splitted[0]);
            var methodName = splitted[1];

            if (type != null && RegisteredTypes.TryGetValue(type, out var methodsCache)) {
                return methodsCache.GetInvokeInformations(methodName);
            }

            throw new Exception($"Interface '{name}' not found!");
        }
    }
}
