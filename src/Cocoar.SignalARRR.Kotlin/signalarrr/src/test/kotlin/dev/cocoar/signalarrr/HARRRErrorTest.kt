package dev.cocoar.signalarrr

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNotNull
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Test

class HARRRErrorTest {

    // --- HARRRError.parse ---

    @Test
    fun `parse pure JSON envelope`() {
        val message = """
            {"Version":1,"Code":"room_full","Type":"Cocoar.SignalARRR.Server.HARRRException","Message":"The room is full.","InnerError":{"Type":"System.Exception","Message":"inner"}}
        """.trimIndent()
        val error = HARRRError.parse(message)

        assertEquals("room_full", error.code)
        assertEquals("Cocoar.SignalARRR.Server.HARRRException", error.type)
        assertEquals("The room is full.", error.message)
        assertNotNull(error.innerError)
        assertEquals("System.Exception", error.innerError!!.type)
        assertEquals("inner", error.innerError!!.message)
        // "room_full" is not a code this client knows, so it folds to "internal" while the raw
        // code is preserved.
        assertEquals("internal", error.normalizedCode)
    }

    @Test
    fun `parse SignalR-prefixed HARRRException form`() {
        val message = "An unexpected error occurred invoking 'X' on the server. " +
            "HARRRException: {\"Version\":1,\"Code\":\"unauthorized\",\"Type\":\"T\",\"Message\":\"m\"}"
        val error = HARRRError.parse(message)

        assertEquals("unauthorized", error.code)
        assertEquals("unauthorized", error.normalizedCode)
    }

    @Test
    fun `parse legacy bracketed type form`() {
        val error = HARRRError.parse("[System.ArgumentException] Invalid value")

        assertEquals("System.ArgumentException", error.type)
        assertEquals("Invalid value", error.message)
    }

    @Test
    fun `parse plain text falls back to raw message`() {
        val error = HARRRError.parse("Something went wrong")

        assertEquals("Error", error.type)
        assertEquals("Something went wrong", error.message)
    }

    @Test
    fun `parse empty JSON object falls through to fallback`() {
        val error = HARRRError.parse("{}")

        assertEquals("Error", error.type)
    }

    @Test
    fun `parse JSON with only Type field is accepted`() {
        val error = HARRRError.parse("{\"Type\":\"System.ArgumentException\"}")

        assertEquals("System.ArgumentException", error.type)
        assertNull(error.code)
    }

    // --- HARRRErrorCodes.normalize ---

    @Test
    fun `normalize returns each known code unchanged`() {
        val known = listOf(
            HARRRErrorCodes.UNAUTHORIZED,
            HARRRErrorCodes.METHOD_NOT_FOUND,
            HARRRErrorCodes.INVALID_ARGUMENT_COUNT,
            HARRRErrorCodes.ARGUMENT_BINDING_FAILED,
            HARRRErrorCodes.CANCELLED,
            HARRRErrorCodes.TIMEOUT,
            HARRRErrorCodes.NO_CLIENT_RESPONDED,
            HARRRErrorCodes.UPLOAD_SLOT_LIMIT_REACHED,
            HARRRErrorCodes.INTERNAL,
        )
        known.forEach { code ->
            assertEquals(code, HARRRErrorCodes.normalize(code), "code '$code' should normalize to itself")
        }
    }

    @Test
    fun `normalize folds null and unknown codes to internal`() {
        assertEquals(HARRRErrorCodes.INTERNAL, HARRRErrorCodes.normalize(null))
        assertEquals(HARRRErrorCodes.INTERNAL, HARRRErrorCodes.normalize("something_unknown"))
    }

    // --- HARRRException message formatting ---

    @Test
    fun `HARRRException formats structured errors as code and message`() {
        val error = HARRRError(version = 1, code = "unauthorized", type = "T", message = "nope")
        val exception = HARRRException(error, "raw")

        assertEquals("[unauthorized] nope", exception.message)
        assertEquals("unauthorized", exception.code)
    }

    @Test
    fun `HARRRException formats fallback errors as the raw message`() {
        val error = HARRRError(type = "Error", message = "ignored", code = null)
        val exception = HARRRException(error, "the raw server message")

        assertEquals("the raw server message", exception.message)
    }
}
