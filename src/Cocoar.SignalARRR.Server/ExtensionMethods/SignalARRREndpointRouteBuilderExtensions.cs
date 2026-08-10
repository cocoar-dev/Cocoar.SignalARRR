using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder {
    public static class SignalARRREndpointRouteBuilderExtensions {

        public static IHubEndpointConventionBuilder MapSignalARRRHub<THub>(
            this IEndpointRouteBuilder endpoints, string pattern) where THub : HARRR {

            var hub = endpoints.MapHub<THub>(pattern);
            var transfers = MapFileTransferEndpoints<THub>(endpoints, pattern);
            return Combine(hub, transfers);

        }

        public static IHubEndpointConventionBuilder MapSignalARRRHub<THub>(
            this IEndpointRouteBuilder endpoints, string pattern,
            Action<HttpConnectionDispatcherOptions> configureOptions) where THub : HARRR {

            var opts = configureOptions.InvokeAction();

            var hub = endpoints.MapHub<THub>(pattern, configureOptions);

            var transfers = MapFileTransferEndpoints<THub>(endpoints, pattern);

            return Combine(hub, transfers);

        }

        private static IHubEndpointConventionBuilder Combine(
            IEndpointConventionBuilder hub, IEndpointConventionBuilder[] transfers) {

            var all = new IEndpointConventionBuilder[transfers.Length + 1];
            all[0] = hub;
            Array.Copy(transfers, 0, all, 1, transfers.Length);
            return new FanOutHubEndpointConventionBuilder(all);
        }

        /// <summary>
        /// Applies everything chained onto <c>MapSignalARRRHub</c> to the hub <em>and</em> to the
        /// file-transfer endpoints that belong to it.
        /// </summary>
        /// <remarks>
        /// Returning SignalR's own builder meant <c>.RequireAuthorization()</c> — the idiomatic way
        /// to secure a mapped endpoint, and the one this library's README shows — configured the hub
        /// alone and silently left <c>/download/{id}</c> and <c>/upload/{id}</c> anonymous. Copying
        /// the hub type's <c>[Authorize]</c> attributes covered only the other style of securing it.
        /// <para>
        /// Every convention fans out, not just authorization: a policy someone applies to the hub is
        /// meant for the transfers it hands out too, and picking a subset would recreate the same
        /// class of surprise one method at a time.
        /// </para>
        /// </remarks>
        private sealed class FanOutHubEndpointConventionBuilder : IHubEndpointConventionBuilder {
            private readonly IEndpointConventionBuilder[] _builders;

            public FanOutHubEndpointConventionBuilder(IEndpointConventionBuilder[] builders) => _builders = builders;

            public void Add(Action<EndpointBuilder> convention) {
                foreach (var builder in _builders) builder.Add(convention);
            }

            public void Finally(Action<EndpointBuilder> finallyConvention) {
                foreach (var builder in _builders) builder.Finally(finallyConvention);
            }
        }

        /// <summary>
        /// Maps the download and upload endpoints that back <see cref="System.IO.Stream"/> parameters
        /// and return values, carrying over the hub's own authorization requirements.
        /// </summary>
        /// <remarks>
        /// These are ordinary HTTP endpoints, so the hub's <c>[Authorize]</c> does not reach them by
        /// itself — they were anonymous even when the hub was not, and possession of the URL was the
        /// only credential. Both ways of securing a hub are now covered: the attributes are copied
        /// here, and anything chained onto <c>MapSignalARRRHub</c> reaches these endpoints through
        /// <see cref="FanOutHubEndpointConventionBuilder"/>.
        /// <para>
        /// What this still cannot do is bind the HTTP request itself to the connection that
        /// requested the transfer, because the POST carries no connection identity — so the slot id
        /// remains a capability worth treating as a secret: do not log these URLs. Consuming a slot
        /// <em>is</em> bound to its owner; see <c>ServerPushStreamManager.WaitForUpload</c>.
        /// </para>
        /// </remarks>
        private static IEndpointConventionBuilder[] MapFileTransferEndpoints<THub>(IEndpointRouteBuilder endpoints, string pattern) where THub : HARRR {

            var download = endpoints.MapGet($"{pattern}/download/{{id}}", async context => await InvokeDownload(context));
            var upload = endpoints.MapPost($"{pattern}/upload/{{id}}", async context => await InvokeUpload(context));

            var authorizeData = typeof(THub).GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().ToArray();
            if (authorizeData.Length > 0) {
                download.RequireAuthorization(authorizeData);
                upload.RequireAuthorization(authorizeData);
            }

            return new IEndpointConventionBuilder[] { download, upload };
        }

        /// <summary>
        /// Serves a previously stored stream for download (server → client file transfer).
        /// </summary>
        internal static async Task InvokeDownload(HttpContext context) {
            var streamManager = context.RequestServices.GetRequiredService<ServerPushStreamManager>();

            // ToLowerInvariant, not ToLower: this request's culture is set independently of the one
            // that stored the download, so a culture-sensitive fold would miss the key.
            var uri = context.Request.GetDisplayUrl().ToLowerInvariant();

            var (stream, contentType) = streamManager.TakeStream(uri);

            if (stream == null) {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Stream not found or already downloaded");
                return;
            }

            context.Response.ContentType = contentType ?? "application/octet-stream";

            try {
                await stream
                    .CopyToAsync(context.Response.Body, 131072, context.RequestAborted)
                    .ConfigureAwait(false);
            } finally {
                stream.Dispose();
            }
        }

        /// <summary>
        /// Receives a stream upload from a client (client → server file transfer).
        /// The client first calls RequestUploadSlot() to get the upload URL,
        /// then POSTs the stream data to this endpoint.
        /// </summary>
        internal static async Task InvokeUpload(HttpContext context) {
            var streamManager = context.RequestServices.GetRequiredService<ServerPushStreamManager>();
            var options = context.RequestServices.GetService<SignalARRRServerOptions>() ?? new SignalARRRServerOptions();

            var uri = context.Request.GetDisplayUrl().ToLowerInvariant();

            // Checked before the body is read. Previously the whole request was buffered into memory
            // first and only then matched against a slot, so an anonymous POST to a bogus id still
            // forced a full buffered read.
            if (!streamManager.UploadSlotExists(uri)) {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Upload slot not found or already used");
                return;
            }

            var maxBytes = options.MaxUploadSizeBytes;
            if (maxBytes > 0 && context.Request.ContentLength is { } declared && declared > maxBytes) {
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync($"Upload exceeds the configured limit of {maxBytes} bytes.");
                return;
            }

            // Copy the request body to a MemoryStream so it outlives the HTTP request
            var memoryStream = new MemoryStream();
            try {
                await CopyWithLimitAsync(context.Request.Body, memoryStream, maxBytes, context.RequestAborted);
            } catch (InvalidDataException) {
                memoryStream.Dispose();
                context.Response.StatusCode = 413;
                await context.Response.WriteAsync($"Upload exceeds the configured limit of {maxBytes} bytes.");
                return;
            } catch {
                memoryStream.Dispose();
                throw;
            }

            memoryStream.Position = 0;

            // CompleteUpload disposes the stream itself when there is nobody to hand it to.
            if (!streamManager.CompleteUpload(uri, memoryStream)) {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Upload slot not found or already used");
                return;
            }

            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("OK");
        }

        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="destination"/>, failing once more
        /// than <paramref name="maxBytes"/> have been read.
        /// </summary>
        /// <remarks>
        /// Content-Length is a claim, not a fact — it can be absent (chunked) or simply wrong, so the
        /// limit has to hold while copying rather than only up front.
        /// </remarks>
        private static async Task CopyWithLimitAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken) {

            if (maxBytes <= 0) {
                await source.CopyToAsync(destination, 131072, cancellationToken).ConfigureAwait(false);
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(131072);
            try {
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0) {
                    total += read;
                    if (total > maxBytes) {
                        throw new InvalidDataException($"Upload exceeds {maxBytes} bytes.");
                    }

                    await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                }
            } finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

    }
}
