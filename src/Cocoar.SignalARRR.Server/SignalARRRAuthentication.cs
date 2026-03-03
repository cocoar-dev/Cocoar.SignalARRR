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


            var authorizeData = methodInfo.GetAuthorizeData();

            if (!authorizeData.Any())
                return PolicyAuthorizationResult.Success();

            var authenticationService = _serviceProvider.GetRequiredService<IAuthenticationService>();
            var policyEvaluator = _serviceProvider.GetRequiredService<IPolicyEvaluator>();
            var policyProvider = _serviceProvider.GetRequiredService<IAuthorizationPolicyProvider>();


            if (!authorizeData.Any()) {
                authorizeData = methodInfo.DeclaringType?.GetCustomAttributes<AuthorizeAttribute>().ToList() ?? new List<AuthorizeAttribute>();
            }



            var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);

            if (policy == null) {
                return PolicyAuthorizationResult.Success();
            }

            var ctx = new DefaultHttpContext();
            ctx.RequestServices = _serviceProvider;

            AuthenticateResult authenticateResult = AuthenticateResult.NoResult();
            if (clientContext.UserValidUntil < DateTime.Now) {

                if (String.IsNullOrWhiteSpace(authorization)) {
                    throw new ArgumentNullException("Authorization not provided!");
                }
                if (!authorization.Contains(" ")) {
                    authorization = $"Bearer {authorization}";
                }
                ctx.Request.Headers["Authorization"] = authorization;


                foreach (var policyAuthenticationScheme in policy.AuthenticationSchemes) {

                    authenticateResult = await authenticationService.AuthenticateAsync(ctx, policyAuthenticationScheme);
                    if (authenticateResult.Succeeded) {
                        clientContext.SetPrincipal(authenticateResult.Principal!);
                        break;
                    }
                }


            } else {
                var t = new AuthenticationTicket(clientContext.User, clientContext.User.Identity?.AuthenticationType ?? string.Empty);
                authenticateResult = AuthenticateResult.Success(t);
            }

            ctx.User = authenticateResult.Principal ?? new System.Security.Claims.ClaimsPrincipal();


            if (methodInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null) {
                return PolicyAuthorizationResult.Success();
            }

            var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, ctx, clientContext);



            return authorizeResult;
        }

    }
}
