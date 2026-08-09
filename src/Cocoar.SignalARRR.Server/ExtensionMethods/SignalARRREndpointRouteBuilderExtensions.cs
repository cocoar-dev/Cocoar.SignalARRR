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

        public static HubEndpointConventionBuilder MapSignalARRRHub<THub>(
            this IEndpointRouteBuilder endpoints, string pattern) where THub : HARRR {

            var ret = endpoints.MapHub<THub>(pattern);
            MapFileTransferEndpoints<THub>(endpoints, pattern);
            return ret;

        }

        public static HubEndpointConventionBuilder MapSignalARRRHub<THub>(
            this IEndpointRouteBuilder endpoints, string pattern,
            Action<HttpConnectionDispatcherOptions> configureOptions) where THub : HARRR {

            var opts = configureOptions.InvokeAction();

            var ret = endpoints.MapHub<THub>(pattern, configureOptions);

            MapFileTransferEndpoints<THub>(endpoints, pattern);

            return ret;

        }

        /// <summary>
        /// Maps the download and upload endpoints that back <see cref="System.IO.Stream"/> parameters
        /// and return values, carrying over the hub's own authorization requirements.
        /// </summary>
        /// <remarks>
        /// These are ordinary HTTP endpoints, so the hub's <c>[Authorize]</c> does not reach them and
        /// a <c>.RequireAuthorization()</c> applied to the returned hub builder does not either — it
        /// only configures the hub. They were therefore anonymous even when the hub was not, and
        /// possession of the URL was the only credential.
        /// <para>
        /// Copying the hub type's authorization data closes the common case. It cannot bind a
        /// transfer to the connection that requested it, because the client's upload POST carries no
        /// connection identity at all — that needs a protocol change. Until then the slot id is an
        /// unguessable capability and should be treated as a secret: do not log these URLs.
        /// </para>
        /// </remarks>
        private static void MapFileTransferEndpoints<THub>(IEndpointRouteBuilder endpoints, string pattern) where THub : HARRR {

            var download = endpoints.MapGet($"{pattern}/download/{{id}}", async context => await InvokeDownload(context));
            var upload = endpoints.MapPost($"{pattern}/upload/{{id}}", async context => await InvokeUpload(context));

            var authorizeData = typeof(THub).GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().ToArray();
            if (authorizeData.Length == 0) {
                return;
            }

            download.RequireAuthorization(authorizeData);
            upload.RequireAuthorization(authorizeData);
        }

        /// <summary>
        /// Serves a previously stored stream for download (server → client file transfer).
        /// </summary>
        internal static async Task InvokeDownload(HttpContext context) {
            var streamManager = context.RequestServices.GetRequiredService<ServerPushStreamManager>();

            var uri = context.Request.GetDisplayUrl().ToLower();

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
