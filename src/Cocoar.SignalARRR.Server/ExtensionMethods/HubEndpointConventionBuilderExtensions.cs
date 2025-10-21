using System;
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
            // Register HTTP endpoint for stream reference downloads (large file transfers)
            endpoints.MapGet($"{pattern}/download/{{id}}", async context => await InvokeDownload(context));
            return ret;

        }

        public static HubEndpointConventionBuilder MapHARRRController<THub>(
            this IEndpointRouteBuilder endpoints, string pattern,
            Action<HttpConnectionDispatcherOptions> configureOptions) where THub : HARRR {

            var opts = configureOptions.InvokeAction();

            var ret = endpoints.MapHub<THub>(pattern, configureOptions);

            // Register HTTP endpoint for stream reference downloads (large file transfers)
            endpoints.MapGet($"{pattern}/download/{{id}}", async context => await InvokeDownload(context));

            return ret;

        }

        public static async Task InvokeDownload(HttpContext context) {
            var streamManager = context.RequestServices.GetRequiredService<ServerPushStreamManager>();

            var uri = context.Request.GetDisplayUrl().ToLower();

            var stream = streamManager.GetByIdentifier(uri);

            await stream
                .CopyToAsync(context.Response.Body, 131072, context.RequestAborted)
                .ConfigureAwait(false);

            streamManager.DisposeStream(uri);
        }

    }
}
