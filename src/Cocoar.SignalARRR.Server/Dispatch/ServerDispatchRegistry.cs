using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// What the source generator found at compile time: the hubs, the methods clients may call on
    /// them and the <see cref="ServerMethods{T}"/> classes to register. Filled by the generated
    /// module initializers; read by <c>AddSignalARRR</c>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ServerDispatchRegistry {

        internal sealed class Registration {
            public Registration(Type hubType, Type declaringType, IReadOnlyList<ServerMethodDescriptor> methods, Action<IServiceCollection>? registerServices) {
                HubType = hubType;
                DeclaringType = declaringType;
                Methods = methods;
                RegisterServices = registerServices;
            }

            public Type HubType { get; }
            public Type DeclaringType { get; }
            public IReadOnlyList<ServerMethodDescriptor> Methods { get; }
            /// <summary>Set for a <see cref="ServerMethods{T}"/> class, <c>null</c> for the hub itself.</summary>
            public Action<IServiceCollection>? RegisterServices { get; }
            public bool IsHub => RegisterServices == null;
        }

        private static readonly object Gate = new object();
        private static readonly Dictionary<Type, ServerHubAccessor> Hubs = new Dictionary<Type, ServerHubAccessor>();
        private static readonly List<Registration> Registrations = new List<Registration>();

        /// <summary>Registers a hub and the methods it declares itself, including its interfaces.</summary>
        public static void RegisterHub<THub>(ServerMethodDescriptor[] methods) where THub : HARRR {
            lock (Gate) {
                Hubs[typeof(THub)] = new ServerHubAccessor<THub>();
                // A hub's table is generated once per assembly that compiles it; registering it again
                // (the module initializer run explicitly and implicitly) replaces the earlier copy.
                Registrations.RemoveAll(r => r.IsHub && r.DeclaringType == typeof(THub));
                Registrations.Add(new Registration(typeof(THub), typeof(THub), methods, null));
            }
        }

        /// <summary>Registers a <see cref="ServerMethods{T}"/> class of <paramref name="hubType"/> and its methods.</summary>
        public static void RegisterServerMethods<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(Type hubType, ServerMethodDescriptor[] methods) where T : ServerMethods {
            lock (Gate) {
                Registrations.RemoveAll(r => !r.IsHub && r.DeclaringType == typeof(T));
                Registrations.Add(new Registration(hubType, typeof(T), methods, services => services.AddTransient<T>()));
            }
        }

        internal static IReadOnlyList<Registration> Snapshot() {
            lock (Gate) {
                return Registrations.ToList();
            }
        }

        /// <summary>The accessor of a hub the generator registered.</summary>
        internal static ServerHubAccessor Hub(Type hubType) {
            lock (Gate) {
                if (Hubs.TryGetValue(hubType, out var accessor)) return accessor;
            }

            throw new InvalidOperationException(
                $"Hub '{hubType.FullName}' has no generated dispatch table. Its project must reference Cocoar.SignalARRR.Server, " +
                "which runs the source generator, and be rebuilt.");
        }

        /// <summary>A registered hub by <see cref="Type.FullName"/> or by the name the backplane sends.</summary>
        internal static Type? HubByName(string name) {
            if (string.IsNullOrEmpty(name)) return null;

            // The backplane sends the assembly-qualified name without version, culture and key; only
            // the part before the first comma has to match.
            var comma = name.IndexOf(',');
            var fullName = comma < 0 ? name : name.Substring(0, comma).Trim();

            lock (Gate) {
                foreach (var hubType in Hubs.Keys) {
                    if (string.Equals(hubType.FullName, fullName, StringComparison.Ordinal)) return hubType;
                }
            }

            return null;
        }
    }
}
