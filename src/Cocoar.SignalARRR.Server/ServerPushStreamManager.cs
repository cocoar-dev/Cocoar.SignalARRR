using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;

namespace Cocoar.SignalARRR.Server {
    internal class ServerPushStreamManager : IDisposable {

        private readonly ConcurrentDictionary<string, PendingStream> _pendingStreams = new();
        private readonly ConcurrentDictionary<string, UploadSlot> _uploadSlots = new();

        /// <summary>Open slots per owning connection, so the cap can be enforced without scanning.</summary>
        private readonly ConcurrentDictionary<string, int> _slotsPerConnection = new(StringComparer.Ordinal);
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
        /// Create an upload slot for <paramref name="ownerConnectionId"/> and return the upload URL.
        /// The client uploads the stream to this URL, and the server awaits it via WaitForUpload.
        /// </summary>
        /// <remarks>
        /// The owner is recorded so that consuming the slot can be restricted to the connection that
        /// asked for it — see <see cref="WaitForUpload"/>. The POST itself still cannot be checked
        /// that way (an HTTP request carries no connection identity), so the URL remains a secret
        /// worth protecting; what this closes is the other half, where any connection could name
        /// another's slot as a Stream argument and be handed its bytes.
        /// <para>
        /// <paramref name="maxSlotsPerConnection"/> bounds what one client can pin: a slot is a
        /// dictionary entry and a <see cref="TaskCompletionSource{T}"/> held for the expiration
        /// window whether or not anything is ever uploaded, and requesting one is an ordinary hub
        /// call that can be made in a loop.
        /// </para>
        /// </remarks>
        public string CreateUploadSlot(Uri baseUrl, string ownerConnectionId, int maxSlotsPerConnection) {
            if (string.IsNullOrEmpty(ownerConnectionId)) throw new ArgumentNullException(nameof(ownerConnectionId));

            // Reserve first, then build: a check followed by an add would let a client racing itself
            // past the cap.
            var held = _slotsPerConnection.AddOrUpdate(ownerConnectionId, 1, (_, n) => n + 1);
            if (maxSlotsPerConnection > 0 && held > maxSlotsPerConnection) {
                Release(ownerConnectionId);
                throw new HARRRException(
                    HARRRErrorCodes.UploadSlotLimitReached,
                    $"This connection already holds {maxSlotsPerConnection} upload slots that have not been used. " +
                    "Complete or abandon an upload before requesting another.");
            }

            var id = Guid.NewGuid().ToString().ToLowerInvariant();
            var uri = new Uri($"{baseUrl}/upload/{id}");
            var tcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uploadSlots.TryAdd(Normalize(uri.ToString()), new UploadSlot(tcs, Stopwatch.GetTimestamp(), ownerConnectionId));
            return uri.ToString();
        }

        /// <summary>Removes a slot and gives its owner the quota back. Every removal goes through here.</summary>
        private bool TryTakeSlot(string key, out UploadSlot? slot) {
            if (_uploadSlots.TryRemove(key, out slot)) {
                Release(slot.OwnerConnectionId);
                return true;
            }
            return false;
        }

        private void Release(string ownerConnectionId) {
            if (string.IsNullOrEmpty(ownerConnectionId)) return;

            _slotsPerConnection.AddOrUpdate(ownerConnectionId, 0, (_, n) => n > 0 ? n - 1 : 0);
            // Otherwise the counter dictionary itself grows one entry per connection, forever.
            _slotsPerConnection.TryRemove(new KeyValuePair<string, int>(ownerConnectionId, 0));
        }

        /// <summary>
        /// Cancels every slot still held by a connection that has gone away.
        /// </summary>
        /// <remarks>
        /// Called from <c>OnDisconnectedAsync</c>, next to the equivalent for client-to-server
        /// streams. Without it a slot outlived its connection and was only reclaimed by the
        /// expiration sweep, so a client could disconnect and reconnect to keep allocating.
        /// </remarks>
        public void CancelUploadSlotsFor(string connectionId) {
            if (string.IsNullOrEmpty(connectionId)) return;

            foreach (var entry in _uploadSlots.ToArray()) {
                if (!string.Equals(entry.Value.OwnerConnectionId, connectionId, StringComparison.Ordinal)) continue;

                if (TryTakeSlot(entry.Key, out var slot)) {
                    slot!.Completion.TrySetCanceled();
                }
            }
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
        /// <param name="ownerConnectionId">
        /// The connection the slot must belong to. A slot named by anyone else is reported as not
        /// found — deliberately the same answer as for a slot that does not exist, so the check
        /// cannot be used to probe which URLs are live.
        /// </param>
        public async Task<Stream> WaitForUpload(string uploadUrl, string ownerConnectionId, TimeSpan timeout, CancellationToken cancellationToken = default) {
            var key = Normalize(uploadUrl);
            if (!_uploadSlots.TryGetValue(key, out var slot)
                || !string.Equals(slot.OwnerConnectionId, ownerConnectionId, StringComparison.Ordinal)) {
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
                if (TryTakeSlot(key, out var removed)) {
                    removed!.Completion.TrySetCanceled();
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
                if (TryTakeSlot(key, out var slot)) {
                    slot!.Completion.TrySetCanceled();
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
                if (TryTakeSlot(key, out var slot)) {
                    slot!.Completion.TrySetCanceled();
                }
            }
        }

        // CreatedAt is a monotonic Stopwatch timestamp rather than a wall-clock time: a clock
        // correction must not decide whether a pending transfer is expired. Same reasoning as in
        // ServerStreamManager, where it additionally showed up as an intermittent CI failure.
        private sealed record PendingStream(Stream Stream, long CreatedAt, string? ContentType);

        private sealed record UploadSlot(TaskCompletionSource<Stream> Completion, long CreatedAt, string OwnerConnectionId);
    }
}
