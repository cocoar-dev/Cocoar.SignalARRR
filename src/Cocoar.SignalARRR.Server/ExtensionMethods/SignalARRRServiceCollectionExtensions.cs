using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cocoar.Reflectensions;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Common.Interfaces;
using Cocoar.SignalARRR.Common.Serialization;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection {
    public static class SignalARRRServiceCollectionExtensions {
        public static IServiceCollection AddSignalARRR(this IServiceCollection serviceCollection, Action<SignalARRRServerOptionsBuilder>? options = null) {

            SignalARRRServerOptions serverOptions = options?.InvokeAction() ?? new SignalARRRServerOptionsBuilder();

            AddSignalARRRMethods(serviceCollection, serverOptions);
            serviceCollection.AddSingleton(serverOptions);
            // Protocol serializer handles both JSON and MessagePack values.
            // JsonProtocolSerializer handles JsonElement natively, and its JSON round-trip
            // fallback works for MessagePack-deserialized values (plain .NET objects) too.
            // SignalR supports both protocols simultaneously — different clients can use different protocols.
            serviceCollection.AddSingleton<IProtocolSerializer, JsonProtocolSerializer>();
            serviceCollection.AddSingleton(sp => new ServerPushStreamManager(
                sp.GetRequiredService<SignalARRRServerOptions>().UploadSlotExpiration));
            serviceCollection.AddSingleton(sp => new ServerStreamManager(
                sp.GetRequiredService<SignalARRRServerOptions>().StreamIdleTimeout));
            serviceCollection.AddSingleton<InMemoryHARRRClientManager>();
            serviceCollection.AddSingleton<IHARRRClientManager>(sp => sp.GetRequiredService<InMemoryHARRRClientManager>());
            // TryAdd, not Add, and not because these are extension points — they are internal and
            // not replaceable from outside this assembly (AF-3). It is here so that registration
            // order does not matter: Cocoar.SignalARRR.Server.Backplane.Redis swaps its
            // implementation in with Replace, and a consumer who calls AddSignalARRRRedisBackplane
            // *before* AddSignalARRR would otherwise get the disabled default appended afterwards
            // and silently win, leaving a configured cluster running single-node.
            serviceCollection.TryAddSingleton<LocalSignalARRRBackplaneDispatcher>();
            serviceCollection.TryAddSingleton<ISignalARRRBackplane, DisabledSignalARRRBackplane>();
            serviceCollection.TryAddSingleton<ISignalARRRConnectionRegistry, DisabledSignalARRRConnectionRegistry>();
            serviceCollection.AddSingleton<ClientManager>(sp => new ClientManager(sp.GetRequiredService<IHARRRClientManager>(), sp));
            serviceCollection.AddTransient(typeof(ClientContextDispatcher<>));

            // This one *is* an extension point, which is why the interface is public: register your
            // own before this call and it stays.
            serviceCollection.TryAddSingleton<ITransportAuthRevalidationService, DefaultTransportAuthRevalidationService>();

            return serviceCollection;
        }


        /// <summary>
        /// Returns the methods of a <see cref="ServerMethods{T}"/> class that may be invoked over RPC.
        /// </summary>
        /// <remarks>
        /// Only members the user actually declared are invokable. Property accessors
        /// (<see cref="MethodBase.IsSpecialName"/>) and everything inherited from
        /// <see cref="ServerMethods"/> or <see cref="object"/> are infrastructure: none of them carry
        /// <c>[Authorize]</c>, so registering them would expose e.g. <c>get_ClientContext</c> —
        /// which returns the caller's principal, claims, client certificate and remote IP — as an
        /// anonymously callable endpoint. Inherited members are otherwise kept, so a user-defined
        /// intermediate base class still contributes its methods.
        /// </remarks>
        /// <summary>
        /// Resolves the hub type a <see cref="ServerMethods{T}"/> class belongs to, by walking the
        /// inheritance chain until the closed <c>ServerMethods&lt;THub&gt;</c> base is found.
        /// </summary>
        /// <remarks>
        /// Walking is required: the class need not derive from <c>ServerMethods&lt;THub&gt;</c>
        /// directly. With a user-defined intermediate base class, <c>BaseType</c> is that class and
        /// its <c>GenericTypeArguments</c> is empty, so indexing it blindly threw
        /// <see cref="IndexOutOfRangeException"/> out of <c>AddSignalARRR</c> at startup.
        /// </remarks>
        private static Type? GetHubTypeForServerMethods(Type type) {
            for (var current = type.BaseType; current != null; current = current.BaseType) {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ServerMethods<>))
                    return current.GenericTypeArguments[0];
            }

            return null;
        }

        private static IEnumerable<MethodInfo> GetInvokableServerMethods(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName
                            && m.DeclaringType != typeof(object)
                            && m.DeclaringType != typeof(ServerMethods));

        /// <summary>
        /// Builds the allow-list of what clients may call: the hub's own methods, each
        /// <see cref="ServerMethods{T}"/> class's methods, and every interface either declares.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Every declared interface becomes wire-reachable.</strong> There is no filter on
        /// intent — an interface a class implements for dependency injection or testing is
        /// registered exactly like one written as a contract, and each of its members is callable
        /// by any client that can reach the hub. <c>[SignalARRRContract]</c> does not gate this and
        /// is not checked here: it marks an interface for the source generator, and gating on it
        /// would break the DynamicProxy, .NET Framework, TypeScript and Swift clients, which
        /// legitimately use contracts that carry no attribute. Gating would also fail silently —
        /// an unregistered interface only shows up as "not registered" at call time.
        /// </para>
        /// <para>
        /// Authorization is unaffected: the plan resolved for each member still walks the
        /// implementing class and the owning hub. The exposure is surface, not a bypass. Keep
        /// interfaces that are not meant for clients off the class and on a collaborator it holds.
        /// </para>
        /// </remarks>
        private static void AddSignalARRRMethods(IServiceCollection serviceCollection, SignalARRRServerOptions serverOptions) {



            Dictionary<Type, ISignalARRRMethodsCollection> hubMethodsDictionary = new();
            Dictionary<Type, ISignalARRRInterfaceCollection> interfaceDictionary = new();



            var harrTypes = serverOptions.AssembliesContainingServerMethods.SelectMany(ass =>
                ass.GetTypes().WhichInheritFromClass(typeof(HARRR)));

            foreach (var harrType in harrTypes) {
                var methodsCollection = new SignalARRRMethodsCollection(ServerWireSlots.Policy);
                // DeclaredOnly keeps the HARRR/Hub infrastructure out; IsSpecialName additionally keeps
                // the accessors of any property declared on the user's own hub from becoming endpoints.
                var messageMethodsWithName = harrType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Select(m => (MethodInfo: m, Attribute: m.GetCustomAttribute<MessageNameAttribute>()));

                foreach (var (methodInfo, methodNameAttribute) in messageMethodsWithName) {
                    var methodName = methodNameAttribute?.Name ?? methodInfo.Name;
                    methodsCollection.AddMethod(methodName, methodInfo);
                }

                hubMethodsDictionary[harrType] = methodsCollection;

                var directInterfaces = harrType.GetDirectInterfaces().ToList();
                if (directInterfaces.Any()) {
                    var interfaceCollection = new SignalARRRInterfaceCollection(ServerWireSlots.Policy);
                    foreach (var @interface in directInterfaces) {
                        interfaceCollection.RegisterInterface(@interface, harrType);
                    }

                    interfaceDictionary[harrType] = interfaceCollection;
                }

            }


            var serverMethodsFromAllAssemblies = serverOptions.AssembliesContainingServerMethods
                 .SelectMany(ass => ass.GetTypes().WhichInheritFromClass(typeof(ServerMethods<>)))
                 // Abstract classes are shared bases, not endpoints. They cannot be constructed, so
                 // registering them as transient services would only fail at resolution time.
                 .Where(t => !t.IsAbstract)
                 .ToList();
            var grouped = serverMethodsFromAllAssemblies.GroupBy(GetHubTypeForServerMethods).ToList();

            foreach (var grouping in grouped) {

                if (grouping.Key == null || !hubMethodsDictionary.TryGetValue(grouping.Key, out var coll))
                    continue;


                foreach (var type in grouping) {

                    serviceCollection.AddTransient(type);

                    var rootName = type.GetCustomAttribute<MessageNameAttribute>()?.Name ?? type.Name;
                    var methodsWithName = GetInvokableServerMethods(type).Select(m => (MethodInfo: m, Attribute: m.GetCustomAttribute<MessageNameAttribute>()));
                    foreach (var (methodInfo, methodNameAttribute) in methodsWithName) {
                        var methodName = methodNameAttribute?.Name ?? methodInfo.Name;
                        var concatNames = $"{rootName}.{methodName}";
                        coll.AddMethod(concatNames, methodInfo);
                    }

                    var directInterfaces = type.GetDirectInterfaces().ToList();
                    if (directInterfaces.Any()) {

                        if (!interfaceDictionary.TryGetValue(type, out var interfaceCollection))
                            interfaceCollection = new SignalARRRInterfaceCollection(ServerWireSlots.Policy);

                        foreach (var @interface in directInterfaces) {
                            interfaceCollection.RegisterInterface(@interface, type);
                        }

                        interfaceDictionary[GetHubTypeForServerMethods(type)!] = interfaceCollection;
                    }

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

        }
    }
}
