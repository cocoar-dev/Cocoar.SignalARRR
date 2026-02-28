using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cocoar.Reflectensions;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    public static class ServiceCollectionExtensions {
        public static IServiceCollection AddSignalARRR(this IServiceCollection serviceCollection, Action<SignalARRRServerOptionsBuilder>? options = null) {

            SignalARRRServerOptions serverOptions = options?.InvokeAction() ?? new SignalARRRServerOptionsBuilder();

            AddSignalARRRMethods(serviceCollection, serverOptions);
            serviceCollection.AddSingleton<ServerPushStreamManager>();
            serviceCollection.AddSingleton<ServerStreamManager>();
            serviceCollection.AddSingleton<InMemoryHARRRClientManager>();
            serviceCollection.AddSingleton<IHARRRClientManager>(sp => sp.GetRequiredService<InMemoryHARRRClientManager>());
            serviceCollection.AddSingleton<ClientManager>(sp => new ClientManager(sp.GetRequiredService<IHARRRClientManager>()));
            serviceCollection.AddTransient(typeof(ClientContextDispatcher<>));

            return serviceCollection;
        }


        private static void AddSignalARRRMethods(IServiceCollection serviceCollection, SignalARRRServerOptions serverOptions) {



            Dictionary<Type, ISignalARRRMethodsCollection> hubMethodsDictionary = new();
            Dictionary<Type, ISignalARRRInterfaceCollection> interfaceDictionary = new();
            //serverOptions.AssembliesContainingServerMethods
            //.SelectMany(ass => ass.GetTypes().WhichInheritFromClass(typeof(HARRR))).ToDictionary(type => type,
            //    hubType => {

            //        var genColl = new SignalARRRMethodsCollection();

            //        var messageMethodsWithName = hubType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m =>
            //            (MethodInfo: m, Attribute: m.GetCustomAttribute<MessageNameAttribute>()));

            //        foreach (var (methodInfo, methodNameAttribute) in messageMethodsWithName) {
            //            var methodName = methodNameAttribute?.Name ?? methodInfo.Name;
            //            genColl.AddMethod(methodName, methodInfo);
            //        }

            //        return (ISignalARRRMethodsCollection)genColl;
            //    });



            var harrTypes = serverOptions.AssembliesContainingServerMethods.SelectMany(ass =>
                ass.GetTypes().WhichInheritFromClass(typeof(HARRR)));

            foreach (var harrType in harrTypes) {
                var methodsCollection = new SignalARRRMethodsCollection();
                var messageMethodsWithName = harrType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m =>
                    (MethodInfo: m, Attribute: m.GetCustomAttribute<MessageNameAttribute>()));

                foreach (var (methodInfo, methodNameAttribute) in messageMethodsWithName) {
                    var methodName = methodNameAttribute?.Name ?? methodInfo.Name;
                    methodsCollection.AddMethod(methodName, methodInfo);
                }

                hubMethodsDictionary[harrType] = methodsCollection;

                var directInterfaces = harrType.GetDirectInterfaces().ToList();
                if (directInterfaces.Any()) {
                    var interfaceCollection = new SignalARRRInterfaceCollection();
                    foreach (var @interface in directInterfaces) {
                        interfaceCollection.RegisterInterface(@interface, harrType);
                    }

                    interfaceDictionary[harrType] = interfaceCollection;
                }

            }


            var serverMethodsFromAllAssemblies = serverOptions.AssembliesContainingServerMethods
                 .SelectMany(ass => ass.GetTypes().WhichInheritFromClass(typeof(ServerMethods<>))).ToList();
            var grouped = serverMethodsFromAllAssemblies.GroupBy(ass => ass.BaseType?.GenericTypeArguments[0]).ToList();

            foreach (var grouping in grouped) {

                if (grouping.Key == null || !hubMethodsDictionary.TryGetValue(grouping.Key, out var coll))
                    continue;


                foreach (var type in grouping) {

                    serviceCollection.AddTransient(type);

                    var rootName = type.GetCustomAttribute<MessageNameAttribute>()?.Name ?? type.Name;
                    var methodsWithName = type.GetMethods().Select(m => (MethodInfo: m, Attribute: m.GetCustomAttribute<MessageNameAttribute>()));
                    foreach (var (methodInfo, methodNameAttribute) in methodsWithName) {
                        var methodName = methodNameAttribute?.Name ?? methodInfo.Name;
                        var concatNames = $"{rootName}.{methodName}";
                        coll.AddMethod(concatNames, methodInfo);
                    }

                    var directInterfaces = type.GetDirectInterfaces().ToList();
                    if (directInterfaces.Any()) {

                        if (!interfaceDictionary.TryGetValue(type, out var interfaceCollection))
                            interfaceCollection = new SignalARRRInterfaceCollection();

                        foreach (var @interface in directInterfaces) {
                            interfaceCollection.RegisterInterface(@interface, type);
                        }

                        interfaceDictionary[type.BaseType!.GenericTypeArguments[0]] = interfaceCollection;
                    }

                    //var interfaceCollection1 = new SignalARRRInterfaceCollection();
                    //foreach (var @interface in type.GetInterfaces()) {
                    //    interfaceCollection1.RegisterInterface(@interface, type);
                    //}

                    //var n = type.BaseType?.GenericTypeArguments[0].FullName;
                    //serviceCollection.AddNamedTransient<ISignalARRRInterfaceCollection>(n, _ => interfaceCollection1);

                }
            }

            foreach (var (key, value) in hubMethodsDictionary) {
                var n = key.FullName;
                serviceCollection.AddKeyedSingleton<ISignalARRRMethodsCollection>(n, (_, _) => value);
            }

            foreach (var (key, value) in interfaceDictionary) {
                var n = key.FullName;
                serviceCollection.AddKeyedSingleton<ISignalARRRInterfaceCollection>(n, (_, _) => value);
            }

            //foreach (var (key, (collection, serviceType)) in hubMethodsDictionary)
            //{
            //    serviceCollection.AddSingleton(serviceType, collection);
            //}

        }
    }
}
