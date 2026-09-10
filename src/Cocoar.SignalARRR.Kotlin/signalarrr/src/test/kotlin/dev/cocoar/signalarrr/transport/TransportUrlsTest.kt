package dev.cocoar.signalarrr.transport

import okhttp3.HttpUrl.Companion.toHttpUrl
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class TransportUrlsTest {

    // --- negotiate ---

    @Test
    fun `negotiate appends negotiate path and version`() {
        val url = TransportUrls.negotiate("http://h/hub".toHttpUrl())
        assertEquals("http://h/hub/negotiate?negotiateVersion=1", url.toString())
    }

    @Test
    fun `negotiate preserves an existing query`() {
        val url = TransportUrls.negotiate("http://h/hub?user=x".toHttpUrl())
        assertEquals("http://h/hub/negotiate?user=x&negotiateVersion=1", url.toString())
    }

    @Test
    fun `negotiate does not double the trailing slash`() {
        val url = TransportUrls.negotiate("http://h/hub/".toHttpUrl())
        assertEquals("http://h/hub/negotiate?negotiateVersion=1", url.toString())
    }

    // --- transport ---

    @Test
    fun `transport adds id and access_token query parameters`() {
        val url = TransportUrls.transport("http://h/hub".toHttpUrl(), "tok en", "a b")
        assertEquals("tok en", url.queryParameter("id"))
        assertEquals("a b", url.queryParameter("access_token"))
    }

    @Test
    fun `transport omits access_token when the token is null`() {
        val url = TransportUrls.transport("http://h/hub".toHttpUrl(), "tok", null)
        assertEquals("tok", url.queryParameter("id"))
        assertNull(url.queryParameter("access_token"))
        assertEquals(0, url.queryParameterNames.count { it == "access_token" })
    }

    @Test
    fun `transport omits access_token when the token is empty`() {
        val url = TransportUrls.transport("http://h/hub".toHttpUrl(), "tok", "")
        assertNull(url.queryParameter("access_token"))
    }

    @Test
    fun `transport preserves an existing query`() {
        val url = TransportUrls.transport("http://h/hub?user=x".toHttpUrl(), "tok", null)
        assertEquals("x", url.queryParameter("user"))
        assertEquals("tok", url.queryParameter("id"))
    }
}
