using System;
using System.IO;

namespace Cocoar.SignalARRR.IntegrationTests {
    internal static class IntegrationTestServerPathResolver {
        public static string GetAssemblyPath(string tfm) {
            var serverProjectDir = FindServerProjectDir();
            var configuration = GetBuildConfiguration();
            var assemblyPath = Path.Combine(serverProjectDir, "bin", configuration, tfm, "IntegrationTestServer.dll");

            if (File.Exists(assemblyPath)) {
                return assemblyPath;
            }

            throw new FileNotFoundException(
                $"Could not find prebuilt IntegrationTestServer assembly at '{assemblyPath}'.",
                assemblyPath);
        }

        private static string FindServerProjectDir() {
            var dir = AppContext.BaseDirectory;
            while (dir != null) {
                var candidate = Path.Combine(dir, "src", "tests", "IntegrationTestServer");
                if (Directory.Exists(candidate)) {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException(
                $"Could not find IntegrationTestServer project. Searched from: {AppContext.BaseDirectory}");
        }

        private static string GetBuildConfiguration() {
            var baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var frameworkDirectory = new DirectoryInfo(baseDirectory);

            if (frameworkDirectory.Name.StartsWith("net", StringComparison.OrdinalIgnoreCase)
                && frameworkDirectory.Parent is not null) {
                return frameworkDirectory.Parent.Name;
            }

            return "Debug";
        }
    }
}
