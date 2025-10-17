using System;
using Microsoft.AspNetCore.Authentication;

namespace TestServer.LocalTokenAuthenticatonHandler
{
    public static class TestAuthenticationExtensions
    {
        public static AuthenticationBuilder AddTestTokenValidation(this AuthenticationBuilder builder)
            => builder.AddTestTokenValidation("AccessToken", _ => { });

        private static AuthenticationBuilder AddTestTokenValidation(this AuthenticationBuilder builder, Action<TestAuthenticationOptions> configureOptions)
            => builder.AddTestTokenValidation("AccessToken", configureOptions);

        private static AuthenticationBuilder AddTestTokenValidation(this AuthenticationBuilder builder, string authenticationScheme, Action<TestAuthenticationOptions> configureOptions)
            => builder.AddTestTokenValidation(authenticationScheme, displayName: "Local Authentication", configureOptions: configureOptions);

        private static AuthenticationBuilder AddTestTokenValidation(this AuthenticationBuilder builder, string authenticationScheme, string displayName, Action<TestAuthenticationOptions> configureOptions)
        {
            return builder.AddScheme<TestAuthenticationOptions, TestAuthenticationHandler>(authenticationScheme, displayName, configureOptions);
        }
    }
}
