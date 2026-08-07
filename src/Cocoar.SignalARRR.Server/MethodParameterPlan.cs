using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Mvc;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// The per-parameter binding decisions for one invokable method, resolved once per process
    /// instead of once per message (P-6). <c>GetParameters()</c> clones a fresh array on every
    /// call and <c>GetCustomAttribute</c> re-instantiates the attribute — both sat on the
    /// per-message path for every parameter of every RPC.
    /// </summary>
    /// <remarks>
    /// Keyed on the registry's <see cref="MethodInfo"/> instances, which live for the process —
    /// the cache cannot grow beyond the set of registered methods. Default values are still
    /// materialized at bind time: they are only needed when a caller omits trailing arguments,
    /// and resolving them eagerly would break on open generic parameter types.
    /// </remarks>
    internal sealed class MethodParameterPlan {

        internal enum ParameterKind : byte {
            Value,
            CancellationToken,
            FromServices,
            Stream,
        }

        internal readonly struct Entry {
            internal Entry(ParameterInfo parameter, ParameterKind kind) {
                Parameter = parameter;
                Kind = kind;
            }

            internal ParameterInfo Parameter { get; }
            internal ParameterKind Kind { get; }
        }

        private static readonly ConcurrentDictionary<MethodInfo, MethodParameterPlan> Cache = new();

        internal static MethodParameterPlan For(MethodInfo methodInfo) =>
            Cache.GetOrAdd(methodInfo, static m => new MethodParameterPlan(m));

        internal Entry[] Entries { get; }

        private MethodParameterPlan(MethodInfo methodInfo) {
            var parameters = methodInfo.GetParameters();
            Entries = new Entry[parameters.Length];

            for (var i = 0; i < parameters.Length; i++) {
                var p = parameters[i];

                ParameterKind kind;
                if (p.ParameterType == typeof(CancellationToken)) {
                    kind = ParameterKind.CancellationToken;
                } else if (p.GetCustomAttribute<FromServicesAttribute>() != null) {
                    kind = ParameterKind.FromServices;
                } else if (typeof(Stream).IsAssignableFrom(p.ParameterType)) {
                    kind = ParameterKind.Stream;
                } else {
                    kind = ParameterKind.Value;
                }

                Entries[i] = new Entry(p, kind);
            }
        }
    }
}
