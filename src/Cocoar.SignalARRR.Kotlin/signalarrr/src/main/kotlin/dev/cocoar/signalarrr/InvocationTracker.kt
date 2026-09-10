package dev.cocoar.signalarrr

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.channels.SendChannel
import kotlinx.serialization.json.JsonElement
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicLong

/** What a completion message carried for a pending invocation. */
internal class CompletionOutcome(val error: String?, val result: JsonElement?)

/** Thread-safe registry of pending invocations and open streams, keyed by invocation id. */
internal class InvocationTracker {
    private val nextId = AtomicLong(0)
    private val invocations = ConcurrentHashMap<String, CompletableDeferred<CompletionOutcome>>()
    private val streams = ConcurrentHashMap<String, SendChannel<JsonElement>>()

    fun nextInvocationId(): String = nextId.getAndIncrement().toString()

    fun registerInvocation(id: String): CompletableDeferred<CompletionOutcome> =
        CompletableDeferred<CompletionOutcome>().also { invocations[id] = it }

    fun removeInvocation(id: String) {
        invocations.remove(id)
    }

    fun registerStream(id: String, channel: SendChannel<JsonElement>) {
        streams[id] = channel
    }

    fun removeStream(id: String): Boolean = streams.remove(id) != null

    fun yieldStreamItem(id: String, item: JsonElement) {
        streams[id]?.trySend(item)
    }

    /** Routes a completion to whichever of invocation or stream is waiting under [id]. */
    fun complete(id: String, error: String?, result: JsonElement?) {
        invocations.remove(id)?.complete(CompletionOutcome(error, result))
        streams.remove(id)?.close(error?.let { HubInvocationException(it) })
    }

    /** Fails every pending invocation and stream — on disconnect. */
    fun failAll(cause: Throwable) {
        val pending = invocations.values.toList()
        val open = streams.values.toList()
        invocations.clear()
        streams.clear()
        pending.forEach { it.completeExceptionally(cause) }
        open.forEach { it.close(cause) }
    }
}
