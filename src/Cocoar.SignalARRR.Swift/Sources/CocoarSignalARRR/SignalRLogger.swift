import Foundation
import os.log

/// Log level for SignalR client diagnostics.
public enum SignalRLogLevel: Int, Sendable, Comparable {
    case debug = 0
    case info = 1
    case warning = 2
    case error = 3
    case none = 4

    public static func < (lhs: SignalRLogLevel, rhs: SignalRLogLevel) -> Bool {
        lhs.rawValue < rhs.rawValue
    }
}

/// Internal logger used by the SignalR client. Bridges to `os_log`.
final class SignalRLogger: @unchecked Sendable {
    let level: SignalRLogLevel
    private let osLog: OSLog

    init(level: SignalRLogLevel = .info, subsystem: String = "com.cocoar.signalarrr") {
        self.level = level
        self.osLog = OSLog(subsystem: subsystem, category: "SignalR")
    }

    func debug(_ message: @autoclosure () -> String) {
        guard level <= .debug else { return }
        os_log(.debug, log: osLog, "%{public}s", message())
    }

    func info(_ message: @autoclosure () -> String) {
        guard level <= .info else { return }
        os_log(.info, log: osLog, "%{public}s", message())
    }

    func warning(_ message: @autoclosure () -> String) {
        guard level <= .warning else { return }
        os_log(.default, log: osLog, "⚠ %{public}s", message())
    }

    func error(_ message: @autoclosure () -> String) {
        guard level <= .error else { return }
        os_log(.error, log: osLog, "%{public}s", message())
    }
}
