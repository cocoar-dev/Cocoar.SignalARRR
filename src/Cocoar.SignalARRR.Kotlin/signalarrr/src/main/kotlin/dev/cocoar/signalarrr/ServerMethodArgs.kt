package dev.cocoar.signalarrr

import dev.cocoar.signalarrr.serialization.JsonValues
import kotlinx.serialization.DeserializationStrategy
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonNull

/**
 * The arguments of a server-to-client call, in contract order.
 *
 * Each slot is one of: a [JsonElement] (a value, decode it with [value]), a [ServerCancellationToken]
 * (where the contract declares a `CancellationToken`), or a `ByteArray` (where it declares a
 * `Stream`, downloaded before the handler runs).
 */
public class ServerMethodArgs(
    public val raw: List<Any?>,
    @PublishedApi internal val json: Json,
) : Iterable<Any?> {

    public val size: Int get() = raw.size

    override fun iterator(): Iterator<Any?> = raw.iterator()

    /** The slot as sent, without decoding. */
    public operator fun get(index: Int): Any? = raw.getOrNull(index)

    /** The slot as a [JsonElement]; `JsonNull` for a missing slot. */
    public fun element(index: Int): JsonElement = when (val v = raw.getOrNull(index)) {
        is JsonElement -> v
        null -> JsonNull
        else -> throw IllegalArgumentException("Argument $index is not a value but ${v::class.simpleName}")
    }

    /** Decodes the slot as [T]. */
    public inline fun <reified T> value(index: Int): T = JsonValues.decode(element(index), json)

    /** Decodes the slot with an explicit deserializer. */
    public fun <T> value(index: Int, deserializer: DeserializationStrategy<T>): T =
        JsonValues.decode(element(index), deserializer, json)

    /** The cancellation token in this slot, or `null` if the server did not send one there. */
    public fun cancellationToken(index: Int): ServerCancellationToken? = raw.getOrNull(index) as? ServerCancellationToken

    /** The downloaded stream content in this slot, or `null` if it is not a stream reference. */
    public fun bytes(index: Int): ByteArray? = raw.getOrNull(index) as? ByteArray

    override fun toString(): String = "ServerMethodArgs$raw"
}
