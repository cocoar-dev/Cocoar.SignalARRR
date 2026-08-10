using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Server {
    internal class ServerPushStreamManager : IDisposable {

        private readonly ConcurrentDictionary<string, PendingStream> _pendingStreams = new();
        private readonly ConcurrentDictionary<string, UploadSlot> _uploadSlots = new();
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _expirationTimeout;

        /// <summary>Downloads stored and not yet taken. Previously not observable (O-8).</summary>
        public int PendingDownloadCount => _pendingStreams.Count;

        /// <summary>Upload slots waiting for a client. Previously not observable (O-8).</summary>
        public int PendingUploadSlotCount => _uploadSlots.Count;

        public ServerPushStreamManager() : this(TimeSpan.FromMinutes(10)) { }

        public ServerPushStreamManager(TimeSpan expirationTimeout) {
            _expirationTimeout = expirationTimeout;
            // The callback must not throw: an unhandled exception on a timer thread takes the process
            // down, and DisposeStream can fail while flushing a FileStream.
            _cleanupTimer = new Timer(_ => SafeCleanupExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        // --- Download (Server → Client) ---

        public string StoreStreamForDownload(Stream stream, Uri baseUrl, string? contentType = null) {
            if (_pendingStreams.Values.Any(p => ReferenceEquals(p.Stream, stream))) {
                throw new InvalidOperationException(
                    "The same Stream instance cannot be sent to multiple clients. " +
                    "Each client requires its own Stream (e.g., open the file separately for each client). " +
                    "A Stream can only be read once.");
            }

            // Normalize, i.e. ToLowerInvariant: same Turkish-'I' hazard as the upload slots, and the
            // two ends of this pair run on different threads whose CurrentCulture is set independently
            // from each request (UseRequestLocalization) — a download stored under 'i' would then be
            // looked up as 'ı' and never found, pinning the Stream until it expires.
            var uri = new Uri(Normalize($"{baseUrl}/download/{Guid.NewGuid()}"));
            var key = uri.ToString();

            _pendingStreams.TryAdd(key, new PendingStream(stream, Stopwatch.GetTimestamp(), contentType));
            return key;
        }

        public (Stream? Stream, string? ContentType) TakeStream(string identifier) {
            if (_pendingStreams.TryRemove(identifier, out var pending)) {
                return (pending.Stream, pending.ContentType);
            }
            return (null, null);
        }

        // --- Upload (Client → Server) ---

        /// <summary>
        /// Create an upload slot and return the upload URL.
        /// The client uploads the stream to this URL, and the server awaits it via WaitForUpload.
        /// </summary>
        public string CreateUploadSlot(Uri baseUrl) {
            var id = Guid.NewGuid().ToString().ToLowerInvariant();
            var uri = new Uri($"{baseUrl}/upload/{id}");
            var tcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uploadSlots.TryAdd(Normalize(uri.ToString()), new UploadSlot(tcs, Stopwatch.GetTimestamp()));
            return uri.ToString();
        }

        /// <summary>
        /// Indicates whether an upload slot exists, without consuming it.
        /// </summary>
        /// <remarks>
        /// Lets the HTTP endpoint reject an unknown slot <em>before</em> buffering the request body,
        /// rather than after.
        /// </remarks>
        public bool UploadSlotExists(string identifier) => _uploadSlots.ContainsKey(Normalize(identifier));

        // ToLowerInvariant, not ToLower: under a Turkish locale 'I' lowercases to 'ı', so a slot
        // created on one request could not be found by the next.
        private static string Normalize(string identifier) => identifier.ToLowerInvariant();

        /// <summary>
        /// Called by the upload HTTP endpoint when the client sends the stream data.
        /// Sets the result on the TCS but does NOT remove it — WaitForUpload handles cleanup.
        /// The upload may complete BEFORE WaitForUpload is called (client uploads first,
        /// then returns StreamReference, then server calls WaitForUpload).
        /// </summary>
        public bool CompleteUpload(string identifier, Stream stream) {
            if (_uploadSlots.TryGetValue(Normalize(identifier), out var slot) && slot.Completion.TrySetResult(stream)) {
                return true;
            }

            // Either the slot is gone (expired, cancelled, already used) or it was completed by a
            // concurrent upload. Either way nobody will consume this stream, so do not leak it.
            stream.Dispose();
            return false;
        }

        /// <summary>
        /// Wait for a client to upload a stream to the given upload slot.
        /// Returns immediately if the upload already completed.
        /// </summary>
        /// <summary>
        /// Wait for a client to upload a stream to the given upload slot.
        /// Returns immediately if the upload already completed.
        /// </summary>
        /// <remarks>
        /// <paramref name="timeout"/> is not optional by design. This waits on something only the
        /// client can supply, so without a deadline a client that requests a slot and never uploads
        /// parks the invocation forever.
        /// </remarks>
        public async Task<Stream> WaitForUpload(string uploadUrl, TimeSpan timeout, CancellationToken cancellationToken = default) {
            var key = Normalize(uploadUrl);
            if (!_uploadSlots.TryGetValue(key, out var slot)) {
                throw new InvalidOperationException($"Upload slot not found: {uploadUrl}");
            }

            try {
                return timeout > TimeSpan.Zero
                    ? await slot.Completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false)
                    : await slot.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            } catch (TimeoutException) {
                throw new TimeoutException(
                    $"The client did not upload to '{uploadUrl}' within {timeout}. " +
                    $"Adjust {nameof(SignalARRRServerOptions)}.{nameof(SignalARRRServerOptions.StreamUploadTimeout)} if larger uploads are expected.");
            } finally {
                // Whatever the outcome, the slot is done. Previously it was only removed on success,
                // so a cancelled or abandoned wait left it behind for the process lifetime.
                if (_uploadSlots.TryRemove(key, out var removed)) {
                    removed.Completion.TrySetCanceled();
                }
            }
        }

        // --- Cleanup ---

        public void DisposeStream(string identifier) {
            if (_pendingStreams.TryRemove(identifier, out var pending)) {
                pending.Stream?.Dispose();
            }
        }

        private void SafeCleanupExpired() {
            try {
                CleanupExpired();
            } catch {
                // Never let this escape onto the timer thread — see the constructor.
            }
        }

        private void CleanupExpired() {
            var expiredDownloads = _pendingStreams
                .Where(kvp => Stopwatch.GetElapsedTime(kvp.Value.CreatedAt) >= _expirationTimeout)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in expiredDownloads) {
                DisposeStream(key);
            }

            // Upload slots were never swept: RequestUploadSlot is a plain hub method, so a client
            // calling it in a loop grew this dictionary without bound for the process lifetime.
            var expiredUploads = _uploadSlots
                .Where(kvp => Stopwatch.GetElapsedTime(kvp.Value.CreatedAt) >= _expirationTimeout)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in expiredUploads) {
                if (_uploadSlots.TryRemove(key, out var slot)) {
                    slot.Completion.TrySetCanceled();
                    SignalARRRServerTelemetry.UploadSlotsSwept.Add(1);
                }
            }
        }

        public void Dispose() {
            _cleanupTimer.Dispose();
            foreach (var key in _pendingStreams.Keys.ToList()) {
                DisposeStream(key);
            }
            foreach (var key in _uploadSlots.Keys.ToList()) {
                if (_uploadSlots.TryRemove(key, out var slot)) {
                    slot.Completion.TrySetCanceled();
                }
            }
        }

        // CreatedAt is a monotonic Stopwatch timestamp rather than a wall-clock time: a clock
        // correction must not decide whether a pending transfer is expired. Same reasoning as in
        // ServerStreamManager, where it additionally showed up as an intermittent CI failure.
        private sealed record PendingStream(Stream Stream, long CreatedAt, string? ContentType);

        private sealed record UploadSlot(TaskCompletionSource<Stream> Completion, long CreatedAt);
    }
}
