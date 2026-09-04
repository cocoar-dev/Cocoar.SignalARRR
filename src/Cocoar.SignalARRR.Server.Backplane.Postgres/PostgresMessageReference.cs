using System;
using System.Text.Json;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// What a notification carries when the envelope itself does not fit: the id of the row in
    /// the <c>messages</c> table, plus origin and target so a node that is not addressed can
    /// skip the fetch. Encoded as a JSON array, <c>[id, origin, target]</c>, so the first
    /// character tells it apart from an inline envelope, which is a JSON object.
    /// </summary>
    internal sealed class PostgresMessageReference {
        public long Id { get; }
        public string OriginNodeId { get; }
        public string? TargetNodeId { get; }

        public PostgresMessageReference(long id, string originNodeId, string? targetNodeId) {
            Id = id;
            OriginNodeId = originNodeId;
            TargetNodeId = targetNodeId;
        }

        /// <summary>Whether <paramref name="payload"/> is a reference rather than an inline envelope.</summary>
        public static bool IsReference(string payload) {
            return payload.Length > 0 && payload[0] == '[';
        }

        public static string Format(long id, string originNodeId, string? targetNodeId) {
            return JsonSerializer.Serialize(new object?[] { id, originNodeId, targetNodeId });
        }

        public static PostgresMessageReference? TryParse(string payload) {
            try {
                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 3) {
                    return null;
                }

                if (root[0].ValueKind != JsonValueKind.Number || root[1].ValueKind != JsonValueKind.String) {
                    return null;
                }

                var target = root[2].ValueKind == JsonValueKind.String ? root[2].GetString() : null;
                return new PostgresMessageReference(root[0].GetInt64(), root[1].GetString()!, target);
            } catch (JsonException) {
                return null;
            }
        }
    }
}
