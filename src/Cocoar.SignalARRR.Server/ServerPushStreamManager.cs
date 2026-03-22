using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Server {
    internal class ServerPushStreamManager : IDisposable {

        private readonly ConcurrentDictionary<string, PendingStream> _pendingStreams = new();
        private readonly ConcurrentDictionary<string, TaskCompletionSource<Stream>> _uploadSlots = new();
        private readonly Timer _cleanupTimer;
        private readonly TimeSpan _expirationTimeout;

        public ServerPushStreamManager() : this(TimeSpan.FromMinutes(10)) { }

        public ServerPushStreamManager(TimeSpan expirationTimeout) {
            _expirationTimeout = expirationTimeout;
            _cleanupTimer = new Timer(_ => CleanupExpired(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        // --- Download (Server → Client) ---

        public string StoreStreamForDownload(Stream stream, Uri baseUrl, string? contentType = null) {
            if (_pendingStreams.Values.Any(p => ReferenceEquals(p.Stream, stream))) {
                throw new InvalidOperationException(
                    "The same Stream instance cannot be sent to multiple clients. " +
                    "Each client requires its own Stream (e.g., open the file separately for each client). " +
                    "A Stream can only be read once.");
            }

            var uri = new Uri($"{baseUrl}/download/{Guid.NewGuid()}".ToLower());
            var key = uri.ToString();

            _pendingStreams.TryAdd(key, new PendingStream(stream, DateTime.UtcNow, contentType));
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
            var id = Guid.NewGuid().ToString().ToLower();
            var uri = new Uri($"{baseUrl}/upload/{id}");
            var tcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uploadSlots.TryAdd(uri.ToString().ToLower(), tcs);
            return uri.ToString();
        }

        /// <summary>
        /// Called by the upload HTTP endpoint when the client sends the stream data.
        /// Sets the result on the TCS but does NOT remove it — WaitForUpload handles cleanup.
        /// The upload may complete BEFORE WaitForUpload is called (client uploads first,
        /// then returns StreamReference, then server calls WaitForUpload).
        /// </summary>
        public bool CompleteUpload(string identifier, Stream stream) {
            if (_uploadSlots.TryGetValue(identifier, out var tcs)) {
                tcs.TrySetResult(stream);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Wait for a client to upload a stream to the given upload slot.
        /// Returns immediately if the upload already completed.
        /// </summary>
        public async Task<Stream> WaitForUpload(string uploadUrl, CancellationToken cancellationToken = default) {
            var key = uploadUrl.ToLower();
            if (!_uploadSlots.TryGetValue(key, out var tcs)) {
                throw new InvalidOperationException($"Upload slot not found: {uploadUrl}");
            }

            if (cancellationToken.CanBeCanceled) {
                cancellationToken.Register(() => {
                    if (_uploadSlots.TryRemove(key, out var t)) {
                        t.TrySetCanceled(cancellationToken);
                    }
                });
            }

            var stream = await tcs.Task;
            _uploadSlots.TryRemove(key, out _);
            return stream;
        }

        // --- Cleanup ---

        public void DisposeStream(string identifier) {
            if (_pendingStreams.TryRemove(identifier, out var pending)) {
                pending.Stream?.Dispose();
            }
        }

        private void CleanupExpired() {
            var cutoff = DateTime.UtcNow - _expirationTimeout;

            var expiredDownloads = _pendingStreams
                .Where(kvp => kvp.Value.CreatedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var key in expiredDownloads) {
                DisposeStream(key);
            }
        }

        public void Dispose() {
            _cleanupTimer.Dispose();
            foreach (var key in _pendingStreams.Keys.ToList()) {
                DisposeStream(key);
            }
            foreach (var key in _uploadSlots.Keys.ToList()) {
                if (_uploadSlots.TryRemove(key, out var tcs)) {
                    tcs.TrySetCanceled();
                }
            }
        }

        private sealed record PendingStream(Stream Stream, DateTime CreatedAt, string? ContentType);
    }
}
