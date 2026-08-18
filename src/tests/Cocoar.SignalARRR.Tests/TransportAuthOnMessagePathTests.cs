using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

    private static IServiceProvider Services() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Probe").AddScheme<ProbeOptions, ProbeHandler>("Probe", _ => { });
        services.AddAuthorization();
        // The probe certificate is self-signed, so chain building would fail before the question
        // under test is reached. Certificate validity itself is covered by TransportAuthRevalidationTests.
        services.AddSingleton(new SignalARRRServerOptions { CustomCertificateValidator = _ => true });
        // No ClientContextDispatcher on purpose — see StreamingContextWithoutADispatcher.
        return services.BuildServiceProvider();
    }

    private static SignalARRRAuthentication Authentication() => new(Services());

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

    // ---- no credential at all falls back to the connection's own principal ------------------

    [Fact]
    public async Task A_client_that_sends_no_credential_keeps_the_principal_it_connected_with() {
        // This was a flat denial, and it caught nothing: the connection is authenticated, the
        // principal is right here, and SignalR itself would honour it for the life of the socket.
        // Denying hit valid sessions exactly as hard as expired ones, three minutes in.
        var context = ContextWith("Bearer", AuthenticationMode.MessageLevel);

        var result = await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task The_fallback_still_honours_an_expiry_the_principal_states() {
        // Which makes it stricter than plain SignalR, where `exp` is never looked at again once
        // negotiate is past. A token that has actually expired stops working; one that has not, does not.
        var expired = new Claim("exp", DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds().ToString());
        var context = ContextWith("Bearer", AuthenticationMode.MessageLevel, extraClaims: expired);

        var result = await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.False(result.Succeeded);
        Assert.True(result.Forbidden);
    }

    [Fact]
    public async Task The_fallback_does_not_let_an_anonymous_connection_through() {
        // No special case in the fallback for this: policy evaluation rejects an unauthenticated
        // principal, which is the right authority for the decision.
        var context = (ClientContext)RuntimeHelpers.GetUninitializedObject(typeof(ClientContext));
        context.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity()));
        context.AuthMode = AuthenticationMode.None;
        context.UserValidUntil = DateTime.UtcNow.AddMinutes(-1);

        var result = await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task The_fallback_does_not_extend_the_cache() {
        // Extending it would let a token outlive its own `exp` by up to one cache duration. The
        // expiry check is cheap enough to run per message.
        var context = ContextWith("Bearer", AuthenticationMode.MessageLevel);

        await Authentication().Authorize(context, string.Empty, ProtectedMethod());

        Assert.True(context.UserValidUntil < DateTime.UtcNow);
    }

    [Fact]
    public async Task A_message_level_client_with_a_token_is_accepted() {
        var context = ContextWith("Bearer", AuthenticationMode.MessageLevel);

        var result = await Authentication().Authorize(context, "a-token", ProtectedMethod());

        Assert.True(result.Succeeded);
    }

    // ---- the re-authentication context carries the connection's request facts ----------------

    /// <summary>
    /// Authenticates only when the context says which tenant the request arrived for — the shape of
    /// any scheme whose trusted issuers, keys or introspection endpoint are per-tenant.
    /// </summary>
    private class TenantBoundHandler : AuthenticationHandler<ProbeOptions> {
        public TenantBoundHandler(IOptionsMonitor<ProbeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            var credential = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(credential)) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            // The realm a middleware stamped before authentication ran, and the host it came in on.
            if (Context.Items["tenant"] is not string tenant || !Request.Host.HasValue) {
                return Task.FromResult(AuthenticateResult.Fail("no tenant on the context — nothing to trust"));
            }
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.Name, $"{tenant}@{Request.Host.Host}") }, "Probe")),
                "Probe")));
        }
    }

    private static SignalARRRAuthentication TenantBoundAuthentication() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Probe").AddScheme<ProbeOptions, TenantBoundHandler>("Probe", _ => { });
        services.AddAuthorization();
        return new SignalARRRAuthentication(services.BuildServiceProvider());
    }

    private static ClientContext ContextArrivedAt(string host, string? tenant) {
        var context = ContextWith("Bearer", AuthenticationMode.MessageLevel);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString(host);
        httpContext.Request.Path = "/signalr/ui";
        if (tenant != null) {
            httpContext.Items["tenant"] = tenant;
        }
        SetPrivate(context, "<RequestSnapshot>k__BackingField", new ConnectionRequestSnapshot(httpContext));
        return context;
    }

    [Fact]
    public async Task Re_authentication_sees_where_the_connection_arrived() {
        // The context was fabricated blank, carrying only the Authorization header — so a handler
        // that resolves its trust from the request had nothing and failed closed, and the connection
        // was denied from the moment the auth cache lapsed.
        var context = ContextArrivedAt("alpha.example.test", tenant: "alpha");

        var result = await TenantBoundAuthentication().Authorize(context, "a-token", ProtectedMethod());

        Assert.True(result.Succeeded);
        Assert.Equal("alpha@alpha.example.test", context.User.Identity!.Name);
    }

    [Fact]
    public async Task A_handler_that_finds_no_context_still_fails_closed() {
        var context = ContextArrivedAt("alpha.example.test", tenant: null);

        var result = await TenantBoundAuthentication().Authorize(context, "a-token", ProtectedMethod());

        Assert.False(result.Succeeded);
    }

    // ---- a client with nothing to give is asked once, not per element ------------------------

    private class ProbeHub : HARRR {
        public ProbeHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
    }

    /// <remarks>
    /// The service provider handed to the context has no <c>ClientContextDispatcher</c> registered,
    /// so resolving one throws. That is the assertion: reaching the challenge at all is observable,
    /// without having to stand up a hub context to count round trips.
    /// </remarks>
    private static ClientContext StreamingContextWithoutADispatcher() {
        var context = ContextWith("Bearer", AuthenticationMode.MessageLevel);
        var services = Services();
        SetPrivate(context, "<ServiceProvider>k__BackingField", services);
        SetPrivate(context, "<ScopeFactory>k__BackingField", services.GetRequiredService<IServiceScopeFactory>());
        SetPrivate(context, "<HARRRType>k__BackingField", typeof(ProbeHub));
        return context;
    }

    [Fact]
    public async Task The_first_expired_element_challenges_the_client() {
        var context = StreamingContextWithoutADispatcher();

        await Assert.ThrowsAnyAsync<Exception>(() => context.TryAuthenticate(ProtectedMethod()));
    }

    [Fact]
    public async Task A_client_that_answered_with_nothing_is_not_challenged_again() {
        // Per streamed element this was a round trip each, once the fallback stopped the empty
        // answer from killing the stream outright.
        var context = StreamingContextWithoutADispatcher();
        SetPrivate(context, "_clientHasNoCredentialToGive", true);

        var result = await context.TryAuthenticate(ProtectedMethod());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void A_credential_arriving_later_makes_the_client_worth_asking_again() {
        var context = StreamingContextWithoutADispatcher();
        SetPrivate(context, "_clientHasNoCredentialToGive", true);

        context.SetPrincipal(PrincipalWith("Bearer"));

        Assert.False((bool)typeof(ClientContext)
            .GetField("_clientHasNoCredentialToGive", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(context)!);
    }

    // ---- revalidation can say more than yes or no --------------------------------------------

    private class ScriptedRevalidation : ITransportAuthRevalidationService {
        private readonly RevalidationResult _result;
        public ScriptedRevalidation(RevalidationResult result) => _result = result;
        public Task<RevalidationResult> RevalidateAsync(ClientContext c, CancellationToken ct = default)
            => Task.FromResult(_result);
    }

    private static SignalARRRAuthentication AuthenticationRevalidatingWith(RevalidationResult result) {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Probe").AddScheme<ProbeOptions, ProbeHandler>("Probe", _ => { });
        services.AddAuthorization();
        services.AddSingleton<ITransportAuthRevalidationService>(new ScriptedRevalidation(result));
        return new SignalARRRAuthentication(services.BuildServiceProvider());
    }

    [Fact]
    public async Task A_revalidation_can_state_how_long_its_verdict_holds() {
        // AuthCacheDuration is one number for the whole server, and credentials on the same hub do
        // not want the same cadence. The service that knows can say so.
        var context = ContextWith("Negotiate", AuthenticationMode.TransportLevel);

        await AuthenticationRevalidatingWith(RevalidationResult.ValidForDuration(TimeSpan.FromSeconds(30)))
            .Authorize(context, string.Empty, ProtectedMethod());

        Assert.InRange(context.UserValidUntil, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(31));
    }

    [Fact]
    public async Task Denying_leaves_the_connection_up() {
        var context = ContextWith("Negotiate", AuthenticationMode.TransportLevel);
        var aborted = false;
        SetPrivate(context, "_abort", (Action)(() => aborted = true));

        var result = await AuthenticationRevalidatingWith(RevalidationResult.Deny())
            .Authorize(context, string.Empty, ProtectedMethod());

        Assert.True(result.Forbidden);
        Assert.False(aborted);
    }

    [Fact]
    public async Task Aborting_drops_the_connection() {
        // Refusing each call while the socket stays up leaves a client that believes it is connected
        // and a server that will not serve it — worse than either alternative on a push connection.
        var context = ContextWith("Negotiate", AuthenticationMode.TransportLevel);
        var aborted = false;
        SetPrivate(context, "_abort", (Action)(() => aborted = true));

        var result = await AuthenticationRevalidatingWith(RevalidationResult.Abort())
            .Authorize(context, string.Empty, ProtectedMethod());

        Assert.True(result.Forbidden);
        Assert.True(aborted);
    }

    [Fact]
    public void Abort_on_a_connection_that_is_already_gone_is_not_an_error() {
        var context = ContextWith("Negotiate", AuthenticationMode.TransportLevel);
        SetPrivate(context, "_abort", (Action)(() => throw new ObjectDisposedException("connection")));

        context.Abort();
    }

    [Fact]
    public async Task A_bool_still_works_for_an_existing_implementation() {
        var context = ContextWith("Negotiate", AuthenticationMode.TransportLevel);

        var result = await AuthenticationRevalidatingWith(true).Authorize(context, string.Empty, ProtectedMethod());

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
