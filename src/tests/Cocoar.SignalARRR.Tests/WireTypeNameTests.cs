using System;
using System.Collections.Generic;
using Cocoar.SignalARRR.Common.Helper;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    /// <summary>
    /// Covers the type names the backplane puts on the wire and how a node finds its hub by one (F-10).
    /// </summary>
    /// <remarks>
    /// A genuine version skew needs two builds of the same assembly loaded at once, which no test
    /// here can stage. What can be pinned down exactly is the property that makes the skew
    /// survivable: the name must not carry build identity, and finding the hub must not depend on
    /// it. The skew itself is then simulated by rewriting the version in a name — which is
    /// precisely what the other node would have sent. Since 6.0 no other type is resolved by name:
    /// a node forwards client results as JSON, and only the node that asked deserializes them.
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

        private static Type RegisteredHub() {
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(OverloadProbeHub).Module.ModuleHandle);
            return typeof(OverloadProbeHub);
        }

        [Fact]
        public void A_hub_is_found_by_its_wire_name() {
            var hub = RegisteredHub();

            Assert.Same(hub, Cocoar.SignalARRR.Server.ServerDispatchRegistry.HubByName(WireTypeName.From(hub)));
        }

        [Fact]
        public void An_unknown_or_unregistered_name_finds_no_hub() {
            Assert.Null(Cocoar.SignalARRR.Server.ServerDispatchRegistry.HubByName("Nope.NotHere, NoSuchAssembly"));
            Assert.Null(Cocoar.SignalARRR.Server.ServerDispatchRegistry.HubByName(WireTypeName.From(typeof(string))));
            Assert.Null(Cocoar.SignalARRR.Server.ServerDispatchRegistry.HubByName(""));
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
        /// This is F-10 itself: a hub named by a node running a different build is still found.
        /// </summary>
        /// <remarks>
        /// Stands in for the rolling deployment. The backplane once called
        /// <c>Type.GetType(assemblyQualifiedName)</c>, which binds on the full assembly identity, so
        /// a peer one version ahead or behind resolved to <c>null</c> — silently.
        /// </remarks>
        [Fact]
        public void A_hub_named_by_a_differently_versioned_peer_is_still_found() {
            var hub = RegisteredHub();
            var fromOtherBuild = hub.AssemblyQualifiedName!
                .Replace("Version=" + hub.Assembly.GetName().Version, "Version=47.11.0.0");

            Assert.Same(hub, Cocoar.SignalARRR.Server.ServerDispatchRegistry.HubByName(fromOtherBuild));
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
        [Theory]
        [MemberData(nameof(Types))]
        public void Two_builds_of_the_same_type_agree_on_the_name(Type type) {
            // What the other build writes: the same name, rendered from an identity with another version.
            var there = type.AssemblyQualifiedName!.Replace("Version=" + type.Assembly.GetName().Version, "Version=47.11.0.0");
            var renderedThere = System.Text.RegularExpressions.Regex.Replace(there, @",\s*(Version|Culture|PublicKeyToken)=[^,\]]+", "");

            Assert.Equal(WireTypeName.From(type), renderedThere);
        }
    }
}
