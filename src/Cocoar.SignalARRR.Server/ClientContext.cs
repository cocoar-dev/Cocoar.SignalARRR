using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Cocoar.SignalARRR.Server {

    public class ClientContext {
        public string Id { get; }
        internal Type HARRRType { get; }
        public IPAddress? RemoteIp { get; }
        public ClaimsPrincipal User { get; private set; } = null!;
        public string? UserIdentifier { get; private set; }
        // UTC throughout: DateTime.Now converts through the local time zone on every read, and
        // this value is compared on the per-message (and per-stream-element) hot path.
        internal DateTime UserValidUntil { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The authentication mode for this client (message-level token vs transport-level credentials).
        /// Detected automatically at connection time for unambiguous cases (cert, Negotiate);
        /// resolved lazily on first auth-required call otherwise.
        /// </summary>
        public AuthenticationMode AuthMode { get; internal set; } = AuthenticationMode.None;

        /// <summary>
        /// The client certificate presented during the TLS handshake, if any.
        /// Used for server-side revalidation when the auth cache expires.
        /// </summary>
        public X509Certificate2? ClientCertificate { get; private set; }

        public DateTime ConnectedAt { get; internal set; }
        public List<DateTime> ReconnectedAt { get; } = new List<DateTime>();

        internal IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Where this connection arrived, captured at connect time. Replayed onto the synthetic
        /// context that per-message authentication and policy evaluation run against, so a handler
        /// that worked at negotiate keeps working per message.
        /// </summary>
        internal ConnectionRequestSnapshot? RequestSnapshot { get; }

        /// <summary>
        /// Creates a scope for work that outlives the request this connection was established by.
        /// </summary>
        /// <remarks>
        /// <see cref="ServiceProvider"/> is that request's scope, and re-authentication resolves
        /// scoped services from it — <c>IAuthenticationService</c> among them — minutes or hours
        /// later. It does work: the connect request's scope stays alive for the connection, long
        /// polling included, which is asserted by <c>LongPollingTransportTests</c>. But it is a
        /// lifetime nobody promised us. The factory itself is a singleton, so holding it is safe, and
        /// a scope taken from it is rooted in the application container rather than in a request.
        /// </remarks>
        internal IServiceScopeFactory ScopeFactory { get; } = null!;

        private readonly Action? _abort;

        /// <summary>
        /// Drops this connection. Safe to call more than once and after it has already gone.
        /// </summary>
        /// <remarks>
        /// The library had no way to do this at all, so an application that learned a session was
        /// revoked could only refuse each call and leave the socket up — a client that believes it
        /// is connected while the server serves it nothing. Captured from the
        /// <see cref="HubCallerContext"/> at connect time, because that is the only place it is
        /// offered.
        /// </remarks>
        public void Abort() {
            try {
                _abort?.Invoke();
            } catch (ObjectDisposedException) {
                // The connection is already gone, which is the state the caller wanted.
            }
        }

        public Uri ConnectedTo { get; }


        private TimeSpan _authCacheDuration;

        // Read once per connection rather than per message: HasTransportLevelCredentials runs on the
        // dispatch path and once per streamed element.
        private IReadOnlyList<string>? _connectionBoundSchemes;

        /// <summary>
        /// Set when the client answers an authentication challenge with nothing, so it is not asked
        /// again for every subsequent streamed element. Cleared when a credential does arrive.
        /// </summary>
        private bool _clientHasNoCredentialToGive;

        public ClientContext(HARRR hub, HubCallerContext hubCallerContext) {
            Id = hubCallerContext.ConnectionId;
            _abort = hubCallerContext.Abort;
            var httpContext = hubCallerContext.GetHttpContext()!;
            ServiceProvider = httpContext.RequestServices;
            ScopeFactory = ServiceProvider.GetRequiredService<IServiceScopeFactory>();
            RequestSnapshot = new ConnectionRequestSnapshot(httpContext);
            User = hubCallerContext.User ?? new ClaimsPrincipal();
            UserIdentifier = ResolveUserIdentifier(hubCallerContext.UserIdentifier, User);
            HARRRType = hub.GetType();

            // Get configured auth cache duration (default: 3 minutes)
            var serverOptions = ServiceProvider.GetService<SignalARRRServerOptions>();
            _authCacheDuration = serverOptions?.AuthCacheDuration ?? TimeSpan.FromMinutes(3);
            _connectionBoundSchemes = serverOptions?.ConnectionBoundSchemes;

            // If the user was already authenticated during SignalR negotiate (hub has [Authorize]),
            // initialize the cache so the first method call doesn't trigger an unnecessary challenge.
            if (User.Identity?.IsAuthenticated == true) {
                UserValidUntil = DateTime.UtcNow.Add(_authCacheDuration);
            }

            // Capture client certificate for transport-level auth revalidation
            ClientCertificate = httpContext.Connection.ClientCertificate;
            AuthMode = DetectAuthenticationMode();

            RemoteIp = httpContext.Connection.RemoteIpAddress;
            var connectedToBuilder = new UriBuilder(httpContext.Request.GetDisplayUrl());
            connectedToBuilder.Query = null;
            ConnectedTo = connectedToBuilder.Uri;

            foreach (var (key, value) in httpContext.Request.Headers) {
                if (key.StartsWith("#")) {
                    Attributes.Set(key.Substring(1), value);
                }
            }

            foreach (var (key, value) in httpContext.Request.Query) {
                if (key.StartsWith("@")) {
                    Attributes.Set(key.Substring(1), value);
                }
            }
        }

        public ClientAttributes Attributes { get; } = new ClientAttributes();

        private readonly HashSet<string> _groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The SignalR groups this client belongs to. Managed via ClientManager.AddToGroupAsync / RemoveFromGroupAsync.
        /// </summary>
        public IReadOnlyCollection<string> Groups => _groups;

        internal void AddGroup(string groupName) => _groups.Add(groupName);
        internal void RemoveGroup(string groupName) => _groups.Remove(groupName);


        /// <summary>
        /// Marks the cached authentication good for another <c>AuthCacheDuration</c>.
        /// </summary>
        /// <remarks>
        /// Called after a successful transport-level revalidation. Without it every message would
        /// revalidate again, and for a client certificate that means chain building — potentially
        /// CRL/OCSP network I/O — on the dispatch hot path.
        /// </remarks>
        internal void ExtendAuthCache(TimeSpan? validFor = null) =>
            UserValidUntil = DateTime.UtcNow.Add(validFor ?? _authCacheDuration);

        internal void SetPrincipal(ClaimsPrincipal claimsPrincipal) {
            // Reached only by validating a credential the client sent, so it evidently has one now.
            _clientHasNoCredentialToGive = false;

            this.User = claimsPrincipal ?? new ClaimsPrincipal();
            UserIdentifier = ResolveUserIdentifier(UserIdentifier, this.User);

            if (this.User.Identity?.IsAuthenticated == true) {
                this.UserValidUntil = DateTime.UtcNow.Add(_authCacheDuration);
            } else {
                this.UserValidUntil = DateTime.UtcNow;
            }
        }

        private static string? ResolveUserIdentifier(string? hubUserIdentifier, ClaimsPrincipal? principal) {
            if (!string.IsNullOrWhiteSpace(hubUserIdentifier)) {
                return hubUserIdentifier;
            }

            return principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal?.Identity?.Name;
        }

        public async Task<PolicyAuthorizationResult> TryAuthenticate(MethodInfo methodInfo) {

            // Runs per streamed element (P-7): both checks must stay allocation-free. The plan is
            // cached per MethodInfo, and RequiresAuthorization folds [AllowAnonymous] in, so an
            // anonymous-permitted method exits here instead of after a full policy evaluation.
            if (!methodInfo.GetAuthorizationPlan().RequiresAuthorization)
                return PolicyAuthorizationResult.Success();

            if (UserValidUntil >= DateTime.UtcNow)
                return PolicyAuthorizationResult.Success();

            // Transport-level auth: re-validate server-side without challenging the client
            if (AuthMode == AuthenticationMode.TransportLevel) {
                return await RevalidateTransportAuth(methodInfo);
            }

            // Message-level or undetermined: challenge the client for a fresh token — unless it has
            // already told us it has none.
            //
            // A challenge is a round trip to the client, and this method runs per streamed element.
            // A client that authenticates only its connection answers every one of them with an
            // empty string, so asking again per element buys nothing and costs a round trip each
            // time. It used to be self-limiting only because the empty answer killed the stream;
            // now that the connection falls back to its negotiated principal instead, the asking
            // would go on for as long as the stream does.
            //
            // Deliberately not solved by extending the auth cache here: the fallback leaves the
            // stamp in the past on purpose, so that the expiry stated on the principal keeps being
            // checked per element rather than once per cache duration. It is the *asking* that is
            // wasteful, not the checking.
            var res = string.Empty;
            if (!_clientHasNoCredentialToGive) {
                var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(HARRRType);
                var harrrContext = (IClientContextDispatcher)ServiceProvider.GetRequiredService(hubContextType);
                res = await harrrContext.Challenge(Id);

                // Cleared again as soon as any message arrives carrying a credential, so a client
                // that acquires one later — a user signing in mid-connection — is asked again.
                _clientHasNoCredentialToGive = string.IsNullOrWhiteSpace(res);
            }

            // If the challenge came back empty and the mode is still undetermined, this client may
            // be transport-authenticated and simply have no token to give.
            //
            // The test must be for *genuine* transport credentials. Accepting any authenticated
            // identity here was an escalation path: a client that connected with a short-lived
            // bearer token still has an authenticated cached principal, so answering the challenge
            // with an empty string moved it to TransportLevel, where revalidation then approved it
            // against that same cached principal. AuthMode persists for the connection, so token
            // expiry and revocation were never enforced again for its whole lifetime.
            if (string.IsNullOrWhiteSpace(res) && AuthMode == AuthenticationMode.None) {
                if (HasTransportLevelCredentials()) {
                    AuthMode = AuthenticationMode.TransportLevel;
                    return await RevalidateTransportAuth(methodInfo);
                }
            }

            if (AuthMode == AuthenticationMode.None) {
                AuthMode = AuthenticationMode.MessageLevel;
            }

            using var scope = ScopeFactory.CreateScope();
            var authentication = new SignalARRRAuthentication(scope.ServiceProvider);
            return await authentication.Authorize(this, res, methodInfo);
        }

        private async Task<PolicyAuthorizationResult> RevalidateTransportAuth(MethodInfo methodInfo) {
            using var scope = ScopeFactory.CreateScope();
            var revalidationService = scope.ServiceProvider.GetService<ITransportAuthRevalidationService>()
                ?? new DefaultTransportAuthRevalidationService(scope.ServiceProvider);

            var result = await revalidationService.RevalidateAsync(this);

            if (result.Outcome == RevalidationOutcome.Valid) {
                ExtendAuthCache(result.ValidFor);

                // Run policy evaluation with the existing principal
                var authentication = new SignalARRRAuthentication(scope.ServiceProvider);
                return await authentication.AuthorizeWithPrincipal(this, methodInfo);
            }

            if (result.Outcome == RevalidationOutcome.Abort) {
                Abort();
            }

            // Revalidation failed — credentials are no longer valid
            return PolicyAuthorizationResult.Forbid();
        }

        /// <summary>
        /// Indicates whether this connection carries credentials bound to the transport rather than
        /// to a message. See <see cref="TransportCredentialPolicy"/>.
        /// </summary>
        internal bool HasTransportLevelCredentials() =>
            TransportCredentialPolicy.IsTransportLevel(ClientCertificate, User, _connectionBoundSchemes);

        private AuthenticationMode DetectAuthenticationMode() {
            if (HasTransportLevelCredentials()) {
                return AuthenticationMode.TransportLevel;
            }

            // For cookie auth and bearer-via-negotiate, we cannot distinguish at connect time.
            // Return None; will be resolved on first auth-required call.
            return AuthenticationMode.None;
        }

        /// <summary>
        /// Refreshes transport-level credentials from a new connection context (e.g., on reconnect).
        /// </summary>
        internal void RefreshTransportCredentials(HubCallerContext hubCallerContext) {
            var httpContext = hubCallerContext.GetHttpContext();
            if (httpContext != null) {
                ClientCertificate = httpContext.Connection.ClientCertificate;
                if (hubCallerContext.User?.Identity?.IsAuthenticated == true) {
                    SetPrincipal(hubCallerContext.User);
                }
                AuthMode = DetectAuthenticationMode();
            }
        }


        public T GetTypedMethods<T>() where T : class {
            var instance = ProxyCreator.CreateInstanceFromInterface<T>(new ServerProxyCreatorHelper(this, null));
            return instance;
        }

        public void ForwardToHttpContext<T>(HttpContext httpContext, Action<T> action) where T : class {
            var instance = ProxyCreator.CreateInstanceFromInterface<T>(new ServerProxyCreatorHelper(this, httpContext));
            action(instance);
        }
    }

}
