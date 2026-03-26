using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using Cocoar.Reflectensions;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Server.ExtensionMethods;
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
        internal DateTime UserValidUntil { get; set; } = DateTime.Now;

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
            HARRRType = hub.GetType();

            // Get configured auth cache duration (default: 3 minutes)
            _authCacheDuration = ServiceProvider.GetService<SignalARRRServerOptions>()?.AuthCacheDuration
                ?? TimeSpan.FromMinutes(3);

            // If the user was already authenticated during SignalR negotiate (hub has [Authorize]),
            // initialize the cache so the first method call doesn't trigger an unnecessary challenge.
            if (User.Identity?.IsAuthenticated == true) {
                UserValidUntil = DateTime.Now.Add(_authCacheDuration);
            }

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

            if (this.User.Identity?.IsAuthenticated == true) {
                this.UserValidUntil = DateTime.Now.Add(_authCacheDuration);
            } else {
                this.UserValidUntil = DateTime.Now;
            }


        }

        public async Task<PolicyAuthorizationResult> TryAuthenticate(MethodInfo methodInfo) {

            if (!methodInfo.GetAuthorizeData().Any())
                return PolicyAuthorizationResult.Success();

            if (UserValidUntil >= DateTime.Now)
                return PolicyAuthorizationResult.Success();


            var hubContextType = typeof(ClientContextDispatcher<>).MakeGenericType(HARRRType);
            var harrrContext = (IClientContextDispatcher)ServiceProvider.GetRequiredService(hubContextType);
            var res = await harrrContext.Challenge(Id);

            var authentication = new SignalARRRAuthentication(ServiceProvider);
            return await authentication.Authorize(this, res, methodInfo);
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
