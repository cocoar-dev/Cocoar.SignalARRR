using System;
using System.IO;
using System.Text;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// Covers the URL handshake between a fixture and the server process it starts.
    /// </summary>
    /// <remarks>
    /// These are plain file-system tests with no server and no Docker, so they run on every leg.
    /// The defect they pin down took down 24 integration tests at once on the Windows leg, because
    /// the exception escaped the fixture's polling loop and killed the collection fixture's
    /// constructor.
    /// </remarks>
    public class ServerUrlFileTests {

        /// <summary>
        /// A file that is still held open by its writer must read as "not ready", not throw.
        /// </summary>
        /// <remarks>
        /// The fixtures called <c>File.ReadAllText</c> directly, which requests <c>FileShare.Read</c>
        /// and therefore refuses to open a file another process still has open for writing. That is
        /// exactly the state the server is in between creating the URL file and flushing it.
        /// <para>
        /// The lock here is taken with <c>FileShare.None</c> rather than the writer's actual
        /// <c>FileShare.Read</c>, because .NET enforces that one on Unix as well (via an advisory
        /// lock) — so this test discriminates on every platform instead of only on Windows, where
        /// the original failure happened to surface.
        /// </para>
        /// </remarks>
        [Fact]
        public void TryRead_returns_false_while_the_file_is_locked_by_its_writer() {
            var path = Path.Combine(Path.GetTempPath(), $"signalarrr-urlfile-{Guid.NewGuid():N}.url");

            try {
                using (var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    writer.Write(Encoding.UTF8.GetBytes("http://127.0.0.1:5000"));
                    writer.Flush();

                    // Still holding the handle: the read must report "not ready" rather than throw.
                    Assert.False(ServerUrlFile.TryRead(path, out var whileLocked));
                    Assert.Equal(string.Empty, whileLocked);
                }

                // Handle released: the very next poll succeeds.
                Assert.True(ServerUrlFile.TryRead(path, out var afterRelease));
                Assert.Equal("http://127.0.0.1:5000", afterRelease);
            } finally {
                try { File.Delete(path); } catch { }
            }
        }

        /// <summary>
        /// A created-but-not-yet-written file is not a URL, and must not be reported as one.
        /// </summary>
        [Fact]
        public void TryRead_returns_false_for_an_empty_file() {
            var path = Path.Combine(Path.GetTempPath(), $"signalarrr-urlfile-{Guid.NewGuid():N}.url");

            try {
                File.WriteAllText(path, string.Empty);
                Assert.False(ServerUrlFile.TryRead(path, out _));

                File.WriteAllText(path, "   \r\n ");
                Assert.False(ServerUrlFile.TryRead(path, out _));
            } finally {
                try { File.Delete(path); } catch { }
            }
        }

        [Fact]
        public void TryRead_returns_false_when_the_file_does_not_exist() {
            var path = Path.Combine(Path.GetTempPath(), $"signalarrr-urlfile-{Guid.NewGuid():N}.url");

            Assert.False(ServerUrlFile.TryRead(path, out var url));
            Assert.Equal(string.Empty, url);
        }

        [Fact]
        public void TryRead_trims_the_published_url() {
            var path = Path.Combine(Path.GetTempPath(), $"signalarrr-urlfile-{Guid.NewGuid():N}.url");

            try {
                File.WriteAllText(path, "http://127.0.0.1:5000\r\n");
                Assert.True(ServerUrlFile.TryRead(path, out var url));
                Assert.Equal("http://127.0.0.1:5000", url);
            } finally {
                try { File.Delete(path); } catch { }
            }
        }
    }
}
