package dev.cocoar.signalarrr

/** Log levels, ordered from most to least verbose. */
public enum class LogLevel { DEBUG, INFO, WARNING, ERROR, NONE }

/**
 * Logging sink of the client. The library has no logging dependency of its own; plug in
 * whatever the app uses (Logcat, Timber, SLF4J) by implementing this interface.
 */
public interface SignalARRRLogger {
    public val level: LogLevel

    public fun log(level: LogLevel, message: String, throwable: Throwable? = null)

    public fun debug(message: () -> String) {
        if (level <= LogLevel.DEBUG) log(LogLevel.DEBUG, message())
    }

    public fun info(message: () -> String) {
        if (level <= LogLevel.INFO) log(LogLevel.INFO, message())
    }

    public fun warning(throwable: Throwable? = null, message: () -> String) {
        if (level <= LogLevel.WARNING) log(LogLevel.WARNING, message(), throwable)
    }

    public fun error(throwable: Throwable? = null, message: () -> String) {
        if (level <= LogLevel.ERROR) log(LogLevel.ERROR, message(), throwable)
    }
}

/** Writes to `System.err`. The default when no logger is configured. */
public class ConsoleLogger(override val level: LogLevel = LogLevel.INFO) : SignalARRRLogger {
    override fun log(level: LogLevel, message: String, throwable: Throwable?) {
        System.err.println("[SignalARRR] ${level.name}: $message")
        throwable?.printStackTrace(System.err)
    }
}

/** Discards everything. */
public object NoopLogger : SignalARRRLogger {
    override val level: LogLevel = LogLevel.NONE
    override fun log(level: LogLevel, message: String, throwable: Throwable?) {}
}
