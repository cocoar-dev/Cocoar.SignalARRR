using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;

namespace Cocoar.SignalARRR.IntegrationTests.Extensions
{
    /// <summary>
    /// Minimal-API helpers for test triggers that need ClientManager and standard response wiring.
    /// </summary>
    public static class EndpointRouteBuilderTestExtensions
    {
        /// <summary>
        /// Maps a POST endpoint that resolves ClientManager and invokes the provided sync handler.
        /// </summary>
        public static IEndpointConventionBuilder MapSignalARRRTest(this IEndpointRouteBuilder endpoints, string pattern, Func<HttpContext, ClientManager, object?> handler)
        {
            return endpoints.MapPost(pattern, async context =>
            {
                var clientManager = context.RequestServices.GetRequiredService<ClientManager>();
                object? result;
                try
                {
                    result = handler(context, clientManager);
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync(ex.ToString());
                    return;
                }

                await WriteResultAsync(context, result);
            });
        }

        /// <summary>
        /// Maps a POST endpoint that resolves ClientManager and invokes the provided async handler.
        /// </summary>
        public static IEndpointConventionBuilder MapSignalARRRTest(this IEndpointRouteBuilder endpoints, string pattern, Func<HttpContext, ClientManager, Task<object?>> handler)
        {
            return endpoints.MapPost(pattern, async context =>
            {
                var clientManager = context.RequestServices.GetRequiredService<ClientManager>();
                object? result;
                try
                {
                    result = await handler(context, clientManager);
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = 500;
                    await context.Response.WriteAsync(ex.ToString());
                    return;
                }

                await WriteResultAsync(context, result);
            });
        }

        private static async Task WriteResultAsync(HttpContext context, object? result)
        {
            switch (result)
            {
                case IResult ires:
                    await ires.ExecuteAsync(context);
                    break;
                case string s:
                    await context.Response.WriteAsJsonAsync(s);
                    break;
                default:
                    await context.Response.WriteAsJsonAsync(result);
                    break;
            }
        }
    }
}
