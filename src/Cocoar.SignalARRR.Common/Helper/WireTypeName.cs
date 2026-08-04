using System;
using System.Text.RegularExpressions;

namespace Cocoar.SignalARRR.Common.Helper {

    /// <summary>
    /// Renders and resolves the type names that travel between nodes.
    /// </summary>
    /// <remarks>
    /// A name on the wire outlives the process that wrote it, so it must not pin the exact build it
    /// came from. <see cref="Type.AssemblyQualifiedName"/> does exactly that — it carries
    /// <c>Version</c>, <c>Culture</c> and <c>PublicKeyToken</c>, and <see cref="Type.GetType(string)"/>
    /// binds on all of them. During a rolling deployment the two halves of a cluster therefore stop
    /// recognising each other's types the moment the assembly version changes.
    /// <para>
    /// Plain <see cref="Type.FullName"/> is not the answer either, and the reason is easy to miss:
    /// for a <em>closed generic</em> it still embeds the fully assembly-qualified name of every type
    /// argument, version included. <c>List&lt;Order&gt;.FullName</c> ends in
    /// <c>[[MyApp.Order, MyApp, Version=1.0.0.0, …]]</c>. Writing <c>FullName</c> would have fixed
    /// simple types and left every generic result type just as broken, silently.
    /// </para>
    /// <para>
    /// So the name is the assembly-qualified one with the version-bearing parts removed at every
    /// nesting level, leaving <c>Namespace.Type, AssemblyName</c>. The assembly stays — it is what
    /// disambiguates two identically named types — while the build identity goes. Resolution then
    /// binds by partial assembly name, which is version-agnostic by design.
    /// </para>
    /// </remarks>
    public static class WireTypeName {

        /// <summary>
        /// Matches the build-identity parts of an assembly reference, at any nesting depth.
        /// </summary>
        /// <remarks>
        /// Stops at <c>,</c> and <c>]</c> so that a nested generic argument's closing bracket is not
        /// swallowed. Culture and public key token are dropped along with the version: they are just
        /// as build-specific, and keeping them would reintroduce the same coupling through the back
        /// door for strong-named assemblies.
        /// </remarks>
        private static readonly Regex BuildIdentity = new Regex(
            @",\s*(Version|Culture|PublicKeyToken)=[^,\]]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// The name to put on the wire for <paramref name="type"/>.
        /// </summary>
        public static string From(Type type) {
            if (type == null) {
                throw new ArgumentNullException(nameof(type));
            }

            var qualified = type.AssemblyQualifiedName;
            return qualified == null
                ? type.FullName ?? type.Name
                : BuildIdentity.Replace(qualified, string.Empty);
        }

        /// <summary>
        /// Resolves a name produced by <see cref="From"/>, or <c>null</c> when it is not known here.
        /// </summary>
        /// <remarks>
        /// Also accepts a name that still carries build identity, so a node does not go blind when it
        /// is talking to one that has not been updated yet — the parts are stripped before binding.
        /// </remarks>
        public static Type? Resolve(string? name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return null;
            }

            var stripped = BuildIdentity.Replace(name, string.Empty);

            // Partial assembly names bind without regard to version, which is the whole point.
            var resolved = Type.GetType(stripped, throwOnError: false);
            if (resolved != null) {
                return resolved;
            }

            // Last resort for a name whose assembly cannot be located by name but whose type is
            // already loaded — the tolerant scan TypeHelper does for method generic arguments.
            var separator = stripped.IndexOf(',');
            return separator < 0 ? null : TypeHelper.FindType(stripped.Substring(0, separator).Trim());
        }
    }
}
