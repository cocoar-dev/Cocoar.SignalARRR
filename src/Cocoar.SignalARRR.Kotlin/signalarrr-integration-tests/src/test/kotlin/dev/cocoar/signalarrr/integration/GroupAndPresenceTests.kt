package dev.cocoar.signalarrr.integration

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.withTimeout
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test

/** ClientManager features as seen from the client: groups, broadcasts, presence. */
class GroupAndPresenceTests : IntegrationTestBase() {

    private val contract = "TestShared.ITestClientMethods"

    @Test
    fun `group broadcast reaches a joined client`() = test {
        val received = CompletableDeferred<Unit>()
        connection.onServerMethod("$contract|Nix") { received.complete(Unit) }
        val id = connectionId()
        val group = "kotlin-group-${System.nanoTime()}"

        val (joinStatus, _) = get("/__test/join-group", "connectionId" to id, "group" to group)
        assertEquals(200, joinStatus)

        val (status, _) = trigger("/__test/broadcast-group-nix", "group" to group)
        assertEquals(200, status)
        withTimeout(5_000) { received.await() }

        val (_, groups) = get("/__test/client-groups", "connectionId" to id)
        assertTrue(groups.contains(group), groups)
    }

    @Test
    fun `send-based group subscription runs on the hub`() = test {
        val received = CompletableDeferred<Unit>()
        connection.onServerMethod("$contract|Nix") { received.complete(Unit) }
        val group = "send-group-${System.nanoTime()}"

        connection.send("SubscribeViaSend", group)
        // The send is fire-and-forget; give the server a moment to process it before broadcasting.
        assertEquals("sync", connection.invoke<String>("Echo", "sync"))

        val (status, _) = trigger("/__test/broadcast-group-nix", "group" to group)
        assertEquals(200, status)
        withTimeout(5_000) { received.await() }
    }

    @Test
    fun `invoke-all collects this client's answer`() = test {
        val id = connectionId()
        connection.onServerMethod("$contract|GetById") { args -> "kotlin:" + args.value<String>(0) }

        val (status, body) = trigger("/__test/invoke-one-getbyid", "connectionId" to id, "id" to "q")
        assertEquals(200, status, body)
        assertTrue(body.contains("kotlin:q"), body)
    }

    @Test
    fun `client is present`() = test {
        val id = connectionId()
        val (status, body) = get("/__test/client-exists", "connectionId" to id)
        assertEquals(200, status, body)
        assertTrue(body.contains("true", ignoreCase = true), body)
    }
}
