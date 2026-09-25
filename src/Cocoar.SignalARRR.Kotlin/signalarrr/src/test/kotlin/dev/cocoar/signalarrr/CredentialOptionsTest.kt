package dev.cocoar.signalarrr

import kotlinx.coroutines.runBlocking
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

/**
 * The credential options — `credential`, `connectionCredential`, `messageCredential` — named and
 * behaving as in every SignalARRR client: nothing coupled implicitly, and a clear error instead of a
 * silent choice when a credential is set in two places.
 */
class CredentialOptionsTest {

    private val url = "http://127.0.0.1:1/hub"

    /** Runs `create` and returns the options it resolved. */
    private fun created(configure: HARRRConnectionOptions.() -> Unit): HARRRConnectionOptions {
        lateinit var options: HARRRConnectionOptions
        HARRRConnection.create(url) {
            configure()
            options = this
        }
        return options
    }

    @Test
    fun `credential covers the connection`() = runBlocking {
        val options = created { credential = { "both" } }
        assertEquals("both", options.accessTokenProvider?.invoke())
    }

    @Test
    fun `message credential alone leaves the connection anonymous`() {
        val options = created { messageCredential = { "message" } }
        assertNull(options.accessTokenProvider)
    }

    @Test
    fun `connection credential takes its part over from credential`() = runBlocking {
        val options = created {
            credential = { "both" }
            connectionCredential = { "connection" }
        }
        assertEquals("connection", options.accessTokenProvider?.invoke())
    }

    @Suppress("DEPRECATION")
    @Test
    fun `the former message option still covers the connection`() = runBlocking {
        val options = created { messageAccessTokenProvider = { "legacy" } }
        assertEquals("legacy", options.accessTokenProvider?.invoke())
    }

    @Suppress("DEPRECATION")
    @Test
    fun `former and new message option together is an error`() {
        assertThrows<IllegalArgumentException> {
            HARRRConnection.create(url) {
                messageAccessTokenProvider = { "legacy" }
                messageCredential = { "new" }
            }
        }
    }

    @Test
    fun `accessTokenProvider and connection credential together is an error`() {
        assertThrows<IllegalArgumentException> {
            HARRRConnection.create(url) {
                accessTokenProvider = { "signalr" }
                credential = { "both" }
            }
        }
    }

    @Test
    fun `accessTokenProvider next to a message credential is fine`() = runBlocking {
        val options = created {
            accessTokenProvider = { "connection" }
            messageCredential = { "message" }
        }
        assertEquals("connection", options.accessTokenProvider?.invoke())
    }
}
