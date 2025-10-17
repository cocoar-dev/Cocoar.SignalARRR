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
            //endpoints.MapPost($"{pattern}/response/{{id}}", async context => await InvokeResponse(context));
            endpoints.MapGet($"{pattern}/download/{{id}}", async context => await InvokeDownload(context));
            return ret;

        }

        public static HubEndpointConventionBuilder MapHARRRController<THub>(
            this IEndpointRouteBuilder endpoints, string pattern,
            Action<HttpConnectionDispatcherOptions> configureOptions) where THub : HARRR {

            var opts = configureOptions.InvokeAction();

            var ret = endpoints.MapHub<THub>(pattern, configureOptions);

            //endpoints.MapPost($"{pattern}/response/{{id}}", async context => await InvokeResponse(context));
            endpoints.MapGet($"{pattern}/download/{{id}}", async context => await InvokeDownload(context));


            return ret;

        }

        //public static async Task InvokeResponse(HttpContext context) {

        //    context.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = null;
        //    var requestManager = context.RequestServices.GetRequiredService<ServerRequestManager>();
        //    var id = context.Request.RouteValues["id"].ToString().ToGuid();
        //    var error = context.Request.Query["error"].ToString().ToNull();
        //    var responseType = requestManager.GetResponseType(id);
        //    switch (responseType) {
        //        case RequestType.Invalid: {
        //                await context.BadRequest();
        //                return;
        //            }
        //        case RequestType.Default: {

        //                JToken payload = null;
        //                if (error == null) {
        //                    if (context.Request.ContentLength != null && context.Request.ContentLength > 0) {
        //                        var json = await context.GetRawBodyStringAsync(Encoding.UTF8);
        //                        payload = Json.Converter.ToJToken(json);
        //                    }
        //                }
        //                requestManager.CompleteRequest(id, payload, error);
        //                await context.Ok();
        //                return;
        //            }
        //        case RequestType.Proxy: {

        //                var httpContext = requestManager.GetHttpContext(id);

        //                if (error != null) {
        //                    await httpContext.BadRequest(error);
        //                } else {
        //                    if (context.Request.ContentLength != null && context.Request.ContentLength > 0) {
        //                        httpContext.Response.Headers["Content-Type"] = context.Request.Headers["Content-Type"];
        //                        await context.Request.Body
        //                            .CopyToAsync(httpContext.Response.Body, 131072, httpContext.RequestAborted)
        //                            .ConfigureAwait(false);
        //                        await httpContext.Response.Body.FlushAsync(httpContext.RequestAborted)
        //                            .ConfigureAwait(false);
        //                    }
        //                }
        //                requestManager.CompleteProxyRequest(id);
        //                return;
        //            }
        //    }

        //}


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
