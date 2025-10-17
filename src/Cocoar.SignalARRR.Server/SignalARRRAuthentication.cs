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

        //private IAuthenticationSchemeProvider? _schemes { get; }

        private IServiceProvider _serviceProvider;

        public SignalARRRAuthentication(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
            //_schemes = _serviceProvider.GetService<IAuthenticationSchemeProvider>();
        }

        //public async Task<HttpContext> Authenticate(ClientContext clientContext, string authorization, string scheme) {

        //    var authenticationService = _serviceProvider.GetRequiredService<IAuthenticationService>();
        //    var ctx = new DefaultHttpContext();
        //    ctx.RequestServices = _serviceProvider;

        //    if (String.IsNullOrWhiteSpace(scheme)) {
        //        var defaultScheme = await _schemes.GetDefaultAuthenticateSchemeAsync();
        //        scheme = defaultScheme?.Name;
        //    }

        //    AuthenticateResult authenticateResult;

        //    if (clientContext.UserValidUntil < DateTime.Now) {

        //        if (!authorization.Contains(" ")) {
        //            authorization = $"Bearer {authorization}";
        //        }
        //        ctx.Request.Headers["Authorization"] = authorization;

                
        //        authenticateResult = await authenticationService.AuthenticateAsync(ctx, scheme);

        //    } else {
        //        var t = new AuthenticationTicket(clientContext.User, clientContext.User.Identity.AuthenticationType);
        //        authenticateResult = AuthenticateResult.Success(t);
        //    }
        //    ctx.User = authenticateResult.Principal;
        //    clientContext.SetPrincipal(ctx.User);
        //    return ctx;
        //}


        
        public async Task<PolicyAuthorizationResult> Authorize(ClientContext clientContext, string authorization, MethodInfo methodInfo) {


            var authorizeData = methodInfo.GetAuthorizeData();

            if(!authorizeData.Any())
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
                        clientContext.SetPrincipal(authenticateResult.Principal);
                        break;
                    }
                }

                
            } else {
                var t = new AuthenticationTicket(clientContext.User, clientContext.User.Identity.AuthenticationType);
                authenticateResult = AuthenticateResult.Success(t);
            }

            ctx.User = authenticateResult.Principal;
            

            if (methodInfo.GetCustomAttribute<AllowAnonymousAttribute>() != null) {
                return PolicyAuthorizationResult.Success();
            }

            var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, ctx, clientContext);



            return authorizeResult;
        }

    }
}
