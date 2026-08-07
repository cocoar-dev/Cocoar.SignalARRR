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

        public Uri ConnectedTo { get; }


        private TimeSpan _authCacheDuration;

        public ClientContext(HARRR hub, HubCallerContext hubCallerContext) {
            Id = hubCallerContext.ConnectionId;
            var httpContext = hubCallerContext.GetHttpContext()!;
            ServiceProvider = httpContext.RequestServices;
            User = hubCallerContext.User ?? new ClaimsPrincipal();
            UserIdentifier = ResolveUserIdentifier(hubCallerContext.UserIdentifier, User);
            HARRRType = hub.GetType();

            // Get configured auth cache duration (default: 3 minutes)
            _authCacheDuration = ServiceProvider.GetService<SignalARRRServerOptions>()?.AuthCacheDuration
                ?? TimeSpan.FromMinutes(3);

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
                    Attributes[key.Substring(1)] = value;
                }
            }

            foreach (var (key, value) in httpContext.Request.Query) {
                if (key.StartsWith("@")) {
                    Attributes[key.Substring(1)] = value;
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


        internal void SetPrincipal(ClaimsPrincipal claimsPrincipal) {
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

            // Message-level or undetermined: challenge the client for a fresh token
            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(HARRRType);
            var harrrContext = (IClientContextDispatcher)ServiceProvider.GetRequiredService(hubContextType);
            var res = await harrrContext.Challenge(Id);

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

            var authentication = new SignalARRRAuthentication(ServiceProvider);
            return await authentication.Authorize(this, res, methodInfo);
        }

        private async Task<PolicyAuthorizationResult> RevalidateTransportAuth(MethodInfo methodInfo) {
            var revalidationService = ServiceProvider.GetService<ITransportAuthRevalidationService>()
                ?? new DefaultTransportAuthRevalidationService(ServiceProvider);

            if (await revalidationService.RevalidateAsync(this)) {
                // Revalidation succeeded — extend cache
                UserValidUntil = DateTime.UtcNow.Add(_authCacheDuration);

                // Run policy evaluation with the existing principal
                var authentication = new SignalARRRAuthentication(ServiceProvider);
                return await authentication.AuthorizeWithPrincipal(this, methodInfo);
            }

            // Revalidation failed — credentials are no longer valid
            return PolicyAuthorizationResult.Forbid();
        }

        /// <summary>
        /// Indicates whether this connection carries credentials bound to the transport rather than
        /// to a message. See <see cref="TransportCredentialPolicy"/>.
        /// </summary>
        internal bool HasTransportLevelCredentials() =>
            TransportCredentialPolicy.IsTransportLevel(ClientCertificate, User);

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


    public class ClientAttributes : Dictionary<string, StringValues> {

        public ClientAttributes() : base(StringComparer.OrdinalIgnoreCase) {

        }

        public new string? this[string key] {
            get => TryGetValue(key, out var val) ? val : default;
            set {

                base[key] = value;
            }
        }

        public bool Has(string key) {
            return ContainsKey(key);
        }

        public bool Has(string key, string value) {
            if (TryGetValue(key, out var val)) {
                return val.Any(v => v != null && v.Match(value));
            }

            return false;
        }

    }

}
