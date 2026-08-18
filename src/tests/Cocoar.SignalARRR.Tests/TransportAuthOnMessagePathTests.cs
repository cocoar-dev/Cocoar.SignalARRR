using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers what happens to a transport-authenticated connection on the ordinary call path once its
/// auth cache expires.
/// </summary>
/// <remarks>
/// <c>Authorize</c> used to re-authenticate from scratch against a freshly built
/// <c>DefaultHttpContext</c>, which carries nothing of the connection's original request — no
/// cookie, no Negotiate handshake, no header. Only a client certificate survived, because it is
/// copied across explicitly. So a Windows- or Negotiate-authenticated client was denied every
/// <c>[Authorize]</c> call once <c>AuthCacheDuration</c> (default three minutes) had passed, while a
/// stream on the very same connection kept running — the per-element re-auth goes through
/// <see cref="SignalARRRAuthentication.AuthorizeWithPrincipal"/>, which trusts the revalidated
/// principal. Same credential, same connection, two answers.
/// </remarks>
public class TransportAuthOnMessagePathTests {

    // ---- a handler that authenticates from whatever the synthetic context actually carries ----

    private class ProbeOptions : AuthenticationSchemeOptions { }

    /// <summary>
    /// Stands in for a real handler: it authenticates from an <c>Authorization</c> header or a client
    /// certificate, and fails when the context carries neither — which is exactly the situation a
    /// cookie or Negotiate connection lands in once its cache has expired.
    /// </summary>
    private class ProbeHandler : AuthenticationHandler<ProbeOptions> {
        public ProbeHandler(IOptionsMonitor<ProbeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if (!string.IsNullOrEmpty(Request.Headers["Authorization"].ToString())) {
                return Task.FromResult(Ticket("from-header"));
            }
            if (Context.Connection.ClientCertificate != null) {
                return Task.FromResult(Ticket("from-certificate"));
            }
            return Task.FromResult(AuthenticateResult.Fail("nothing on the synthetic context"));
        }

        private static AuthenticateResult Ticket(string how) =>
            AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, how) }, "Probe")),
                "Probe"));
    }

    // ---- fixtures ----------------------------------------------------------------------------

    private static ClaimsPrincipal PrincipalWith(string scheme, params Claim[] extra) {
        var claims = new List<Claim> { new(ClaimTypes.Name, "alice") };
        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    /// <remarks>
    /// A real <see cref="ClientContext"/> needs a live hub and an HTTP context. Everything under test
    /// here reads only the principal, the mode, the cache stamp and the certificate, so an
    /// uninitialized instance with those four set is enough — and keeps the test on the decision
    /// rather than on hub plumbing.
    /// </remarks>
    private static ClientContext ContextWith(
        string scheme,
        AuthenticationMode mode,
        bool cacheExpired = true,
        bool withCertificate = false,
        IReadOnlyList<string>? connectionBoundSchemes = null,
        params Claim[] extraClaims) {

        var context = (ClientContext)RuntimeHelpers.GetUninitializedObject(typeof(ClientContext));
        context.SetPrincipal(PrincipalWith(scheme, extraClaims));
        context.AuthMode = mode;
        context.UserValidUntil = cacheExpired
            ? DateTime.UtcNow.AddMinutes(-1)
            : DateTime.UtcNow.AddMinutes(1);

        SetPrivate(context, "_connectionBoundSchemes", connectionBoundSchemes);
        SetPrivate(context, "_authCacheDuration", TimeSpan.FromMinutes(3));
        if (withCertificate) {
            SetPrivate(context, "<ClientCertificate>k__BackingField", SelfSigned());
        }
        return context;
    }

    private static void SetPrivate(ClientContext context, string field, object? value) =>
        typeof(ClientContext)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, value);

    private static X509Certificate2 SelfSigned() {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=probe", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static SignalARRRAuthentication Authentication() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Probe").AddScheme<ProbeOptions, ProbeHandler>("Probe", _ => { });
        services.AddAuthorization();
        // The probe certificate is self-signed, so chain building would fail before the question
        // under test is reached. Certificate validity itself is covered by TransportAuthRevalidationTests.
        services.AddSingleton(new SignalARRRServerOptions { CustomCertificateValidator = _ => true });
        return new SignalARRRAuthentication(services.BuildServiceProvider());
    }

    private static MethodInfo ProtectedMethod() => typeof(Guarded).GetMethod(nameof(Guarded.Method))!;

    private class Guarded {
        [Authorize]
        public void Method() { }
    }

    // ---- connection-bound credentials survive cache expiry ----------------------------------

    [Theory]
    [InlineData("Negotiate")]
    [InlineData("NTLM")]
    [InlineData("Kerberos")]
    [InlineData("Windows")]
    public async Task A_connection_bound_credential_is_accepted_after_the_cache_expires(string scheme) {
        // Each of these was denied: revalidation passed, and the scheme loop below it then threw the
        // result away by re-authenticating against a context carrying nothing.
        var context = ContextWith(scheme, AuthenticationMode.TransportLevel);

        var result = await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_client_certificate_is_accepted_after_the_cache_expires() {
        var context = ContextWith("Certificate", AuthenticationMode.TransportLevel, withCertificate: true);

        var result = await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Accepting_it_extends_the_cache_so_the_next_message_does_not_revalidate() {
        // Revalidating a certificate means chain building, potentially with CRL/OCSP network I/O.
        // Leaving the stamp in the past put that on every single message.
        var context = ContextWith("Negotiate", AuthenticationMode.TransportLevel);

        await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.True(context.UserValidUntil > DateTime.UtcNow);
    }

    // ---- the credential's own lifetime is still enforced -------------------------------------

    [Fact]
    public async Task An_expired_ticket_is_rejected_even_for_a_connection_bound_scheme() {
        // The guard that keeps the shortcut above from becoming a way to outlive the credential.
        var expired = new Claim("exp", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString());
        var context = ContextWith("Negotiate", AuthenticationMode.TransportLevel, extraClaims: expired);

        var result = await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.False(result.Succeeded);
        Assert.True(result.Forbidden);
    }

    [Fact]
    public async Task A_message_level_client_without_a_token_is_still_challenged() {
        var context = ContextWith("Bearer", AuthenticationMode.MessageLevel);

        var result = await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.False(result.Succeeded);
        Assert.True(result.Challenged);
    }

    [Fact]
    public async Task A_message_level_client_with_a_token_is_accepted() {
        var context = ContextWith("Bearer", AuthenticationMode.MessageLevel);

        var result = await Authentication().Authorize(context, "a-token", ProtectedMethod());

        Assert.True(result.Succeeded);
    }

    // ---- cookies are opt-in ------------------------------------------------------------------

    [Fact]
    public void A_cookie_is_not_connection_bound_by_default() {
        // Unchanged posture: SignalARRR cannot tell a cookie identity from a bearer one by
        // inspection, and guessing is how a bearer token once escaped its own expiry.
        var context = ContextWith("Cookies", AuthenticationMode.None);

        Assert.False(context.HasTransportLevelCredentials());
    }

    [Fact]
    public void A_cookie_is_connection_bound_once_the_application_declares_it() {
        var context = ContextWith("Cookies", AuthenticationMode.None,
            connectionBoundSchemes: new[] { "Cookies" });

        Assert.True(context.HasTransportLevelCredentials());
    }

    [Fact]
    public void Declaring_one_scheme_does_not_admit_another() {
        var context = ContextWith("Bearer", AuthenticationMode.None,
            connectionBoundSchemes: new[] { "Cookies" });

        Assert.False(context.HasTransportLevelCredentials());
    }

    [Fact]
    public async Task A_declared_cookie_connection_is_accepted_after_the_cache_expires() {
        // The whole point of the opt-in: with it, a cookie-authenticated client keeps working past
        // AuthCacheDuration instead of being denied every call from minute three onwards.
        var context = ContextWith("Cookies", AuthenticationMode.TransportLevel,
            connectionBoundSchemes: new[] { "Cookies" });

        var result = await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.True(result.Succeeded);
    }
}
