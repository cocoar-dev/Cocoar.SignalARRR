package dev.cocoar.signalarrr

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

/**
 * The machine-readable error codes of the wire contract. Clients branch on these — a code is the
 * only cross-language signal, a .NET type name means nothing here. Application code may put its
 * own codes on the wire (`HARRRException("room_full", ...)` on the server); those travel verbatim.
 * A code this client does not know folds to [INTERNAL].
 */
public object HARRRErrorCodes {
    public const val UNAUTHORIZED: String = "unauthorized"
    public const val METHOD_NOT_FOUND: String = "method_not_found"
    public const val INVALID_ARGUMENT_COUNT: String = "invalid_argument_count"
    public const val ARGUMENT_BINDING_FAILED: String = "argument_binding_failed"
    public const val CANCELLED: String = "cancelled"
    public const val TIMEOUT: String = "timeout"
    public const val NO_CLIENT_RESPONDED: String = "no_client_responded"
    public const val UPLOAD_SLOT_LIMIT_REACHED: String = "upload_slot_limit_reached"
    public const val INTERNAL: String = "internal"

    private val known = setOf(
        UNAUTHORIZED, METHOD_NOT_FOUND, INVALID_ARGUMENT_COUNT, ARGUMENT_BINDING_FAILED,
        CANCELLED, TIMEOUT, NO_CLIENT_RESPONDED, UPLOAD_SLOT_LIMIT_REACHED, INTERNAL,
    )

    /** Maps a wire code to one this client knows; unknown or missing codes become [INTERNAL]. */
    public fun normalize(code: String?): String = if (code != null && code in known) code else INTERNAL
}

/**
 * The structured error envelope the server puts into a `HubException` message as JSON.
 *
 * [code] is what to branch on; [message] is for humans; [type] is the .NET exception type and
 * exists for .NET-side diagnostics; [stackTrace] is present only from DEBUG servers; [innerError]
 * nests the cause chain.
 */
@Serializable
public data class HARRRError(
    @SerialName("Version") val version: Int = 0,
    @SerialName("Code") val code: String? = null,
    @SerialName("Type") val type: String = "Error",
    @SerialName("Message") val message: String = "",
    @SerialName("StackTrace") val stackTrace: String? = null,
    @SerialName("InnerError") val innerError: HARRRError? = null,
) {
    /** [code] folded to a value this client version knows. */
    val normalizedCode: String get() = HARRRErrorCodes.normalize(code)

    public companion object {
        private const val MARKER = "HARRRException: "
        private val LEGACY = Regex("\\[([\\w.]+)]\\s*(.*)", RegexOption.DOT_MATCHES_ALL)
        private val json = Json { ignoreUnknownKeys = true; isLenient = true }

        /**
         * Parses a `HubException` message. Accepts the pure JSON envelope, the SignalR-prefixed
         * form (`... HARRRException: {json}`), and the legacy `[Type] Message` format; anything
         * else becomes an error of type `Error` carrying the raw message.
         */
        public fun parse(message: String): HARRRError {
            val markerIndex = message.indexOf(MARKER)
            val candidate = if (markerIndex >= 0) message.substring(markerIndex + MARKER.length) else message

            runCatching { json.decodeFromString(serializer(), candidate.trim()) }.getOrNull()?.let { parsed ->
                // Accepted when it carries actual error content — a versioned envelope, a code, or a
                // concrete type. A bare "{}" (or unrelated JSON) falls through to the fallbacks.
                if (parsed.version >= 1 || !parsed.code.isNullOrEmpty() || (parsed.type.isNotEmpty() && parsed.type != "Error")) {
                    return parsed
                }
            }

            LEGACY.find(message)?.let { m ->
                return HARRRError(type = m.groupValues[1], message = m.groupValues[2])
            }

            return HARRRError(type = "Error", message = message)
        }

        public fun parse(throwable: Throwable): HARRRError = parse(throwable.message ?: throwable.toString())
    }
}

/**
 * A SignalARRR call failed on the server. [error] is the parsed envelope, [rawMessage] the string
 * the server sent. `message` reads `[code] message` for a structured error.
 */
public class HARRRException(public val error: HARRRError, public val rawMessage: String) :
    HubInvocationException(formatMessage(error, rawMessage)) {

    /** Shortcut for `error.normalizedCode`. */
    public val code: String get() = error.normalizedCode

    private companion object {
        fun formatMessage(error: HARRRError, raw: String): String =
            if (error.type == "Error" && error.code == null) raw else "[${error.code ?: error.type}] ${error.message}"
    }
}
