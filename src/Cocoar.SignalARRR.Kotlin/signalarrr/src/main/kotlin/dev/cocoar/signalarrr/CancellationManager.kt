package dev.cocoar.signalarrr

import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.suspendCancellableCoroutine
import java.util.concurrent.ConcurrentHashMap
import kotlin.coroutines.resume

/**
 * Maps the cancellation ids the server hands out to the coroutines running the handlers.
 *
 * A server-to-client call whose contract declares a `CancellationToken` arrives with a
 * [CancellationTokenReference] in its arguments; the handler runs in a coroutine registered under
 * that id, and `CancelTokenFromServer` cancels the coroutine. So a handler needs nothing beyond
 * ordinary suspending code — `delay`, `withContext`, a `Flow` — to observe the cancellation.
 */
public class CancellationManager {
    private val jobs = ConcurrentHashMap<String, Job>()

    /** Registers [job] as the work behind cancellation id [id]. */
    public fun register(id: String, job: Job) {
        jobs[id] = job
    }

    /** Cancels the work registered under [id]. Unknown ids are ignored. */
    public fun cancel(id: String): Boolean {
        val job = jobs.remove(id) ?: return false
        job.cancel(ServerCancelledException(id))
        return true
    }

    /** Removes a registration without cancelling. */
    public fun remove(id: String) {
        jobs.remove(id)
    }

    /** Cancels everything — when the connection drops, a handler's caller is gone. */
    public fun cancelAll(reason: String) {
        val all = jobs.values.toList()
        jobs.clear()
        all.forEach { it.cancel(CancellationException(reason)) }
    }

    public val size: Int get() = jobs.size
}

/** The cancellation a handler observes when the server cancels the call. */
public class ServerCancelledException(public val cancellationId: String) :
    CancellationException("The server cancelled the operation ($cancellationId)")

/**
 * The handle a handler receives in place of a `CancellationToken` argument.
 *
 * The handler's coroutine is already cancelled when the server cancels, so this is rarely needed.
 * It exists so argument positions stay aligned with the contract, and for code that wants to poll
 * ([isCancelled]) or await the cancellation explicitly ([awaitCancellation]).
 */
public class ServerCancellationToken internal constructor(public val id: String, private val job: Job) {
    public val isCancelled: Boolean get() = job.isCancelled

    /** Suspends until the server cancels or the handler completes. */
    public suspend fun awaitCancellation() {
        suspendCancellableCoroutine { cont ->
            val handle = job.invokeOnCompletion { cont.resume(Unit) }
            cont.invokeOnCancellation { handle.dispose() }
        }
    }

    override fun toString(): String = "ServerCancellationToken($id, cancelled=$isCancelled)"
}
