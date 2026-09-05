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
        /// and <c>connections</c> tables with their indexes, the unlogged <c>messages</c> table
        /// that carries the envelopes, and the <c>publish</c> function every node calls to send one.
        /// </summary>
        /// <remarks>
        /// <c>messages</c> is unlogged on purpose: it holds live traffic for minutes, not history,
        /// and a crash of the database loses every subscription along with it, so nothing would be
        /// left to replay to anyway. Skipping the write-ahead log makes every publish cheaper. The
        /// origin and target columns let a node that catches up after a subscription drop read only
        /// the rows meant for it; the two <c>ALTER TABLE</c> statements bring a table created by
        /// 5.1.0-beta.1 up to date.
        /// </remarks>
        public static string GetCreateScript(string schema = "signalarrr") {
            if (string.IsNullOrWhiteSpace(schema)) {
                throw new ArgumentException("A schema name is required.", nameof(schema));
            }

            var s = QuoteIdentifier(schema);
            var lockKey = QuoteLiteral($"signalarrr-backplane:{schema}:publish");
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
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    created_at      timestamptz NOT NULL DEFAULT now(),
    origin_node_id  text NOT NULL DEFAULT '',
    target_node_id  text NULL,
    payload         text NOT NULL
);

ALTER TABLE {s}.messages ADD COLUMN IF NOT EXISTS origin_node_id text NOT NULL DEFAULT '';
ALTER TABLE {s}.messages ADD COLUMN IF NOT EXISTS target_node_id text NULL;

-- One publish: lock, insert, notify, in one transaction. The advisory lock serializes publishes
-- so that message ids are assigned in commit order — without it, a row inserted first can
-- commit last, and a node whose cursor had already passed its id would never replay it.
-- NOTIFY serializes committing transactions anyway, so the lock costs no throughput.
CREATE OR REPLACE FUNCTION {s}.publish(p_channel text, p_payload text, p_origin_node_id text, p_target_node_id text)
RETURNS bigint
LANGUAGE plpgsql
AS $$
DECLARE
    v_id bigint;
BEGIN
    PERFORM pg_advisory_xact_lock(hashtext({lockKey}));
    INSERT INTO {s}.messages (payload, origin_node_id, target_node_id)
    VALUES (p_payload, p_origin_node_id, p_target_node_id)
    RETURNING id INTO v_id;
    PERFORM pg_notify(p_channel, json_build_array(v_id, p_origin_node_id, p_target_node_id)::text);
    RETURN v_id;
END
$$;
";
        }

        internal static string QuoteIdentifier(string identifier) {
            return "\"" + identifier.Replace("\"", "\"\"") + "\"";
        }

        internal static string QuoteLiteral(string literal) {
            return "'" + literal.Replace("'", "''") + "'";
        }
    }
}
