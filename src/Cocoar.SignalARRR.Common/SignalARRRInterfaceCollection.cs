using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Cocoar.SignalARRR.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Common {
    public class SignalARRRInterfaceCollection : ISignalARRRInterfaceCollection {

        private ConcurrentDictionary<Type, ClientInterfaceMethodsCache> RegisteredTypes = new ConcurrentDictionary<Type, ClientInterfaceMethodsCache>();

        /// <summary>
        /// The registered interfaces indexed by the names that may appear on the wire.
        /// </summary>
        /// <remarks>
        /// This index *is* the allow-list. Dispatch used to resolve the incoming name through
        /// <see cref="Helper.TypeHelper.FindType"/>, which scans every loaded assembly on a miss and
        /// caches every name it is ever asked about — including misses — for the lifetime of the
        /// process, all under one global lock. That lookup happens before the authorization check,
        /// so an unauthenticated client could drive it with a loop of random names and cost the
        /// server a full multi-assembly scan plus a permanent dictionary entry per message.
        /// Resolving against what was actually registered removes the scan, the unbounded growth and
        /// the global lock in one go.
        /// </remarks>
        private readonly ConcurrentDictionary<string, ClientInterfaceMethodsCache> _byWireName =
            new ConcurrentDictionary<string, ClientInterfaceMethodsCache>(StringComparer.Ordinal);

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

            // Routed through the non-generic overload so that every registration path — generic or
            // not — goes through a single place. Keeping a second one meant the wire-name index was
            // only populated for half of them.
            RegisterInterface(typeof(TInterface), sp => factory(sp), typeof(TClass));
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
            var cache = RegisteredTypes.AddOrUpdate(interfaceType,
                type => new ClientInterfaceMethodsCache(factory, type, implementationType),
                (type, del) => new ClientInterfaceMethodsCache(factory, type, implementationType));

            // Both proxy flavours put the interface's FullName on the wire (the source generator via
            // its Prefix constant, the DispatchProxy directly). The assembly-qualified name is
            // indexed as well so a caller that sends the more specific form still resolves.
            foreach (var wireName in GetWireNames(interfaceType)) {
                _byWireName[wireName] = cache;
            }
        }

        private static IEnumerable<string> GetWireNames(Type interfaceType) {
            if (interfaceType.FullName is { } fullName) {
                yield return fullName;
            }

            if (interfaceType.AssemblyQualifiedName is { } assemblyQualifiedName) {
                yield return assemblyQualifiedName;
            }
        }


        public (Delegate Factory, MethodInfo MethodInfo) GetInvokeInformation(string name) {

            var separator = name.IndexOf('|');
            if (separator < 0) {
                throw new ArgumentException($"'{name}' has no Interface Information");
            }

            // Substring rather than Split: this runs per message, and Split allocates a char[] and a
            // string[] on top of the two substrings.
            var interfaceName = name.Substring(0, separator);
            var methodName = name.Substring(separator + 1);

            if (_byWireName.TryGetValue(interfaceName, out var methodsCache)) {
                return methodsCache.GetInvokeInformations(methodName);
            }

            throw new Exception($"Interface '{interfaceName}' is not registered.");
        }
    }
}
