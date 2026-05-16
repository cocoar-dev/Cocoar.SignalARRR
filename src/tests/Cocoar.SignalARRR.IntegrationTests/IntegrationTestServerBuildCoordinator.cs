using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Cocoar.SignalARRR.IntegrationTests {
    internal static class IntegrationTestServerBuildCoordinator {
        private static readonly ConcurrentDictionary<string, byte> CompletedBuilds = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        public static void EnsureBuilt(string serverProjectDir, string tfm) {
            var buildKey = $"{serverProjectDir}|{tfm}";
            if (CompletedBuilds.ContainsKey(buildKey)) {
                return;
            }

            using var mutex = new Mutex(false, BuildMutexName(buildKey));
            mutex.WaitOne();
            try {
                if (CompletedBuilds.ContainsKey(buildKey)) {
                    return;
                }

                using var process = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = "dotnet",
                        Arguments = $"build \"{serverProjectDir}\" -c Debug --framework {tfm} --nologo",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                process.Start();
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0) {
                    throw new InvalidOperationException(
                        $"Failed to build IntegrationTestServer:{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
                }

                CompletedBuilds.TryAdd(buildKey, 0);
            } finally {
                mutex.ReleaseMutex();
            }
        }

        private static string BuildMutexName(string buildKey) {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buildKey)));
            return $@"Global\SignalARRR-IntegrationTestServer-{hash}";
        }
    }
}
