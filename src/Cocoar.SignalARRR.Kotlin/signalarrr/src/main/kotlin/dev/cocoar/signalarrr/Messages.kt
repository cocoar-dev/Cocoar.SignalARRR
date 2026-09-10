package dev.cocoar.signalarrr

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive

/**
 * The envelope of every client-to-server call. JSON keys are PascalCase to match the .NET
 * server's `System.Text.Json` default.
 */
@Serializable
public data class ClientRequestMessage(
    @SerialName("Method") val method: String,
    @SerialName("Arguments") val arguments: List<JsonElement> = emptyList(),
    @SerialName("Authorization") val authorization: String = "",
    @SerialName("GenericArguments") val genericArguments: List<String> = emptyList(),
)

/** The envelope of every server-to-client call. */
@Serializable
public data class ServerRequestMessage(
    @SerialName("Id") val id: String,
    @SerialName("Method") val method: String,
    @SerialName("Arguments") val arguments: List<JsonElement>? = null,
    @SerialName("GenericArguments") val genericArguments: List<String>? = null,
    @SerialName("CancellationGuid") val cancellationGuid: String? = null,
    @SerialName("StreamId") val streamId: String? = null,
)

/**
 * The `__type` marker the server puts on arguments that are handles rather than values: a
 * cancellation token it can trip later, a stream the client has to fetch. Without the marker the
 * client falls back to matching on shape, for servers that predate it.
 */
public enum class RemoteReferenceKind(public val wireName: String) {
    CANCELLATION_TOKEN("cancellationToken"),
    STREAM("stream"),
}

public object RemoteReference {
    public const val PROPERTY_NAME: String = "__type"

    /** The marker on [obj], or `null` when it carries none. */
    public fun markerOf(obj: JsonObject): String? =
        (obj[PROPERTY_NAME] as? JsonPrimitive)?.takeIf { it.isString }?.content

    public fun isMarked(obj: JsonObject, kind: RemoteReferenceKind): Boolean = markerOf(obj) == kind.wireName
}

/** Marker in a server request's `Arguments` where a `CancellationToken` parameter sits. */
public data class CancellationTokenReference(val id: String) {
    public companion object {
        private val GUID = Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")

        /**
         * Recognises `{ "__type": "cancellationToken", "Id": "<guid>" }`. The bare `{ "Id": "<guid>" }`
         * form is the fallback for a server without the marker; it additionally requires a GUID,
         * because a lone `Id` string is otherwise a shape ordinary payloads have too.
         */
        public fun from(element: JsonElement?): CancellationTokenReference? {
            val obj = element as? JsonObject ?: return null
            val id = (obj["Id"] as? JsonPrimitive)?.takeIf { it.isString }?.content ?: return null
            if (RemoteReference.markerOf(obj) != null) {
                return if (RemoteReference.isMarked(obj, RemoteReferenceKind.CANCELLATION_TOKEN)) CancellationTokenReference(id) else null
            }
            if (obj.size != 1 || !GUID.matches(id)) return null
            return CancellationTokenReference(id)
        }
    }
}

/** A reference to a remote stream: a URL the client downloads from, or uploaded to. */
public data class StreamReference(val uri: String) {
    /** The wire form `{ "Uri": ... }`. */
    public fun toJson(): JsonObject = JsonObject(mapOf("Uri" to JsonPrimitive(uri)))

    public companion object {
        /** Recognises `{ "__type": "stream", "Uri": "<url>" }` or the unmarked lone-`Uri` form. */
        public fun from(element: JsonElement?): StreamReference? {
            val obj = element as? JsonObject ?: return null
            val uri = (obj["Uri"] as? JsonPrimitive)?.takeIf { it.isString }?.content ?: return null
            if (RemoteReference.markerOf(obj) != null) {
                return if (RemoteReference.isMarked(obj, RemoteReferenceKind.STREAM)) StreamReference(uri) else null
            }
            if (obj.size != 1) return null
            return StreamReference(uri)
        }
    }
}
