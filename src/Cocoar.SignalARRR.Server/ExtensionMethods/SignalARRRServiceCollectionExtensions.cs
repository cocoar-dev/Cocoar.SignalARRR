using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cocoar.SignalARRR.Common.Serialization;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection {
    public static class SignalARRRServiceCollectionExtensions {
        public static IServiceCollection AddSignalARRR(this IServiceCollection serviceCollection, Action<SignalARRRServerOptionsBuilder>? options = null) {

            var optionsBuilder = new SignalARRRServerOptionsBuilder();
            options?.Invoke(optionsBuilder);
            SignalARRRServerOptions serverOptions = optionsBuilder;

            AddSignalARRRMethods(serviceCollection, serverOptions);
            serviceCollection.AddSingleton(serverOptions);
            // Protocol serializer handles both JSON and MessagePack values.
            // JsonProtocolSerializer handles JsonElement natively, and its JSON round-trip
            // fallback works for MessagePack-deserialized values (plain .NET objects) too.
            // SignalR supports both protocols simultaneously — different clients can use different protocols.
            // It converts with the hub protocol's JSON options, the ones the arguments were written with.
            serviceCollection.PostConfigure<Microsoft.AspNetCore.SignalR.JsonHubProtocolOptions>(o => SignalARRRJson.AddTo(o.PayloadSerializerOptions));
            serviceCollection.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(o => Microsoft.AspNetCore.SignalR.HubOptionsExtensions.AddFilter(o, new SignalARRRHubFilter()));
            serviceCollection.AddSingleton<ServerPayloadOptions>();
            serviceCollection.AddHostedService<ServerWireTypeCheck>();
            serviceCollection.AddSingleton<IProtocolSerializer>(sp => new JsonProtocolSerializer(sp.GetRequiredService<ServerPayloadOptions>().Options));
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
            serviceCollection.TryAddSingleton<ClusterSubjectRegistry>();
            serviceCollection.TryAddSingleton<ISignalARRRBackplane, DisabledSignalARRRBackplane>();
            serviceCollection.TryAddSingleton<ISignalARRRConnectionRegistry, DisabledSignalARRRConnectionRegistry>();
            serviceCollection.AddSingleton<ClientManager>(sp => new ClientManager(sp.GetRequiredService<IHARRRClientManager>(), sp));

            // This one *is* an extension point, which is why the interface is public: register your
            // own before this call and it stays.
            serviceCollection.TryAddSingleton<ITransportAuthRevalidationService, DefaultTransportAuthRevalidationService>();

            return serviceCollection;
        }


        /// <summary>
        /// Builds the allow-list of what clients may call from the tables the source generator
        /// produced for the configured assemblies: the hubs' own methods, each
        /// <see cref="ServerMethods{T}"/> class's methods, and every interface either declares.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <strong>Every declared interface becomes wire-reachable.</strong> There is no filter on
        /// intent — an interface a class implements for dependency injection or testing is
        /// registered exactly like one written as a contract, and each of its members is callable
        /// by any client that can reach the hub. <c>[SignalARRRContract]</c> does not gate this: it
        /// marks an interface for proxy generation, and gating on it would break the TypeScript,
        /// Swift and Kotlin clients, which legitimately call contracts that carry no attribute.
        /// </para>
        /// <para>
        /// Authorization is unaffected: the plan generated for each member still walks the
        /// implementing class and the owning hub. The exposure is surface, not a bypass. Keep
        /// interfaces that are not meant for clients off the class and on a collaborator it holds.
        /// </para>
        /// <para>
        /// The tables come from module initializers; each configured assembly's runs here, before
        /// the registry is read. An assembly added with <c>AddServerMethodsFrom</c> that has no table
        /// was built without the generator, and its methods would silently be missing, so that fails.
        /// </para>
        /// </remarks>
        private static void AddSignalARRRMethods(IServiceCollection serviceCollection, SignalARRRServerOptions serverOptions) {

            var assemblies = serverOptions.AssembliesContainingServerMethods.Where(a => a != null).Distinct().ToList();
            foreach (var assembly in assemblies) {
                RunModuleInitializer(assembly);
            }

            var registrations = ServerDispatchRegistry.Snapshot()
                .Where(r => assemblies.Contains(r.DeclaringType.Assembly))
                .ToList();

            var entryAssembly = Assembly.GetEntryAssembly();
            foreach (var assembly in assemblies) {
                if (assembly != entryAssembly && registrations.All(r => r.DeclaringType.Assembly != assembly)) {
                    throw new InvalidOperationException(
                        $"AddServerMethodsFrom: '{assembly.GetName().Name}' has no generated SignalARRR dispatch table. " +
                        "Hubs and ServerMethods classes are found by the source generator at compile time; the project that declares them " +
                        "must reference Cocoar.SignalARRR.Server and be rebuilt.");
                }
            }

            var tables = new Dictionary<Type, ServerMethodTable>();
            foreach (var hub in registrations.Where(r => r.IsHub)) {
                var table = new ServerMethodTable();
                foreach (var method in hub.Methods) table.Add(method);
                tables[hub.HubType] = table;
            }

            foreach (var serverMethods in registrations.Where(r => !r.IsHub)) {
                // A ServerMethods class of a hub that is not among the configured assemblies has
                // nowhere to be called from, exactly as before.
                if (!tables.TryGetValue(serverMethods.HubType, out var table)) continue;

                foreach (var method in serverMethods.Methods) table.Add(method);
                serverMethods.RegisterServices!(serviceCollection);
            }

            serviceCollection.AddSingleton(new ServerMethodTables(tables));
        }

        [UnconditionalSuppressMessage("AOT", "IL2026", Justification = "Runs the module initializer of an assembly that is already loaded; NativeAOT runs them all at startup anyway.")]
        private static void RunModuleInitializer(Assembly assembly) {
            try {
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            } catch (PlatformNotSupportedException) {
                // NativeAOT runs every module initializer at startup.
            }
        }
    }
}
