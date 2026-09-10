package dev.cocoar.signalarrr

/** Base of every error the transport or protocol layer raises. */
public open class SignalRException(message: String, cause: Throwable? = null) : RuntimeException(message, cause)

public class ConnectionFailedException(message: String, cause: Throwable? = null) :
    SignalRException("Connection failed: $message", cause)

public class NegotiationFailedException(message: String, cause: Throwable? = null) :
    SignalRException("Negotiation failed: $message", cause)

public class HandshakeFailedException(message: String, cause: Throwable? = null) :
    SignalRException("Handshake failed: $message", cause)

public class SerializationFailedException(message: String, cause: Throwable? = null) :
    SignalRException("Serialization failed: $message", cause)

public class InvocationFailedException(message: String, cause: Throwable? = null) :
    SignalRException("Invocation failed: $message", cause)

/** The connection is not active (never started, stopped, or lost). */
public class DisconnectedException(message: String = "Connection is not active", cause: Throwable? = null) :
    SignalRException(message, cause)

/** The server closed the connection with an error (`CloseMessage.error`). */
public class HubClosedException(message: String) : SignalRException(message)

/**
 * The server answered an invocation with an error. [message] is the raw error string from the
 * completion message; for SignalARRR calls it carries a [HARRRError] JSON envelope, which
 * [HARRRConnection] parses into a [HARRRException].
 */
public open class HubInvocationException(message: String) : SignalRException(message)
