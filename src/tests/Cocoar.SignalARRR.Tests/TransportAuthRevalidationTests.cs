using System;
using System.Security.Claims;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers which credentials may be revalidated against the cached connection principal.
/// </summary>
/// <remarks>
/// A client that connected with a short-lived bearer token keeps an authenticated principal for the
/// life of the connection. Treating that as transport-level credentials meant revalidation asked the
/// cached principal whether it was authenticated — which it always is — so once a client had been
/// moved into transport mode, the token's own expiry and any revocation were never enforced again.
/// The escalation was reachable by the client itself: answer an authentication challenge with an
/// empty string, and the connection switched mode permanently.
/// </remarks>
public class TransportAuthRevalidationTests {

    private static ClaimsPrincipal PrincipalWith(string? authenticationType, params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType));

    // ---- which schemes are bound to the connection -----------------------------------------

    [Theory]
    [InlineData("Negotiate")]
    [InlineData("NTLM")]
    [InlineData("Kerberos")]
    [InlineData("Windows")]
    [InlineData("Certificate")]
    public void Connection_bound_schemes_are_transport_level(string scheme) {
        var user = PrincipalWith(scheme, new Claim(ClaimTypes.Name, "alice"));

        Assert.True(TransportCredentialPolicy.IsTransportLevel(certificate: null, user));
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("Cookies")]
    [InlineData("AuthenticationTypes.Federation")]
    [InlineData("oidc")]
    public void Message_bound_schemes_are_not_transport_level(string scheme) {
        // The escalation hinged on this: these principals are authenticated, so the previous
        // "IsAuthenticated == true" check accepted them as transport credentials.
        var user = PrincipalWith(scheme, new Claim(ClaimTypes.Name, "mallory"));

        Assert.False(TransportCredentialPolicy.IsTransportLevel(certificate: null, user));
    }

    [Fact]
    public void Scheme_matching_is_case_insensitive() {
        var user = PrincipalWith("negotiate", new Claim(ClaimTypes.Name, "alice"));

        Assert.True(TransportCredentialPolicy.IsTransportLevel(certificate: null, user));
    }

    [Fact]
    public void An_unauthenticated_principal_is_never_transport_level() {
        // A ClaimsIdentity without an authentication type is not authenticated.
        var user = PrincipalWith(authenticationType: null, new Claim(ClaimTypes.Name, "nobody"));

        Assert.False(TransportCredentialPolicy.IsTransportLevel(certificate: null, user));
    }

    [Fact]
    public void A_null_principal_is_never_transport_level() {
        Assert.False(TransportCredentialPolicy.IsTransportLevel(certificate: null, user: null));
    }

    // ---- expiry is honoured regardless of scheme -------------------------------------------

    [Fact]
    public void A_principal_without_an_expiry_claim_is_not_expired() {
        var user = PrincipalWith("Negotiate", new Claim(ClaimTypes.Name, "alice"));

        Assert.False(TransportCredentialPolicy.IsExpired(user));
    }

    [Fact]
    public void A_past_unix_expiry_is_expired() {
        var user = PrincipalWith("Negotiate",
            new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds().ToString()));

        Assert.True(TransportCredentialPolicy.IsExpired(user));
    }

    [Fact]
    public void A_future_unix_expiry_is_not_expired() {
        var user = PrincipalWith("Negotiate",
            new Claim("exp", DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds().ToString()));

        Assert.False(TransportCredentialPolicy.IsExpired(user));
    }

    [Fact]
    public void A_past_formatted_expiry_is_expired() {
        var user = PrincipalWith("Negotiate",
            new Claim(ClaimTypes.Expiration, DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O")));

        Assert.True(TransportCredentialPolicy.IsExpired(user));
    }

    [Fact]
    public void An_unparseable_expiry_fails_closed() {
        var user = PrincipalWith("Negotiate", new Claim("exp", "not-a-timestamp"));

        Assert.True(TransportCredentialPolicy.IsExpired(user));
    }
}
