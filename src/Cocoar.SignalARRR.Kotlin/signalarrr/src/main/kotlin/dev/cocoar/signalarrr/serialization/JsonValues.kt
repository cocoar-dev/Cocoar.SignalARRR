package dev.cocoar.signalarrr.serialization

import dev.cocoar.signalarrr.SerializationFailedException
import kotlinx.serialization.DeserializationStrategy
import kotlinx.serialization.InternalSerializationApi
import kotlinx.serialization.KSerializer
import kotlinx.serialization.SerializationException
import kotlinx.serialization.SerializationStrategy
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.serializer
import kotlinx.serialization.serializerOrNull
import java.math.BigDecimal
import java.math.BigInteger
import java.time.Instant
import java.time.temporal.TemporalAccessor
import java.util.Base64
import java.util.Date
import java.util.UUID

/**
 * Converts between Kotlin values and the dynamic [JsonElement] tree the wire layer works with.
 *
 * Every argument and result crosses the wire as a [JsonElement] — for the JSON protocol as-is,
 * for MessagePack re-encoded. [encode] turns an arbitrary value into that tree without needing a
 * serializer up front: primitives, collections and maps structurally, everything else through its
 * `@Serializable` serializer. [decode] goes the other way with a reified type, so generic results
 * like `List<Item>` resolve to the right serializer.
 */
public object JsonValues {

    /** The configuration used unless a connection is given its own [Json]. */
    public val defaultJson: Json = Json {
        ignoreUnknownKeys = true
        encodeDefaults = true
        explicitNulls = true
        coerceInputValues = true
    }

    /** Encodes [value] into a [JsonElement]. Throws [SerializationFailedException] for a type it cannot handle. */
    public fun encode(value: Any?, json: Json = defaultJson): JsonElement {
        return when (value) {
            null -> JsonNull
            is JsonElement -> value
            is String -> JsonPrimitive(value)
            is Char -> JsonPrimitive(value.toString())
            is Boolean -> JsonPrimitive(value)
            is Int, is Long, is Short, is Byte -> JsonPrimitive(value as Number)
            is Double, is Float -> JsonPrimitive(value as Number)
            is BigDecimal, is BigInteger -> JsonPrimitive(value as Number)
            is UInt -> JsonPrimitive(value.toLong())
            is ULong -> JsonPrimitive(BigInteger(value.toString()))
            is UShort, is UByte -> JsonPrimitive(value.toString().toLong())
            is Enum<*> -> encodeEnum(value, json)
            is UUID -> JsonPrimitive(value.toString())
            is Instant -> JsonPrimitive(value.toString())
            is Date -> JsonPrimitive(value.toInstant().toString())
            is TemporalAccessor -> JsonPrimitive(value.toString())
            is ByteArray -> JsonPrimitive(Base64.getEncoder().encodeToString(value))
            is Array<*> -> JsonArray(value.map { encode(it, json) })
            is IntArray -> JsonArray(value.map { JsonPrimitive(it) })
            is LongArray -> JsonArray(value.map { JsonPrimitive(it) })
            is DoubleArray -> JsonArray(value.map { JsonPrimitive(it) })
            is BooleanArray -> JsonArray(value.map { JsonPrimitive(it) })
            is Map<*, *> -> JsonObject(value.entries.associate { (k, v) -> k.toString() to encode(v, json) })
            is Iterable<*> -> JsonArray(value.map { encode(it, json) })
            is Sequence<*> -> JsonArray(value.map { encode(it, json) }.toList())
            is Pair<*, *> -> JsonArray(listOf(encode(value.first, json), encode(value.second, json)))
            else -> encodeSerializable(value, json)
        }
    }

    /** Encodes [value] with an explicit [serializer] — for generic classes whose serializer cannot be looked up by class. */
    public fun <T> encode(value: T, serializer: SerializationStrategy<T>, json: Json = defaultJson): JsonElement =
        json.encodeToJsonElement(serializer, value)

    /** Decodes [element] into [T]. A `null` element decodes as `JsonNull`. */
    public inline fun <reified T> decode(element: JsonElement?, json: Json = defaultJson): T =
        decode(element, json.serializersModule.serializer<T>(), json)

    /** Decodes [element] with an explicit [deserializer]. */
    public fun <T> decode(element: JsonElement?, deserializer: DeserializationStrategy<T>, json: Json = defaultJson): T {
        val source = element ?: JsonNull
        try {
            return json.decodeFromJsonElement(deserializer, source)
        } catch (e: SerializationException) {
            throw SerializationFailedException("Cannot decode $source as ${deserializer.descriptor.serialName}: ${e.message}", e)
        } catch (e: IllegalArgumentException) {
            throw SerializationFailedException("Cannot decode $source as ${deserializer.descriptor.serialName}: ${e.message}", e)
        }
    }

    // KClass-based lookup is the only way to find a serializer for a value whose static type is
    // unknown without pulling in kotlin-reflect; it is internal API but stable across releases.
    @OptIn(InternalSerializationApi::class)
    private fun encodeEnum(value: Enum<*>, json: Json): JsonElement {
        val serializer = runCatching { value::class.serializerOrNull() }.getOrNull()
        if (serializer != null) {
            @Suppress("UNCHECKED_CAST")
            return json.encodeToJsonElement(serializer as KSerializer<Any>, value)
        }
        return JsonPrimitive(value.name)
    }

    @OptIn(InternalSerializationApi::class)
    private fun encodeSerializable(value: Any, json: Json): JsonElement {
        val serializer = try {
            value::class.serializerOrNull()
        } catch (e: SerializationException) {
            throw SerializationFailedException("Cannot serialize ${value::class.qualifiedName}: ${e.message}", e)
        } ?: throw SerializationFailedException(
            "Cannot serialize ${value::class.qualifiedName}: mark it @Serializable, " +
                "or pass a JsonElement / use the serializer overload",
        )
        @Suppress("UNCHECKED_CAST")
        return json.encodeToJsonElement(serializer as KSerializer<Any>, value)
    }
}
