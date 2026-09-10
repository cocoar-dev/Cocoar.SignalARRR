package dev.cocoar.signalarrr.integration

import dev.cocoar.signalarrr.HARRRErrorCodes
import dev.cocoar.signalarrr.HARRRException
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertFalse
import org.junit.jupiter.api.Assertions.assertNotEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.assertThrows

class ErrorHandlingTests : IntegrationTestBase() {

    @Test
    fun `ArgumentException arrives structured and verbatim`() = test {
        val e = assertThrows<HARRRException> {
            connection.invoke<String>("ExtraMethods.ThrowArgumentException", "testParam")
        }
        assertEquals(HARRRErrorCodes.ARGUMENT_BINDING_FAILED, e.code)
        assertEquals("System.ArgumentException", e.error.type)
        assertTrue(e.error.message.contains("Invalid value provided"), e.error.message)
    }

    /**
     * Since server 5.0 an unexpected exception is withheld: the client sees a fixed sentence and a
     * correlation id, never the method's own message.
     */
    @Test
    fun `unexpected exception withholds the detail`() = test {
        val e = assertThrows<HARRRException> {
            connection.invoke<String>("ExtraMethods.ThrowInvalidOperation")
        }
        assertEquals(HARRRErrorCodes.INTERNAL, e.code)
        assertNotEquals("System.InvalidOperationException", e.error.type)
        assertFalse(e.error.message.contains("This operation is not allowed"), e.error.message)
        assertTrue(e.error.message.contains("Correlation id:"), e.error.message)
    }

    @Test
    fun `application error code travels verbatim`() = test {
        val e = assertThrows<HARRRException> {
            connection.invoke<String>("ExtraMethods.ThrowRoomFull")
        }
        assertEquals("room_full", e.error.code)
        assertEquals(HARRRErrorCodes.INTERNAL, e.code, "an unknown code folds to internal")
        assertEquals("The room is full.", e.error.message)
    }

    @Test
    fun `unknown method is method_not_found`() = test {
        val e = assertThrows<HARRRException> {
            connection.invoke<String>("NonExistentMethod")
        }
        assertEquals(HARRRErrorCodes.METHOD_NOT_FOUND, e.code)
    }

    @Test
    fun `wrong argument count is reported`() = test {
        val e = assertThrows<HARRRException> {
            connection.invoke<String>("Echo", "one", "two")
        }
        assertEquals(HARRRErrorCodes.INVALID_ARGUMENT_COUNT, e.code)
    }

    @Test
    fun `stream error surfaces as HARRRException`() = test {
        val e = assertThrows<HARRRException> {
            connection.stream<Int>("NonExistentStream").collect { }
        }
        assertEquals(HARRRErrorCodes.METHOD_NOT_FOUND, e.code)
    }
}
