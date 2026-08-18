using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Client {

    /// <summary>
    /// Puts the connection's credential on a file-transfer request.
    /// </summary>
    /// <remarks>
    /// <c>/download/{id}</c> and <c>/upload/{id}</c> are ordinary HTTP endpoints; they carry the
    /// hub's authorization requirements, but not its connection, so nothing authenticates them
    /// unless the request does. Every client sent them a bare request, which meant a hub with
    /// <c>[Authorize]</c> answered 401 to every stream argument and every stream return value — the
    /// endpoints having become protected is what made this reachable.
    /// <para>
    /// The <c>Bearer</c> convention matches the server's own: a credential without a space is a
    /// bearer token, one with a space carries its own scheme. Same string, same meaning, whether it
    /// travels in a message or in a header.
    /// </para>
    /// </remarks>
    internal static class FileTransferHttp {

        public static async Task<HttpRequestMessage> AuthorizeAsync(
            HttpRequestMessage request, Func<Task<string>>? accessTokenProvider) {

            if (accessTokenProvider == null) {
                return request;
            }

            var credential = await accessTokenProvider().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(credential)) {
                return request;
            }

            request.Headers.Authorization = credential.Contains(" ")
                ? AuthenticationHeaderValue.Parse(credential)
                : new AuthenticationHeaderValue("Bearer", credential);

            return request;
        }
    }
}
