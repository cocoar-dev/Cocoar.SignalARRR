using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TestServer.LocalTokenAuthenticatonHandler
{
    public class TestAuthenticationHandler : AuthenticationHandler<TestAuthenticationOptions>
    {

        public TestAuthenticationHandler(IOptionsMonitor<TestAuthenticationOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock) : base(options, logger, encoder, clock)
        {
         
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {


            //var endp = this.Context.GetEndpoint();

            var claims = new List<Claim>();
            claims.Add(new Claim("name", "Testuser"));
            claims.Add(new Claim("role", "testrole"));
            claims.Add(new Claim("access_token", this.Request.Headers["Authorization"]));
            ClaimsIdentity claimsIdentity = new ClaimsIdentity(claims, "access_token", "name", "role");
            ClaimsPrincipal claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
            AuthenticationTicket authenticationTicket = new AuthenticationTicket(claimsPrincipal, "test");
            return Task.FromResult(AuthenticateResult.Success(authenticationTicket));

           
        }

    }
}
