using System;
using System.Collections.Generic;
using Cocoar.SignalARRR.Common.Helper;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    /// <summary>
    /// Covers the type names the backplane puts on the wire and reads back (F-10).
    /// </summary>
    /// <remarks>
    /// A genuine version skew needs two builds of the same assembly loaded at once, which no test
    /// here can stage. What can be pinned down exactly is the property that makes the skew
    /// survivable: the name must not carry build identity, and resolution must not depend on it.
    /// The skew itself is then simulated by rewriting the version in a name — which is precisely
    /// what the other node would have sent.
    /// </remarks>
    public class WireTypeNameTests {

        private sealed class Probe { }

        public static IEnumerable<object[]> Types => new List<object[]> {
            new object[] { typeof(Probe) },
            new object[] { typeof(string) },
            new object[] { typeof(List<Probe>) },
            new object[] { typeof(Dictionary<string, List<Probe>>) },
            new object[] { typeof(Probe[]) },
            new object[] { typeof(int?) },
        };

        /// <summary>
        /// No build identity anywhere in the name, at any nesting depth.
        /// </summary>
        /// <remarks>
        /// The nested cases are the ones that matter. <c>Type.FullName</c> looks version-free and is
        /// so for a simple type, but a closed generic embeds the fully qualified name of each type
        /// argument — version included. A fix that reached for <c>FullName</c> would have passed the
        /// first case here and failed the rest.
        /// </remarks>
        [Theory]
        [MemberData(nameof(Types))]
        public void A_wire_name_carries_no_build_identity(Type type) {
            var name = WireTypeName.From(type);

            Assert.DoesNotContain("Version=", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Culture=", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PublicKeyToken=", name, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [MemberData(nameof(Types))]
        public void A_wire_name_resolves_back_to_the_same_type(Type type) {
            Assert.Same(type, WireTypeName.Resolve(WireTypeName.From(type)));
        }

        /// <summary>
        /// The assembly is kept, because it is what tells two identically named types apart.
        /// </summary>
        [Fact]
        public void A_wire_name_still_names_its_assembly() {
            var name = WireTypeName.From(typeof(Probe));

            Assert.Contains(typeof(Probe).FullName!, name, StringComparison.Ordinal);
            Assert.Contains(typeof(Probe).Assembly.GetName().Name!, name, StringComparison.Ordinal);
        }

        /// <summary>
        /// This is F-10 itself: a name from a node running a different build must still resolve.
        /// </summary>
        /// <remarks>
        /// Stands in for the rolling deployment. Previously the backplane called
        /// <c>Type.GetType(assemblyQualifiedName)</c>, which binds on the full assembly identity, so
        /// a peer one version ahead or behind resolved to <c>null</c> — silently, and on both the
        /// hub type and the result type.
        /// </remarks>
        [Theory]
        [MemberData(nameof(Types))]
        public void A_name_from_a_differently_versioned_peer_still_resolves(Type type) {
            var fromOtherBuild = type.AssemblyQualifiedName!
                .Replace("Version=" + type.Assembly.GetName().Version, "Version=47.11.0.0");

            Assert.Same(type, WireTypeName.Resolve(fromOtherBuild));
        }

        /// <summary>
        /// And the same name is produced on both sides, so Redis key names line up.
        /// </summary>
        /// <remarks>
        /// The hub type name is not only a payload field: it is interpolated into the connection,
        /// group, user and attribute index keys. Two nodes deriving different names write into
        /// disjoint keyspaces and filter each other's registrations out — a cluster that has quietly
        /// split in two, with no resolution failure to point at.
        /// </remarks>
        [Fact]
        public void Two_builds_of_the_same_type_agree_on_the_name() {
            var here = WireTypeName.From(typeof(Probe));
            var there = new System.Text.RegularExpressions.Regex(@",\s*Version=[^,\]]+")
                .Replace(typeof(Probe).AssemblyQualifiedName!, "");

            Assert.Equal(here, WireTypeName.From(WireTypeName.Resolve(there)!));
        }

        [Fact]
        public void An_unknown_name_resolves_to_null() {
            Assert.Null(WireTypeName.Resolve("Nope.NotHere, NoSuchAssembly"));
            Assert.Null(WireTypeName.Resolve(null));
            Assert.Null(WireTypeName.Resolve("   "));
        }
    }
}
