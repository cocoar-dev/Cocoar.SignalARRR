using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    /// <summary>
    /// The parts of a cluster subject that need no cluster: local delivery without a backplane,
    /// name uniqueness, and what happens to events a node cannot place or read.
    /// </summary>
    public class ClusterSubjectTests {

        private sealed record OrderChanged(string OrderId, int Version);

        private sealed record OtherEvent(string Id);

        private static ServiceProvider Build(Action<IServiceCollection> configure) {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSignalR();
            services.AddSignalARRR(_ => { });
            configure(services);
            return services.BuildServiceProvider();
        }

        [Fact]
        public async Task Without_a_backplane_a_subject_delivers_locally_and_publish_completes() {
            using var provider = Build(s => s.AddSignalARRRClusterSubject<OrderChanged>("orders"));
            var subject = provider.GetRequiredService<IClusterSubject<OrderChanged>>();
            var received = new List<OrderChanged>();
            using var subscription = subject.Subscribe(received.Add);

            subject.OnNext(new OrderChanged("a", 1));
            await subject.PublishAsync(new OrderChanged("b", 2));

            Assert.Equal("orders", subject.Name);
            Assert.Equal(new[] { new OrderChanged("a", 1), new OrderChanged("b", 2) }, received);
        }

        [Fact]
        public void Two_subjects_cannot_share_a_name() {
            using var provider = Build(s => {
                s.AddSignalARRRClusterSubject<OrderChanged>("events");
                s.AddSignalARRRClusterSubject<OtherEvent>("events");
            });

            provider.GetRequiredService<IClusterSubject<OrderChanged>>();
            var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IClusterSubject<OtherEvent>>());

            Assert.Contains("'events'", ex.Message);
        }

        [Fact]
        public void A_subject_needs_a_name() {
            var services = new ServiceCollection();

            Assert.Throws<ArgumentException>(() => services.AddSignalARRRClusterSubject<OrderChanged>(" "));
        }

        /// <summary>
        /// What arrives from another node is matched by name and deserialized into the registered
        /// type, or dropped. Neither an unknown name nor an unreadable payload may throw: both are
        /// routine during a rolling update with mixed builds.
        /// </summary>
        [Fact]
        public void A_remote_event_is_placed_by_name_and_dropped_when_it_cannot_be_read() {
            using var provider = Build(s => s.AddSignalARRRClusterSubject<OrderChanged>("orders"));
            var subject = provider.GetRequiredService<IClusterSubject<OrderChanged>>();
            var registry = provider.GetRequiredService<ClusterSubjectRegistry>();
            var received = new List<OrderChanged>();
            using var subscription = subject.Subscribe(received.Add);

            registry.Dispatch("no-such-subject", "{\"orderId\":\"x\",\"version\":1}");
            registry.Dispatch("orders", "this is not json");
            registry.Dispatch("orders", "null");
            registry.Dispatch("orders", "{\"orderId\":\"remote\",\"version\":7}");

            Assert.Equal(new[] { new OrderChanged("remote", 7) }, received);
        }

        [Fact]
        public void A_subscriber_that_throws_does_not_break_remote_delivery() {
            using var provider = Build(s => s.AddSignalARRRClusterSubject<OrderChanged>("orders"));
            var subject = provider.GetRequiredService<IClusterSubject<OrderChanged>>();
            var registry = provider.GetRequiredService<ClusterSubjectRegistry>();
            using var subscription = subject.Subscribe(_ => throw new InvalidOperationException("boom"));

            registry.Dispatch("orders", "{\"orderId\":\"remote\",\"version\":1}");
        }
    }
}
