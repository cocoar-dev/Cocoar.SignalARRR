using System;
using System.IO;
using System.Text;

// Deliberately in a neutral namespace and linked into the FullFramework test project as well:
// all three fixtures that start an IntegrationTestServer share this handshake, and duplicating the
// guard is how one of them ends up fixed and the others do not.
namespace Cocoar.SignalARRR.TestInfrastructure {

    /// <summary>
    /// Reads the file the <c>IntegrationTestServer</c> publishes its listening URL to.
    /// </summary>
    /// <remarks>
    /// Both fixtures used to do <c>File.Exists</c> followed by a bare <c>File.ReadAllText</c>. That is
    /// a race, and on Windows it is a hard failure rather than a partial read: the server publishes
    /// with <c>File.WriteAllTextAsync</c>, which holds the handle as <c>FileAccess.Write</c> while
    /// permitting only <c>FileShare.Read</c> — and <c>File.ReadAllText</c> in turn requests
    /// <c>FileShare.Read</c>, which does not permit the writer's outstanding write access. The open
    /// fails with "The process cannot access the file ... because it is being used by another
    /// process". The exception escaped the polling loop, so the fixture constructor threw and every
    /// test in the collection failed at once.
    /// <para>
    /// Unix does not enforce that sharing mode, which is why this only ever showed up on the Windows
    /// leg — and only once the release gate began running all three target frameworks concurrently,
    /// which widened the window enough to hit it.
    /// </para>
    /// <para>
    /// Two changes, because either alone leaves a gap: the open now tolerates a concurrent writer,
    /// and a failed read is reported as "not ready yet" so the caller simply polls again instead of
    /// dying. The file may also legitimately be seen empty or half-written between the create and the
    /// flush, which is why a short read counts as not-ready too.
    /// </para>
    /// </remarks>
    internal static class ServerUrlFile {

        /// <summary>
        /// Attempts to read a published URL. Returns <c>false</c> — never throws — while the file is
        /// absent, locked, empty or still being written.
        /// </summary>
        public static bool TryRead(string path, out string url) {
            url = string.Empty;

            try {
                if (!File.Exists(path)) {
                    return false;
                }

                // FileShare.ReadWrite so the server's own write handle does not lock us out;
                // FileShare.Delete so a concurrent cleanup cannot fail the open either.
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                var content = reader.ReadToEnd().Trim();
                if (content.Length == 0) {
                    return false;
                }

                url = content;
                return true;
            } catch (IOException) {
                // Locked or vanished between the check and the open: not ready, try again.
                return false;
            } catch (UnauthorizedAccessException) {
                return false;
            }
        }
    }
}
