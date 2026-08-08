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

                        // Re-validation succeeded — set cert on synthetic context so auth handlers can find it
                        if (clientContext.ClientCertificate != null) {
                            ctx.Connection.ClientCertificate = clientContext.ClientCertificate;
                        }
                    } else {
                        // Previously threw ArgumentNullException with the message passed as the
                        // parameter name, so a client whose token had simply expired got a mangled
                        // argument error instead of an authentication challenge -- and clients that
                        // key off the error type to trigger a token refresh could not recognise it.
                        return PolicyAuthorizationResult.Challenge();
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
