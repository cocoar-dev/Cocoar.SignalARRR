using System;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The database objects the Postgres backplane needs, for operators who apply schema changes
    /// through their own migrations rather than letting the backplane create them on startup
    /// (<see cref="SignalARRRPostgresBackplaneOptions.AutoCreateSchema"/>).
    /// </summary>
    public static class SignalARRRPostgresBackplaneSchema {
        /// <summary>
        /// The idempotent DDL for <paramref name="schema"/>: the schema itself, the <c>nodes</c>
        /// and <c>connections</c> tables with their indexes, and the unlogged <c>messages</c>
        /// table that carries envelopes too large for a notification payload.
        /// </summary>
        /// <remarks>
        /// <c>messages</c> is unlogged on purpose: it holds live traffic for a few seconds, and the
        /// backplane promises transient delivery only, so losing it on a crash costs nothing while
        /// skipping the write-ahead log makes every large publish cheaper.
        /// </remarks>
        public static string GetCreateScript(string schema = "signalarrr") {
            if (string.IsNullOrWhiteSpace(schema)) {
                throw new ArgumentException("A schema name is required.", nameof(schema));
            }

            var s = QuoteIdentifier(schema);
            return $@"
CREATE SCHEMA IF NOT EXISTS {s};

CREATE TABLE IF NOT EXISTS {s}.nodes (
    node_id    text PRIMARY KEY,
    last_seen  timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS {s}.connections (
    connection_id   text PRIMARY KEY,
    node_id         text NOT NULL,
    hub_type        text NOT NULL,
    user_id         text NULL,
    groups          text[] NOT NULL DEFAULT '{{}}',
    attribute_keys  text[] NOT NULL DEFAULT '{{}}',
    attributes      jsonb NOT NULL DEFAULT '[]',
    registered_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS connections_node_id_idx ON {s}.connections (node_id);
CREATE INDEX IF NOT EXISTS connections_hub_type_user_id_idx ON {s}.connections (hub_type, user_id);
CREATE INDEX IF NOT EXISTS connections_groups_idx ON {s}.connections USING gin (groups);
CREATE INDEX IF NOT EXISTS connections_attribute_keys_idx ON {s}.connections USING gin (attribute_keys);

CREATE UNLOGGED TABLE IF NOT EXISTS {s}.messages (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    created_at  timestamptz NOT NULL DEFAULT now(),
    payload     text NOT NULL
);
";
        }

        internal static string QuoteIdentifier(string identifier) {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }
    }
}
