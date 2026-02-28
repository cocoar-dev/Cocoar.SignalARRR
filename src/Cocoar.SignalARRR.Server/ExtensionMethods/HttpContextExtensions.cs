using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    public static class HttpContextExtensions {

        public static async Task<string?> GetRawBodyStringAsync(this HttpContext httpContext, Encoding encoding) {

            if (httpContext.Request.ContentLength == null || !(httpContext.Request.ContentLength > 0))
                return null;

            using var reader = new StreamReader(httpContext.Request.Body, encoding, true, 1024, true);
            return await reader.ReadToEndAsync();

        }

        public static void ProxyFromHARRRClient<TInterface>(this HttpContext httpContext, ClientContext clientContext,
            Action<TInterface> action) where TInterface : class {

            clientContext.ForwardToHttpContext(httpContext, action);

        }


    }
}
