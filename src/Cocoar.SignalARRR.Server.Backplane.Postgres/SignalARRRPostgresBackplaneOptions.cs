using System;
using System.Text.RegularExpressions;

namespace Cocoar.SignalARRR.Server {
    public sealed class SignalARRRPostgresBackplaneOptions {
        /// <summary>
        /// The longest schema name accepted. Postgres truncates identifiers to 63 bytes silently,
        /// and the notification channels are derived from the schema name with a suffix, so the
        /// limit keeps <c>{schema}_responses</c> and <c>{schema}_commands</c> distinct.
        /// </summary>
        public const int MaxSchemaLength = 50;

        private static readonly Regex SchemaPattern = new Regex("^[a-z_][a-z0-9_]*$", RegexOptions.CultureInvariant);

        /// <summary>An Npgsql connection string; the database must be the primary, not a read replica.</summary>
        public string ConnectionString { get; set; } = string.Empty;

        /// <summary>
        /// The schema that holds the backplane's tables. It also names the notification channels,
        /// so it is the unit of isolation: two applications sharing one database must use two
        /// schemas. Lowercase letters, digits and underscores only, at most
        /// <see cref="MaxSchemaLength"/> characters.
        /// </summary>
        public string Schema { get; set; } = "signalarrr";

        /// <summary>
        /// Whether to create the schema, tables and indexes on startup if they do not exist. The
        /// database role then needs <c>CREATE</c> on the database. Turn it off to run the script
        /// from <see cref="SignalARRRPostgresBackplaneSchema.GetCreateScript"/> through your own
        /// migrations instead; startup then fails if the tables are missing.
        /// </summary>
        public bool AutoCreateSchema { get; set; } = true;

        public string NodeId { get; set; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";
        public TimeSpan InvokeTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan NodeTimeout { get; set; } = TimeSpan.FromSeconds(20);

        internal void Validate() {
            if (string.IsNullOrWhiteSpace(ConnectionString)) {
                throw new InvalidOperationException("SignalARRR Postgres backplane requires a connection string.");
            }

            if (string.IsNullOrEmpty(Schema) || Schema.Length > MaxSchemaLength || !SchemaPattern.IsMatch(Schema)) {
                throw new InvalidOperationException(
                    $"SignalARRR Postgres backplane schema '{Schema}' is not valid: use lowercase letters, digits and underscores, " +
                    $"starting with a letter or underscore, at most {MaxSchemaLength} characters.");
            }

            if (NodeTimeout <= HeartbeatInterval) {
                throw new InvalidOperationException(
                    "SignalARRR Postgres backplane NodeTimeout must be longer than HeartbeatInterval, or every node evicts itself between two heartbeats.");
            }
        }
    }

    public sealed class SignalARRRPostgresBackplaneOptionsBuilder {
        private readonly SignalARRRPostgresBackplaneOptions _options = new SignalARRRPostgresBackplaneOptions();

        public SignalARRRPostgresBackplaneOptionsBuilder WithConnectionString(string connectionString) {
            _options.ConnectionString = connectionString;
            return this;
        }

        /// <summary>The schema for tables and notification channels; see <see cref="SignalARRRPostgresBackplaneOptions.Schema"/>.</summary>
        public SignalARRRPostgresBackplaneOptionsBuilder WithSchema(string schema) {
            _options.Schema = schema;
            return this;
        }

        /// <summary>Create the tables on startup (default) or expect them to exist; see <see cref="SignalARRRPostgresBackplaneOptions.AutoCreateSchema"/>.</summary>
        public SignalARRRPostgresBackplaneOptionsBuilder WithAutoCreateSchema(bool autoCreateSchema) {
            _options.AutoCreateSchema = autoCreateSchema;
            return this;
        }

        public SignalARRRPostgresBackplaneOptionsBuilder WithNodeId(string nodeId) {
            _options.NodeId = nodeId;
            return this;
        }

        public SignalARRRPostgresBackplaneOptionsBuilder WithInvokeTimeout(TimeSpan timeout) {
            _options.InvokeTimeout = timeout;
            return this;
        }

        public SignalARRRPostgresBackplaneOptionsBuilder WithHeartbeatInterval(TimeSpan interval) {
            _options.HeartbeatInterval = interval;
            return this;
        }

        public SignalARRRPostgresBackplaneOptionsBuilder WithNodeTimeout(TimeSpan timeout) {
            _options.NodeTimeout = timeout;
            return this;
        }

        internal SignalARRRPostgresBackplaneOptions Build() => _options;
    }
}
