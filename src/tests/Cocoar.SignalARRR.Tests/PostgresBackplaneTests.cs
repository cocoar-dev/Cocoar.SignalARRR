using System;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    /// <summary>
    /// The parts of the Postgres backplane that need no database: option validation, the DDL
    /// script, and the notification payload that points at a table-backed envelope.
    /// </summary>
    public class PostgresBackplaneTests {

        [Fact]
        public void Registration_requires_a_connection_string() {
            var services = new ServiceCollection();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                services.AddSignalARRRPostgresBackplane(options => options.WithSchema("signalarrr")));

            Assert.Contains("connection string", ex.Message);
        }

        /// <summary>
        /// The schema name becomes an identifier and the prefix of two notification channels, so
        /// anything that would be case-folded, need quoting in a channel name, or push a channel
        /// past 63 bytes is refused up front rather than failing at LISTEN time.
        /// </summary>
        [Theory]
        [InlineData("Signalarrr")]
        [InlineData("signal-arrr")]
        [InlineData("1signalarrr")]
        [InlineData("signal arrr")]
        [InlineData("")]
        public void Registration_rejects_a_schema_that_is_not_a_plain_lowercase_identifier(string schema) {
            var services = new ServiceCollection();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                services.AddSignalARRRPostgresBackplane(options => options
                    .WithConnectionString("Host=localhost")
                    .WithSchema(schema)));

            Assert.Contains("schema", ex.Message);
        }

        [Fact]
        public void Registration_rejects_a_schema_longer_than_the_channel_names_allow() {
            var services = new ServiceCollection();
            var schema = new string('a', SignalARRRPostgresBackplaneOptions.MaxSchemaLength + 1);

            Assert.Throws<InvalidOperationException>(() =>
                services.AddSignalARRRPostgresBackplane(options => options
                    .WithConnectionString("Host=localhost")
                    .WithSchema(schema)));
        }

        [Fact]
        public void Registration_rejects_a_node_timeout_that_does_not_outlast_the_heartbeat() {
            var services = new ServiceCollection();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                services.AddSignalARRRPostgresBackplane(options => options
                    .WithConnectionString("Host=localhost")
                    .WithHeartbeatInterval(TimeSpan.FromSeconds(5))
                    .WithNodeTimeout(TimeSpan.FromSeconds(5))));

            Assert.Contains("NodeTimeout", ex.Message);
        }

        [Fact]
        public void Registration_replaces_the_disabled_backplane_regardless_of_order() {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSignalARRRPostgresBackplane(options => options
                .WithConnectionString("Host=localhost;Username=x;Password=y")
                .WithSchema("my_app"));
            services.AddSignalARRR(b => { });

            using var provider = services.BuildServiceProvider();

            Assert.IsType<PostgresSignalARRRBackplane>(provider.GetRequiredService<ISignalARRRBackplane>());
            Assert.Same(provider.GetRequiredService<ISignalARRRBackplane>(), provider.GetRequiredService<ISignalARRRConnectionRegistry>());
        }

        [Fact]
        public void The_schema_script_targets_the_requested_schema_and_is_idempotent() {
            var script = SignalARRRPostgresBackplaneSchema.GetCreateScript("my_app");

            Assert.Contains("CREATE SCHEMA IF NOT EXISTS \"my_app\"", script);
            Assert.Contains("CREATE TABLE IF NOT EXISTS \"my_app\".nodes", script);
            Assert.Contains("CREATE TABLE IF NOT EXISTS \"my_app\".connections", script);
            Assert.Contains("CREATE UNLOGGED TABLE IF NOT EXISTS \"my_app\".messages", script);
            Assert.Contains("CREATE OR REPLACE FUNCTION \"my_app\".publish(", script);
            Assert.Contains("pg_advisory_xact_lock(hashtext('signalarrr-backplane:my_app:publish'))", script);
            Assert.DoesNotContain("\"signalarrr\"", script);
        }

        [Fact]
        public void A_message_reference_round_trips_and_is_told_apart_from_an_envelope() {
            var payload = PostgresMessageReference.Format(42, "node-1", "node-2");

            Assert.True(PostgresMessageReference.IsReference(payload));
            Assert.False(PostgresMessageReference.IsReference("{\"originNodeId\":\"node-1\"}"));

            var reference = PostgresMessageReference.TryParse(payload);
            Assert.NotNull(reference);
            Assert.Equal(42, reference!.Id);
            Assert.Equal("node-1", reference.OriginNodeId);
            Assert.Equal("node-2", reference.TargetNodeId);
        }

        [Fact]
        public void A_message_reference_without_a_target_is_a_broadcast() {
            var reference = PostgresMessageReference.TryParse(PostgresMessageReference.Format(7, "node-1", null));

            Assert.NotNull(reference);
            Assert.Null(reference!.TargetNodeId);
        }

        [Theory]
        [InlineData("[]")]
        [InlineData("[1]")]
        [InlineData("[\"x\",\"node-1\",null]")]
        [InlineData("[1,2,3]")]
        [InlineData("[1,\"node-1\"")]
        public void A_malformed_message_reference_is_rejected_not_thrown(string payload) {
            Assert.Null(PostgresMessageReference.TryParse(payload));
        }
    }
}
