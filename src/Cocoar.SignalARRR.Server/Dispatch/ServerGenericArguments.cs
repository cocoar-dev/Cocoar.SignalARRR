using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Cocoar.SignalARRR.Server {
    /// <summary>Matches the type argument names a client sent against the types a method declared.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ServerGenericArguments {

        /// <summary>
        /// Whether <paramref name="names"/> name exactly <paramref name="types"/>, in order. A name is
        /// the type's <see cref="Type.FullName"/> (what the .NET client sends), or, without a dot,
        /// <c>System.&lt;name&gt;</c> regardless of case (what some Kotlin contracts send).
        /// </summary>
        public static bool Match(IReadOnlyList<string> names, params Type[] types) {
            if (names.Count != types.Length) return false;

            for (var i = 0; i < types.Length; i++) {
                if (!Matches(names[i], types[i].FullName)) return false;
            }

            return true;
        }

        private static bool Matches(string name, string? fullName) {
            if (fullName == null) return false;
            if (string.Equals(name, fullName, StringComparison.Ordinal)) return true;
            return name.IndexOf('.') < 0 && string.Equals("System." + name, fullName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
