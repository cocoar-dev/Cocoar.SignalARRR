using System.Threading.Tasks;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Cocoar.SignalARRR.Server {
    public class SignalARRRAccessTokenValidationMiddleware {
        private readonly RequestDelegate _next;

        public SignalARRRAccessTokenValidationMiddleware(RequestDelegate next) {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext) {

            var endp = httpContext.GetEndpoint();
            var isSignalRHub = endp?.IsSignalREndpoint() ?? false;
            if (isSignalRHub) {
                var accessToken = httpContext.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)) {
                    httpContext.Request.Headers["Authorization"] = $"Bearer {accessToken}";
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
