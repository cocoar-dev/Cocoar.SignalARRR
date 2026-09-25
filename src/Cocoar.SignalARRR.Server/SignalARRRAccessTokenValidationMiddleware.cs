using System.Threading.Tasks;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Lets SignalR endpoints accept the connection token from the <c>access_token</c> query
    /// parameter by copying it into the <c>Authorization</c> header, where authentication handlers
    /// look for it. Browsers need this: JavaScript cannot set a header on a WebSocket upgrade or an
    /// <c>EventSource</c>, so SignalR puts the token into the URL there.
    /// </summary>
    /// <remarks>
    /// A request that already carries an <c>Authorization</c> header keeps it; the query parameter
    /// is only a fallback. It used to overwrite the header, so a client that sent both had its
    /// header silently replaced by whatever the URL said.
    /// </remarks>
    public class SignalARRRAccessTokenValidationMiddleware {
        private readonly RequestDelegate _next;

        public SignalARRRAccessTokenValidationMiddleware(RequestDelegate next) {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext) {

            var endp = httpContext.GetEndpoint();
            var isSignalRHub = endp?.IsSignalREndpoint() ?? false;
            if (isSignalRHub && string.IsNullOrEmpty(httpContext.Request.Headers.Authorization)) {
                var accessToken = httpContext.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)) {
                    httpContext.Request.Headers.Authorization = $"Bearer {accessToken}";
                }
            }

            await _next(httpContext);
        }
    }

    public static class SignalARRRAccessTokenValidationMiddlewareExtensions {

        public static IApplicationBuilder UseSignalARRRAccessTokenValidation(this IApplicationBuilder appBuilder) {
            appBuilder.UseMiddleware<SignalARRRAccessTokenValidationMiddleware>();
            return appBuilder;
        }

    }
}
