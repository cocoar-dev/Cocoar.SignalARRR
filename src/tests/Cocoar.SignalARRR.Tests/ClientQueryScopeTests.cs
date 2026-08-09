using System;
using System.Collections.Generic;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    /// <summary>
    /// A query's reach — this node or the whole cluster — is decided entirely by the metadata it
    /// carries, and the dispatch paths read nothing else. These tests pin that metadata down, because
    /// getting it wrong is invisible at runtime: a query that has quietly lost its cluster scope still
    /// sends successfully, just to fewer clients than the caller believes.
    /// </summary>
    public class ClientQueryScopeTests {

        private sealed class ScopeTestHub : HARRR {
            public ScopeTestHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
        }

        private static ClusterClientQuery Query() {
            return new ClusterClientQuery(
                Array.Empty<ClientContext>(),
                typeof(ScopeTestHub),
                new ServiceCollection().BuildServiceProvider());
        }

        [Fact]
        public void A_fresh_hub_query_reaches_the_whole_cluster() {
            var query = Query();

            Assert.True(query.DistributedDispatchSupported);
            Assert.True(query.CanUseDirectBackplaneDispatch);
            Assert.Equal(SignalARRRBackplaneTargetKind.All, query.TargetKind);
        }

        [Fact]
        public void WithGroup_keeps_the_cluster_and_records_the_group() {
            var query = Query().WithGroup("dashboard");

            Assert.True(query.DistributedDispatchSupported);
            Assert.True(query.CanUseDirectBackplaneDispatch);
            Assert.Equal(SignalARRRBackplaneTargetKind.Group, query.TargetKind);
            Assert.Equal("dashboard", query.GroupName);
        }

        [Fact]
        public void WithUser_keeps_the_cluster_and_records_the_user() {
            var query = Query().WithUser("user-42");

            Assert.True(query.DistributedDispatchSupported);
            Assert.True(query.CanUseDirectBackplaneDispatch);
            Assert.Equal(SignalARRRBackplaneTargetKind.User, query.TargetKind);
            Assert.Equal("user-42", query.UserId);
        }

        [Fact]
        public void WithAttribute_keeps_the_cluster_but_has_to_resolve_connections_first() {
            var query = Query().WithAttribute("role", "oncall");

            Assert.True(query.DistributedDispatchSupported);
            // The backplane can target all/group/user directly, but an attribute match has to be
            // resolved against the registry into a connection list before anything is sent.
            Assert.False(query.CanUseDirectBackplaneDispatch);
            Assert.Collection(query.AttributeFilters, f => {
                Assert.Equal("role", f.Key);
                Assert.Equal("oncall", f.Value);
            });
        }

        [Fact]
        public void Group_and_user_together_also_need_resolution() {
            var query = Query().WithGroup("dashboard").WithUser("user-42");

            Assert.True(query.DistributedDispatchSupported);
            Assert.False(query.CanUseDirectBackplaneDispatch);
        }

        [Fact]
        public void WithLocalFilter_confines_the_query_to_this_node() {
            var query = Query().WithLocalFilter(_ => true);

            Assert.False(query.DistributedDispatchSupported);
            Assert.False(query.CanUseDirectBackplaneDispatch);
        }

        [Fact]
        public void A_query_confined_to_this_node_cannot_be_widened_back_to_the_cluster() {
            // The one that matters. A predicate cannot be evaluated on another node, so once a query
            // has been narrowed by one, every later filter has to stay local too -- otherwise a
            // WithGroup(...) after a WithLocalFilter(...) would hand the backplane a group target and
            // reach clients the predicate was supposed to exclude.
            var confined = Query().WithLocalFilter(c => true);

            Assert.False(confined.WithGroup("dashboard").DistributedDispatchSupported);
            Assert.False(confined.WithUser("user-42").DistributedDispatchSupported);
            Assert.False(confined.WithAttribute("role", "oncall").DistributedDispatchSupported);
        }

        [Fact]
        public void Contradictory_filters_of_the_same_kind_fall_back_to_local_matching() {
            // Two different groups cannot both be a single backplane group target. Rather than
            // silently dropping one, the query drops to local matching, where both predicates apply.
            var query = Query().WithGroup("dashboard").WithGroup("ops");

            Assert.False(query.DistributedDispatchSupported);
        }

        [Fact]
        public void LocalClients_is_the_only_way_to_reach_ClientContext_instances() {
            IClientQuery query = Query();

            Assert.Empty(query.LocalClients());
            Assert.IsNotAssignableFrom<IEnumerable<ClientContext>>(query);
        }
    }
}
