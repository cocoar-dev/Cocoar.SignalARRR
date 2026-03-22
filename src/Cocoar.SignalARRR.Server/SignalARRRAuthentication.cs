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
    public class SignalARRRAuthentication {

        private IServiceProvider _serviceProvider;

        public SignalARRRAuthentication(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
        }

        public async Task<PolicyAuthorizationResult> Authorize(ClientContext clientContext, string authorization, MethodInfo methodInfo) {

            // Check [AllowAnonymous] first — skip all auth if present
            if (methodInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null) {
                return PolicyAuthorizationResult.Success();
            }

            var authorizeData = methodInfo.GetAuthorizeData();

            if (!authorizeData.Any())
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
            if (clientContext.UserValidUntil < DateTime.Now) {
                // Token cache expired — need to re-authenticate

                if (String.IsNullOrWhiteSpace(authorization)) {
                    throw new ArgumentNullException("Authorization not provided!");
                }
                if (!authorization.Contains(" ")) {
                    authorization = $"Bearer {authorization}";
                }
                ctx.Request.Headers["Authorization"] = authorization;

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

    }
}
