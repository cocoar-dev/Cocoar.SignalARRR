using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {

    /// <summary>
    /// The authorization metadata that applies to one remotely invokable method.
    /// </summary>
    public sealed class SignalARRRAuthorizationPlan {

        internal SignalARRRAuthorizationPlan(bool allowAnonymous, IReadOnlyList<IAuthorizeData> authorizeData) {
            AllowAnonymous = allowAnonymous;
            AuthorizeData = authorizeData;
        }

        /// <summary>Gets a value indicating whether <c>[AllowAnonymous]</c> applies.</summary>
        public bool AllowAnonymous { get; }

        /// <summary>Gets the authorization data that applies, or an empty list if the method is unrestricted.</summary>
        public IReadOnlyList<IAuthorizeData> AuthorizeData { get; }

        /// <summary>Gets a value indicating whether authorization has to be evaluated at all.</summary>
        public bool RequiresAuthorization => !AllowAnonymous && AuthorizeData.Count > 0;
    }

    public static class MethodInfoExtensions {

        // Attributes are immutable metadata, so the resolved plan is cached for the lifetime of the
        // process. This also keeps the extra work introduced below (walking the interface map and
        // the base chain) off the per-message path -- the previous implementation re-ran
        // GetCustomAttributes, which re-instantiates attribute objects, on every single invocation.
        private static readonly ConcurrentDictionary<MethodInfo, SignalARRRAuthorizationPlan> PlanCache = new();

        /// <summary>
        /// Resolves the authorization metadata that applies to <paramref name="methodInfo"/>.
        /// </summary>
        /// <remarks>
        /// Resolution walks, in order, until a level supplies authorization data:
        /// <list type="number">
        /// <item>the method itself, plus the interface methods it implements;</item>
        /// <item>the type the method was reached through and its base classes, up to but excluding
        /// the SignalARRR base types;</item>
        /// <item>the hub a <see cref="ServerMethods{T}"/> class belongs to.</item>
        /// </list>
        /// <para>
        /// Three things were wrong before. The type level was read from <c>DeclaringType</c>, so a
        /// method inherited from an undecorated base class lost the <c>[Authorize]</c> of the
        /// derived class it was registered from. The interface level did not exist, so for the
        /// interface dispatch path -- where the stored method is the interface declaration -- the
        /// implementation's attributes were invisible. And only the concrete
        /// <see cref="AuthorizeAttribute"/> was collected, so any custom attribute implementing
        /// <see cref="IAuthorizeData"/>, which is the actual ASP.NET Core contract, was skipped.
        /// Each of those produced an empty result, and an empty result means "allow".
        /// </para>
        /// </remarks>
        public static SignalARRRAuthorizationPlan GetAuthorizationPlan(this MethodInfo methodInfo) =>
            PlanCache.GetOrAdd(methodInfo, static m => BuildPlan(m));

        /// <summary>
        /// Resolves the authorization data that applies to <paramref name="methodInfo"/>.
        /// </summary>
        public static IReadOnlyList<IAuthorizeData> GetAuthorizeData(this MethodInfo methodInfo) =>
            methodInfo.GetAuthorizationPlan().AuthorizeData;

        private static SignalARRRAuthorizationPlan BuildPlan(MethodInfo methodInfo) {

            var methodLevel = new List<MemberInfo> { methodInfo };
            methodLevel.AddRange(GetInterfaceDeclarations(methodInfo));

            var typeLevel = GetDeclaringTypeChain(methodInfo).Cast<MemberInfo>().ToList();

            var hubLevel = new List<MemberInfo>();
            var hubType = GetHubTypeForServerMethods(methodInfo.ReflectedType ?? methodInfo.DeclaringType);
            if (hubType != null) {
                hubLevel.Add(hubType);
            }

            // AllowAnonymous wins wherever it appears -- that is how ASP.NET Core treats it, and it
            // is the safe direction to be permissive in, because it is an explicit opt-out.
            var allowAnonymous = methodLevel.Concat(typeLevel).Concat(hubLevel)
                .Any(member => member.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any());

            // Override semantics, matching the previous behaviour: the most specific level that
            // supplies any authorization data wins, rather than combining all levels. Changing that
            // to ASP.NET Core's combining semantics would be stricter, but it would also silently
            // start denying callers for whom a method-level [Authorize] currently replaces a
            // class-level one -- that is a deliberate decision, not a side effect of this fix.
            foreach (var level in new[] { methodLevel, typeLevel, hubLevel }) {
                var data = level.SelectMany(GetAuthorizeDataFrom).ToList();
                if (data.Count > 0) {
                    return new SignalARRRAuthorizationPlan(allowAnonymous, data);
                }
            }

            return new SignalARRRAuthorizationPlan(allowAnonymous, Array.Empty<IAuthorizeData>());
        }

        private static IEnumerable<IAuthorizeData> GetAuthorizeDataFrom(MemberInfo member) =>
            member.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>();

        /// <summary>
        /// Returns the interface methods <paramref name="methodInfo"/> implements, so that
        /// <c>[Authorize]</c> on a contract and on its implementation are both honoured.
        /// </summary>
        private static IEnumerable<MethodInfo> GetInterfaceDeclarations(MethodInfo methodInfo) {

            var target = methodInfo.ReflectedType ?? methodInfo.DeclaringType;
            if (target == null || target.IsInterface) {
                yield break;
            }

            var baseDefinition = methodInfo.GetBaseDefinition();

            foreach (var interfaceType in target.GetInterfaces()) {

                InterfaceMapping mapping;
                try {
                    mapping = target.GetInterfaceMap(interfaceType);
                } catch (ArgumentException) {
                    // Generic type definitions and a few exotic cases cannot be mapped.
                    continue;
                }

                for (var i = 0; i < mapping.TargetMethods.Length; i++) {
                    var candidate = mapping.TargetMethods[i];
                    if (candidate == methodInfo || candidate.GetBaseDefinition() == baseDefinition) {
                        yield return mapping.InterfaceMethods[i];
                    }
                }
            }
        }

        /// <summary>
        /// Returns the type the method was reached through and its base classes, stopping before the
        /// SignalARRR infrastructure types.
        /// </summary>
        /// <remarks>
        /// <c>ReflectedType</c> rather than <c>DeclaringType</c>: methods are registered from the
        /// concrete class, so that is the type whose <c>[Authorize]</c> the caller expects to apply,
        /// even when the method itself is declared on a base class.
        /// </remarks>
        private static IEnumerable<Type> GetDeclaringTypeChain(MethodInfo methodInfo) {

            var current = methodInfo.ReflectedType ?? methodInfo.DeclaringType;

            while (current != null && !IsInfrastructureType(current)) {
                yield return current;
                current = current.BaseType;
            }
        }

        // Walking stops here: a user hub reaches HARRR, a ServerMethods class reaches
        // ServerMethods<THub>. Neither the SignalARRR base types nor anything above them (Hub,
        // object) can carry authorization the user intended.
        private static bool IsInfrastructureType(Type type) =>
            type == typeof(object)
            || type == typeof(ServerMethods)
            || type == typeof(HARRR)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ServerMethods<>));

        /// <summary>
        /// Resolves the hub type a <see cref="ServerMethods{T}"/> class belongs to by walking the
        /// inheritance chain to the closed <c>ServerMethods&lt;THub&gt;</c> base.
        /// </summary>
        internal static Type? GetHubTypeForServerMethods(Type? type) {

            for (var current = type?.BaseType; current != null; current = current.BaseType) {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(ServerMethods<>)) {
                    var hubType = current.GenericTypeArguments.FirstOrDefault();
                    return hubType != null && typeof(HARRR).IsAssignableFrom(hubType) ? hubType : null;
                }
            }

            return null;
        }
    }
}
