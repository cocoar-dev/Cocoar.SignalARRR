using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    internal class SignalARRRAuthentication {

        private IServiceProvider _serviceProvider;

        public SignalARRRAuthentication(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
        }

        public async Task<PolicyAuthorizationResult> Authorize(ClientContext clientContext, string authorization, MethodInfo methodInfo) {

            var plan = methodInfo.GetAuthorizationPlan();

            // [AllowAnonymous] anywhere on the method, its contract, its type chain or the hub.
            if (plan.AllowAnonymous) {
                return PolicyAuthorizationResult.Success();
            }

            var authorizeData = plan.AuthorizeData;

            if (authorizeData.Count == 0)
                return PolicyAuthorizationResult.Success();

            var authenticationService = _serviceProvider.GetRequiredService<IAuthenticationService>();
            var policyEvaluator = _serviceProvider.GetRequiredService<IPolicyEvaluator>();
            var policyProvider = _serviceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

            var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);

            if (policy == null) {
                return PolicyAuthorizationResult.Success();
            }

            var ctx = new DefaultHttpContext();
            ctx.RequestServices = _serviceProvider;

            AuthenticateResult authenticateResult = AuthenticateResult.NoResult();
            if (clientContext.UserValidUntil < DateTime.UtcNow) {
                // Token cache expired — need to re-authenticate

                if (String.IsNullOrWhiteSpace(authorization)) {
                    // Transport-level auth: re-validate credentials server-side, no token required
                    if (clientContext.AuthMode == AuthenticationMode.TransportLevel) {
                        var revalidationService = _serviceProvider.GetService<ITransportAuthRevalidationService>()
                            ?? new DefaultTransportAuthRevalidationService(_serviceProvider);

                        if (!await revalidationService.RevalidateAsync(clientContext)) {
                            return PolicyAuthorizationResult.Forbid();
                        }

                        // Revalidation *is* the authentication for a connection-bound credential, so
                        // this returns here instead of falling through to the scheme loop below.
                        //
                        // Falling through re-authenticated from scratch against the synthetic context
                        // built above, which carries nothing from the connection's original request —
                        // no cookie, no Negotiate handshake, no header. Only a client certificate
                        // survived, because it is copied across explicitly. Everything else was denied
                        // the moment the auth cache expired, while a stream on the same connection
                        // kept running: the per-element re-auth goes through AuthorizeWithPrincipal,
                        // which trusts the revalidated principal. Same credential, same connection,
                        // two answers.
                        clientContext.ExtendAuthCache();
                        return await AuthorizeWithPrincipal(clientContext, methodInfo);
                    } else {
                        // No credential to check and none coming: the client authenticated its
                        // connection and sends nothing per message.
                        //
                        // This used to be a flat denial, which caught nothing. Nothing was found to
                        // be invalid — the connection is authenticated, the principal is right here,
                        // and SignalR itself would keep honouring it for the life of the socket. The
                        // denial hit valid sessions exactly as hard as expired ones, three minutes
                        // in, and mid-flight for a running stream. That is an availability failure
                        // wearing a security posture: an application that wants access to stop after
                        // a fixed window sets AuthCacheDuration and configures a credential.
                        //
                        // So fall back to the principal established at connection time — but honour
                        // the expiry it states, which SignalR does not do at all once negotiate is
                        // past. Weaker than either configured mode, stronger than plain SignalR.
                        // Revocation still goes unnoticed; nothing can notice it without a credential
                        // to check.
                        //
                        // The cache is deliberately not extended: the expiry check is cheap, and
                        // extending would let a token outlive its own `exp` by up to one cache
                        // duration. An unauthenticated principal needs no special case — policy
                        // evaluation rejects it, which is the right authority for that decision.
                        if (TransportCredentialPolicy.IsExpired(clientContext.User)) {
                            return PolicyAuthorizationResult.Forbid();
                        }

                        return await AuthorizeWithPrincipal(clientContext, methodInfo);
                    }
                } else {
                    if (!authorization.Contains(" ")) {
                        authorization = $"Bearer {authorization}";
                    }
                    ctx.Request.Headers["Authorization"] = authorization;
                }

                // Determine which authentication schemes to try
                var schemes = policy.AuthenticationSchemes;
                if (!schemes.Any()) {
                    // [Authorize] without a scheme — use the default authentication scheme
                    var schemeProvider = _serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
                    var defaultScheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();
                    if (defaultScheme != null) {
                        schemes = new List<string> { defaultScheme.Name };
                    }
                }

                foreach (var scheme in schemes) {
                    authenticateResult = await authenticationService.AuthenticateAsync(ctx, scheme);
                    if (authenticateResult.Succeeded) {
                        clientContext.SetPrincipal(authenticateResult.Principal!);
                        break;
                    }
                }

            } else {
                // Token cache still valid — use cached principal
                var t = new AuthenticationTicket(clientContext.User, clientContext.User.Identity?.AuthenticationType ?? string.Empty);
                authenticateResult = AuthenticateResult.Success(t);
            }

            ctx.User = authenticateResult.Principal ?? new System.Security.Claims.ClaimsPrincipal();

            var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, ctx, clientContext);

            return authorizeResult;
        }

        /// <summary>
        /// Runs policy evaluation using the existing principal from ClientContext,
        /// without requiring an Authorization header. Used for transport-level auth
        /// (client certificates, cookies, Negotiate) where the identity was established
        /// at connection time.
        /// </summary>
        public async Task<PolicyAuthorizationResult> AuthorizeWithPrincipal(ClientContext clientContext, MethodInfo methodInfo) {

            var plan = methodInfo.GetAuthorizationPlan();

            if (plan.AllowAnonymous) {
                return PolicyAuthorizationResult.Success();
            }

            var authorizeData = plan.AuthorizeData;

            if (authorizeData.Count == 0)
                return PolicyAuthorizationResult.Success();

            var policyEvaluator = _serviceProvider.GetRequiredService<IPolicyEvaluator>();
            var policyProvider = _serviceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

            var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);

            if (policy == null) {
                return PolicyAuthorizationResult.Success();
            }

            var ctx = new DefaultHttpContext();
            ctx.RequestServices = _serviceProvider;
            ctx.User = clientContext.User;

            var ticket = new AuthenticationTicket(
                clientContext.User,
                clientContext.User.Identity?.AuthenticationType ?? string.Empty);
            var authenticateResult = AuthenticateResult.Success(ticket);

            return await policyEvaluator.AuthorizeAsync(policy, authenticateResult, ctx, clientContext);
        }

    }
}
