using System;
using System.Reflection;
using Cocoar.SignalARRR.Common.Attributes;

namespace Cocoar.SignalARRR.Common {

    /// <summary>
    /// Forms the names that contract calls travel under: <c>interface|method</c>.
    /// </summary>
    /// <remarks>
    /// Both halves default to the C# identifier and can be overridden with <see cref="MessageNameAttribute"/>.
    /// That override is the point: without it the C# name <em>is</em> the protocol, so renaming a method
    /// or moving an interface to another namespace silently breaks every TypeScript and Swift client,
    /// which write the name out as a string and get no compiler to check it.
    /// <para>
    /// This is the single place the naming rule lives. It is reached from the registration side
    /// (which builds the allow-list) and from both reflection proxies; the source generator applies the
    /// same rule against Roslyn symbols, since it cannot use reflection.
    /// </para>
    /// </remarks>
    public static class WireName {

        /// <summary>The separator between the interface part and the method part.</summary>
        public const char Separator = '|';

        /// <summary>The name a contract interface is addressed by.</summary>
        public static string ForInterface(Type interfaceType) {
            if (interfaceType == null) throw new ArgumentNullException(nameof(interfaceType));

            var declared = interfaceType.GetCustomAttribute<MessageNameAttribute>(inherit: false)?.Name;
            if (declared == null) {
                return interfaceType.FullName ?? interfaceType.Name;
            }

            Validate(declared, $"interface '{interfaceType.FullName ?? interfaceType.Name}'");
            return declared;
        }

        /// <summary>The name a contract member is addressed by.</summary>
        public static string ForMethod(MethodInfo method) {
            if (method == null) throw new ArgumentNullException(nameof(method));

            var declared = method.GetCustomAttribute<MessageNameAttribute>(inherit: false)?.Name;
            if (declared == null) {
                return method.Name;
            }

            Validate(declared, $"member '{method.DeclaringType?.FullName}.{method.Name}'");
            return declared;
        }

        /// <summary>The full name a call to <paramref name="method"/> on <paramref name="interfaceType"/> travels under.</summary>
        public static string For(Type interfaceType, MethodInfo method) =>
            ForInterface(interfaceType) + Separator + ForMethod(method);

        private static void Validate(string name, string subject) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new InvalidOperationException(
                    $"[MessageName] on {subject} is empty. A wire name has to be a name.");
            }

            // The receiving side splits on the first separator to tell the interface from the method,
            // so a separator inside either half would silently address something else.
            if (name.IndexOf(Separator) >= 0) {
                throw new InvalidOperationException(
                    $"[MessageName(\"{name}\")] on {subject} contains '{Separator}', which separates the interface " +
                    "from the method on the wire. Choose a name without it.");
            }
        }
    }
}
