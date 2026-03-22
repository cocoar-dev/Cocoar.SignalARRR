using System;
using System.IO;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    public static class HubEndpointConventionBuilderExtensions {

        public static HubEndpointConventionBuilder MapHARRRController<THub>(
            this IEndpointRouteBuilder endpoints, string pattern) where THub : HARRR {

            var ret = endpoints.MapHub<THub>(pattern);
            endpoints.MapGet($"{pattern}/download/{{id}}", async context => await InvokeDownload(context));
            endpoints.MapPost($"{pattern}/upload/{{id}}", async context => await InvokeUpload(context));
            return ret;

        }

        public static HubEndpointConventionBuilder MapHARRRController<THub>(
            this IEndpointRouteBuilder endpoints, string pattern,
            Action<HttpConnectionDispatcherOptions> configureOptions) where THub : HARRR {

            var opts = configureOptions.InvokeAction();

            var ret = endpoints.MapHub<THub>(pattern, configureOptions);

            endpoints.MapGet($"{pattern}/download/{{id}}", async context => await InvokeDownload(context));
            endpoints.MapPost($"{pattern}/upload/{{id}}", async context => await InvokeUpload(context));

            return ret;

        }

        /// <summary>
        /// Serves a previously stored stream for download (server → client file transfer).
        /// </summary>
        public static async Task InvokeDownload(HttpContext context) {
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
        public static async Task InvokeUpload(HttpContext context) {
            var streamManager = context.RequestServices.GetRequiredService<ServerPushStreamManager>();

            var uri = context.Request.GetDisplayUrl().ToLower();

            // Copy the request body to a MemoryStream so it outlives the HTTP request
            var memoryStream = new MemoryStream();
            await context.Request.Body.CopyToAsync(memoryStream, 131072, context.RequestAborted);
            memoryStream.Position = 0;

            if (!streamManager.CompleteUpload(uri, memoryStream)) {
                memoryStream.Dispose();
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Upload slot not found or already used");
                return;
            }

            context.Response.StatusCode = 200;
            await context.Response.WriteAsync("OK");
        }

    }
}
