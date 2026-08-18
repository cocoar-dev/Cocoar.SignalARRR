using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// What the connection's original HTTP request said about where it arrived, captured at connect
    /// time so it can be replayed onto the synthetic context used for per-message authentication.
    /// </summary>
    /// <remarks>
    /// Re-authenticating a message credential means handing
    /// <c>IAuthenticationService.AuthenticateAsync</c> an <see cref="HttpContext"/>, and a message
    /// over an open socket is not an HTTP request — so one is fabricated. It used to be fabricated
    /// blank, carrying only the <c>Authorization</c> header: no host, no scheme, no
    /// <see cref="HttpContext.Items"/>. That is enough for a handler validating a self-contained
    /// token against statically configured keys, and nothing at all for a handler that needs to know
    /// where the request arrived — a multi-tenant scheme resolving its trusted issuers from the host,
    /// an introspection endpoint that differs per tenant, anything reading what middleware stamped
    /// onto <c>Items</c> before authentication ran. Those failed closed, correctly, and the
    /// connection was denied from the moment the auth cache lapsed.
    /// <para>
    /// The information was never lost — <see cref="ClientContext"/> reached for the request and took
    /// only its service provider. This carries the rest.
    /// </para>
    /// <para>
    /// A snapshot rather than the <see cref="HttpContext"/> itself: that object belongs to the
    /// request and ASP.NET Core is free to recycle it once the request ends. What matters for
    /// identity is what was true when the connection was established, which is exactly what a
    /// snapshot preserves.
    /// </para>
    /// </remarks>
    internal sealed class ConnectionRequestSnapshot {

        public HostString Host { get; }
        public string Scheme { get; }
        public PathString PathBase { get; }
        public PathString Path { get; }
        public QueryString QueryString { get; }

        /// <summary>
        /// A copy of the request's <see cref="HttpContext.Items"/> as of connect time — the tenant a
        /// middleware resolved, and anything else stamped before authentication ran.
        /// </summary>
        public IReadOnlyDictionary<object, object?> Items { get; }

        public ConnectionRequestSnapshot(HttpContext httpContext) {
            var request = httpContext.Request;
            Host = request.Host;
            Scheme = request.Scheme;
            PathBase = request.PathBase;
            Path = request.Path;
            QueryString = request.QueryString;

            var items = new Dictionary<object, object?>();
            foreach (var entry in httpContext.Items) {
                items[entry.Key] = entry.Value;
            }
            Items = items;
        }

        /// <summary>Replays the snapshot onto a fabricated context.</summary>
        public void ApplyTo(HttpContext context) {
            context.Request.Host = Host;
            context.Request.Scheme = Scheme;
            context.Request.PathBase = PathBase;
            context.Request.Path = Path;
            context.Request.QueryString = QueryString;

            foreach (var entry in Items) {
                context.Items[entry.Key] = entry.Value;
            }
        }
    }
}
