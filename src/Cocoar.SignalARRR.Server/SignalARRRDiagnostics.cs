using System;
using System.IO;
using System.Threading;

namespace Cocoar.SignalARRR.Server {
    internal static class SignalARRRDiagnostics {
        private static readonly object Sync = new object();

        private static string? LogFilePath => Environment.GetEnvironmentVariable("SIGNALARRR_DIAGNOSTICS_LOG_FILE");

        public static bool IsEnabled => !string.IsNullOrWhiteSpace(LogFilePath);

        public static void Write(string category, string message) {
            var logFilePath = LogFilePath;
            if (string.IsNullOrWhiteSpace(logFilePath)) {
                return;
            }

            lock (Sync) {
                var directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrWhiteSpace(directory)) {
                    Directory.CreateDirectory(directory);
                }

                for (int attempt = 0; attempt < 3; attempt++) {
                    try {
                        File.AppendAllText(
                            logFilePath,
                            $"{DateTime.UtcNow:O} [{category}] {message}{Environment.NewLine}");
                        break;
                    } catch (IOException) when (attempt < 2) {
                        Thread.Sleep(10);
                    }
                }
            }
        }
    }
}
