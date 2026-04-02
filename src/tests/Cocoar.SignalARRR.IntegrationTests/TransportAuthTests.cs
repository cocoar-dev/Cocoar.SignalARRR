using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Cocoar.SignalARRR.Common.Attributes;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    #region Transport Auth Test Infrastructure

    /// <summary>
    /// Authentication handler that validates client certificates from the TLS connection.
    /// Maps the certificate subject CN to claims.
    /// </summary>
    public class CertTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions> {
        public CertTestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            var cert = Context.Connection.ClientCertificate;
            if (cert == null) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var cn = cert.GetNameInfo(X509NameType.SimpleName, false) ?? "Unknown";
            var claims = new List<Claim> {
                new("name", cn),
                new(ClaimTypes.Role, "certrole"),
                new("thumbprint", cert.Thumbprint)
            };
            var identity = new ClaimsIdentity(claims, "Certificate", "name", ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Certificate");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    [Authorize(AuthenticationSchemes = "Certificate")]
    public class CertAuthTestHub : HARRR {
        public CertAuthTestHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
    }

    [MessageName("CertAuthMethods")]
    public class CertAuthTestServerMethods : ServerMethods<CertAuthTestHub>, ITestServerMethods {
        public string GetName() => "CertMethodName";
        public Task<string> GetNameAsync() => Task.FromResult("CertMethodNameAsync");
        public Guid GetGuid() => Guid.NewGuid();
        public Task<Guid> GetGuidAsync() => Task.FromResult(Guid.NewGuid());
        public void Nothing() { }
        public Task NothingAsync() => Task.CompletedTask;
    }

    [MessageName("CertAuthExtraMethods")]
    public class CertAuthExtraServerMethods : ServerMethods<CertAuthTestHub> {
        [AllowAnonymous]
        public string PublicInfo() => "PublicCertValue";

        public string SecretInfo() => "SecretCertValue";
    }

    /// <summary>
    /// Also provides a Bearer-auth hub on the same server for mixed-mode tests.
    /// </summary>
    public class MixedAuthTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions> {
        public MixedAuthTestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            // Try cert first
            var cert = Context.Connection.ClientCertificate;
            if (cert != null) {
                var cn = cert.GetNameInfo(X509NameType.SimpleName, false) ?? "Unknown";
                var claims = new List<Claim> {
                    new("name", cn),
                    new(ClaimTypes.Role, "testrole"),
                    new("auth_method", "certificate")
                };
                var identity = new ClaimsIdentity(claims, "MixedScheme", "name", ClaimTypes.Role);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), "MixedScheme")));
            }

            // Fall back to token
            var token = Request.Headers["Authorization"].ToString();
            if (!string.IsNullOrEmpty(token)) {
                var claims = new List<Claim> {
                    new("name", "TokenUser"),
                    new(ClaimTypes.Role, "testrole"),
                    new("auth_method", "token")
                };
                var identity = new ClaimsIdentity(claims, "MixedScheme", "name", ClaimTypes.Role);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), "MixedScheme")));
            }

            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    [Authorize]
    public class MixedAuthTestHub : HARRR {
        public MixedAuthTestHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
    }

    [MessageName("MixedAuthMethods")]
    public class MixedAuthTestServerMethods : ServerMethods<MixedAuthTestHub> {
        public string GetAuthMethod() => ClientContext.User.FindFirst("auth_method")?.Value ?? "unknown";
    }

    public static class TestCertificateHelper {
        /// <summary>
        /// Generates a self-signed certificate for testing.
        /// </summary>
        public static X509Certificate2 CreateSelfSignedCert(string cn, TimeSpan? validity = null) {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));

            var notBefore = DateTimeOffset.UtcNow.AddMinutes(-1);
            var notAfter = notBefore.Add(validity ?? TimeSpan.FromHours(1));
            var cert = request.CreateSelfSigned(notBefore, notAfter);

            // Export and re-import to ensure private key is usable on all platforms
            return LoadFromPfxBytes(cert.Export(X509ContentType.Pfx));
        }

        /// <summary>
        /// Creates a certificate that is already expired.
        /// </summary>
        public static X509Certificate2 CreateExpiredCert(string cn) {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={cn}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var notBefore = DateTimeOffset.UtcNow.AddHours(-2);
            var notAfter = DateTimeOffset.UtcNow.AddHours(-1);
            var cert = request.CreateSelfSigned(notBefore, notAfter);
            return LoadFromPfxBytes(cert.Export(X509ContentType.Pfx));
        }

        private static X509Certificate2 LoadFromPfxBytes(byte[] pfxBytes) {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(pfxBytes, null);
#else
            return new X509Certificate2(pfxBytes);
#endif
        }
    }

    public class TransportAuthTestServerFixture : IDisposable {
        private readonly IHost _host;
        public string ServerUrl { get; }
        public X509Certificate2 ServerCert { get; }
        public X509Certificate2 ClientCert { get; }
        public X509Certificate2 ExpiredClientCert { get; }

        public TransportAuthTestServerFixture() {
            ServerCert = TestCertificateHelper.CreateSelfSignedCert("TestServer");
            ClientCert = TestCertificateHelper.CreateSelfSignedCert("TestClient");
            ExpiredClientCert = TestCertificateHelper.CreateExpiredCert("ExpiredClient");

            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webBuilder => {
                    webBuilder
                        .UseKestrel(kestrel => {
                            kestrel.Listen(System.Net.IPAddress.Loopback, 0, listenOptions => {
                                listenOptions.UseHttps(https => {
                                    https.ServerCertificate = ServerCert;
                                    https.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                                    // Accept all client certs at TLS level — validation happens in auth handler
                                    https.ClientCertificateValidation = (cert, chain, errors) => true;
                                });
                            });
                        })
                        .ConfigureServices(services => {
                            services.AddRouting();
                            services.AddSignalR().AddJsonProtocol(options => {
                                options.PayloadSerializerOptions.PropertyNamingPolicy = null;
                                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                                options.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                            });

                            // Default scheme handles both cert and token auth
                            services.AddAuthentication("MixedScheme")
                                .AddScheme<AuthenticationSchemeOptions, CertTestAuthenticationHandler>("Certificate", null)
                                .AddScheme<AuthenticationSchemeOptions, MixedAuthTestAuthenticationHandler>("MixedScheme", null);

                            services.AddAuthorization();

                            services.AddSignalARRR(builder => {
                                builder.AddServerMethodsFrom(typeof(CertAuthTestHub).Assembly);
                                // Disable CRL/OCSP for test certs (self-signed, no revocation endpoints)
                                builder.WithCertificateRevocationCheck(false);
                            });
                        })
                        .Configure(app => {
                            app.UseRouting();
                            app.UseAuthentication();
                            app.UseAuthorization();
                            app.UseEndpoints(endpoints => {
                                endpoints.MapHARRRController<CertAuthTestHub>("/signalr/certauthhub");
                                endpoints.MapHARRRController<MixedAuthTestHub>("/signalr/mixedauthhub");

                                // Test trigger: expire a client's auth cache
                                endpoints.MapPost("/__test/expire-cert-auth-cache", async context => {
                                    var connectionId = context.Request.Query["connectionId"].ToString();
                                    if (string.IsNullOrWhiteSpace(connectionId)) {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync("Missing connectionId");
                                        return;
                                    }
                                    var clientManager = context.RequestServices.GetRequiredService<ClientManager>();
                                    var client = clientManager.GetClientById(connectionId);
                                    client.UserValidUntil = DateTime.MinValue;
                                    await context.Response.WriteAsync("Expired");
                                });

                                // Test trigger: get client's auth mode
                                endpoints.MapGet("/__test/auth-mode", context => {
                                    var connectionId = context.Request.Query["connectionId"].ToString();
                                    var clientManager = context.RequestServices.GetRequiredService<ClientManager>();
                                    var client = clientManager.GetClientById(connectionId);
                                    return context.Response.WriteAsync(client.AuthMode.ToString());
                                });

                                // Debug endpoint: dump client context state
                                endpoints.MapGet("/__test/client-debug", context => {
                                    var connectionId = context.Request.Query["connectionId"].ToString();
                                    var clientManager = context.RequestServices.GetRequiredService<ClientManager>();
                                    var client = clientManager.GetClientById(connectionId);
                                    var hasCert = client.ClientCertificate != null;
                                    var certExpired = hasCert && client.ClientCertificate!.NotAfter < DateTime.Now;
                                    var isAuth = client.User.Identity?.IsAuthenticated == true;
                                    var authType = client.User.Identity?.AuthenticationType ?? "null";
                                    var cacheValid = client.UserValidUntil >= DateTime.Now;
                                    return context.Response.WriteAsync(
                                        $"AuthMode={client.AuthMode}, HasCert={hasCert}, CertExpired={certExpired}, " +
                                        $"IsAuth={isAuth}, AuthType={authType}, CacheValid={cacheValid}");
                                });

                                // Debug endpoint: directly test revalidation
                                endpoints.MapGet("/__test/revalidate", async context => {
                                    var connectionId = context.Request.Query["connectionId"].ToString();
                                    var clientManager = context.RequestServices.GetRequiredService<ClientManager>();
                                    var client = clientManager.GetClientById(connectionId);
                                    var svc = context.RequestServices.GetService<ITransportAuthRevalidationService>();
                                    var svcType = svc?.GetType().Name ?? "null";
                                    var result = svc != null ? await svc.RevalidateAsync(client) : false;
                                    await context.Response.WriteAsync($"Service={svcType}, Result={result}");
                                });

                                // Test trigger: set/clear CustomCertificateValidator on the server options
                                endpoints.MapPost("/__test/set-cert-validator", async context => {
                                    var mode = context.Request.Query["mode"].ToString();
                                    var serverOptions = context.RequestServices.GetRequiredService<SignalARRRServerOptions>();
                                    serverOptions.CustomCertificateValidator = mode == "reject"
                                        ? _ => false
                                        : null;
                                    await context.Response.WriteAsync("OK");
                                });
                            });
                        });
                });

            _host = hostBuilder.Start();
            var addresses = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
            ServerUrl = addresses!.Addresses.First();
        }

        public void Dispose() {
            _host?.StopAsync().GetAwaiter().GetResult();
            _host?.Dispose();
            ServerCert.Dispose();
            ClientCert.Dispose();
            ExpiredClientCert.Dispose();
        }
    }

    [CollectionDefinition("TransportAuth")]
    public class TransportAuthSignalARRCollection : ICollectionFixture<TransportAuthTestServerFixture> { }

    #endregion

    [Collection("TransportAuth")]
    public class TransportAuthTests : IAsyncLifetime {
        private readonly TransportAuthTestServerFixture _fixture;
        private HARRRConnection? _connection;
        private HARRRConnection? _connection2;

        public TransportAuthTests(TransportAuthTestServerFixture fixture) {
            _fixture = fixture;
        }

        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        public async ValueTask DisposeAsync() {
            if (_connection != null) {
                try { await _connection.StopAsync(); } catch { }
                await _connection.DisposeAsync();
            }
            if (_connection2 != null) {
                try { await _connection2.StopAsync(); } catch { }
                await _connection2.DisposeAsync();
            }
        }

        private HARRRConnection CreateCertConnection(X509Certificate2 clientCert, string hubPath = "/signalr/certauthhub") {
            return HARRRConnection.Create(builder => {
                builder.WithUrl($"{_fixture.ServerUrl}{hubPath}", options => {
                    options.ClientCertificates = new System.Security.Cryptography.X509Certificates.X509CertificateCollection { clientCert };
                    options.HttpMessageHandlerFactory = handler => {
                        if (handler is HttpClientHandler clientHandler) {
                            // Trust the self-signed server cert
                            clientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                            clientHandler.ClientCertificates.Add(clientCert);
                        } else if (handler is SocketsHttpHandler socketsHandler) {
                            socketsHandler.SslOptions = new SslClientAuthenticationOptions {
                                RemoteCertificateValidationCallback = (_, _, _, _) => true,
                                ClientCertificates = new X509CertificateCollection { clientCert }
                            };
                        }
                        return handler;
                    };
                });
            });
        }

        private HARRRConnection CreateTokenConnection(string token, string hubPath = "/signalr/mixedauthhub") {
            return HARRRConnection.Create(builder => {
                builder.WithUrl($"{_fixture.ServerUrl}{hubPath}", options => {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                    options.HttpMessageHandlerFactory = handler => {
                        if (handler is HttpClientHandler clientHandler) {
                            clientHandler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
                        } else if (handler is SocketsHttpHandler socketsHandler) {
                            socketsHandler.SslOptions = new SslClientAuthenticationOptions {
                                RemoteCertificateValidationCallback = (_, _, _, _) => true
                            };
                        }
                        return handler;
                    };
                });
            });
        }

        private HttpClient CreateTestHttpClient() {
            var handler = new HttpClientHandler {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            return new HttpClient(handler);
        }

        // ──────────────────────────────────────────────
        // Test 1: Basic cert auth
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CertAuth_ValidCert_Succeeds() {
            _connection = CreateCertConnection(_fixture.ClientCert);
            await _connection.StartAsync(TestContext.Current.CancellationToken);

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = await typedClient.GetNameAsync();

            Assert.Equal("CertMethodNameAsync", result);
        }

        [Fact]
        public async Task CertAuth_ValidCert_Sync_Succeeds() {
            _connection = CreateCertConnection(_fixture.ClientCert);
            await _connection.StartAsync(TestContext.Current.CancellationToken);

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = typedClient.GetName();

            Assert.Equal("CertMethodName", result);
        }

        // ──────────────────────────────────────────────
        // Test 2: Auth mode detection
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CertAuth_DetectsTransportLevelMode() {
            var ct = TestContext.Current.CancellationToken;
            _connection = CreateCertConnection(_fixture.ClientCert);
            await _connection.StartAsync(ct);

            // Verify the server detected transport-level auth
            using var http = CreateTestHttpClient();
            var response = await http.GetStringAsync(
                $"{_fixture.ServerUrl}/__test/auth-mode?connectionId={_connection.ConnectionId}", ct);

            Assert.Equal("TransportLevel", response);
        }

        // ──────────────────────────────────────────────
        // Test 3: Cache expiry with server-side revalidation
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CertAuth_ExpiredCache_RevalidatesServerSide() {
            var ct = TestContext.Current.CancellationToken;
            _connection = CreateCertConnection(_fixture.ClientCert);
            await _connection.StartAsync(ct);

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();

            // First call succeeds (cache is valid from connect)
            var result1 = await typedClient.GetNameAsync();
            Assert.Equal("CertMethodNameAsync", result1);

            // Expire the auth cache on the server
            using var http = CreateTestHttpClient();
            await http.PostAsync(
                $"{_fixture.ServerUrl}/__test/expire-cert-auth-cache?connectionId={_connection.ConnectionId}",
                null, ct);

            // Second call succeeds — server re-validates cert server-side (no challenge)
            var result2 = await typedClient.GetNameAsync();
            Assert.Equal("CertMethodNameAsync", result2);
        }

        // ──────────────────────────────────────────────
        // Test 4: No challenge sent to transport-auth client
        // Verified by: cert client with no AccessTokenProvider succeeds after cache expiry.
        // If a challenge were sent and no token returned, the old code would throw.
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CertAuth_NoChallengeToClient() {
            var ct = TestContext.Current.CancellationToken;

            // Connect with cert only — no AccessTokenProvider at all
            _connection = CreateCertConnection(_fixture.ClientCert);
            await _connection.StartAsync(ct);

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();

            // First call
            var result1 = await typedClient.GetNameAsync();
            Assert.Equal("CertMethodNameAsync", result1);

            // Expire cache
            using var http = CreateTestHttpClient();
            await http.PostAsync(
                $"{_fixture.ServerUrl}/__test/expire-cert-auth-cache?connectionId={_connection.ConnectionId}",
                null, ct);

            // Second call after cache expiry — succeeds without challenge.
            // If the server tried to challenge, the client would return null/empty,
            // and the server would detect transport auth and re-validate server-side.
            var result2 = await typedClient.GetNameAsync();
            Assert.Equal("CertMethodNameAsync", result2);

            // Verify auth mode is still TransportLevel (not upgraded to MessageLevel)
            var authMode = await http.GetStringAsync(
                $"{_fixture.ServerUrl}/__test/auth-mode?connectionId={_connection.ConnectionId}", ct);
            Assert.Equal("TransportLevel", authMode);
        }

        // ──────────────────────────────────────────────
        // Test 5: Custom validator can reject cert
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CertAuth_ExpiredCert_RevalidationServiceRejects() {
            var ct = TestContext.Current.CancellationToken;

            // Connect with an already-expired cert. TLS accepts it (custom validation callback)
            // and the auth handler creates claims from it.
            _connection = CreateCertConnection(_fixture.ExpiredClientCert);
            await _connection.StartAsync(ct);

            using var http = CreateTestHttpClient();

            // Verify the client has transport-level auth with an expired cert
            var debug = await http.GetStringAsync(
                $"{_fixture.ServerUrl}/__test/client-debug?connectionId={_connection.ConnectionId}", ct);
            Assert.Contains("AuthMode=TransportLevel", debug);
            Assert.Contains("CertExpired=True", debug);

            // Verify the revalidation service correctly rejects the expired cert
            var revalidateResult = await http.GetStringAsync(
                $"{_fixture.ServerUrl}/__test/revalidate?connectionId={_connection.ConnectionId}", ct);
            Assert.Contains("Result=False", revalidateResult);
            Assert.Contains("DefaultTransportAuthRevalidationService", revalidateResult);
        }

        // ──────────────────────────────────────────────
        // Test 6: AllowAnonymous works with cert auth
        // ──────────────────────────────────────────────

        [Fact]
        public async Task CertAuth_AllowAnonymous_BypassesAuth() {
            _connection = CreateCertConnection(_fixture.ClientCert);
            await _connection.StartAsync(TestContext.Current.CancellationToken);

            var result = await _connection.InvokeCoreAsync<string>(
                new Cocoar.SignalARRR.Common.ClientRequestMessage("CertAuthExtraMethods.PublicInfo"),
                TestContext.Current.CancellationToken);

            Assert.Equal("PublicCertValue", result);
        }

        // ──────────────────────────────────────────────
        // Test 7: Mixed mode — cert and Bearer on same server
        // ──────────────────────────────────────────────

        [Fact]
        public async Task MixedMode_CertAndBearer_BothSucceed() {
            var ct = TestContext.Current.CancellationToken;

            // Client 1: connects with cert
            _connection = CreateCertConnection(_fixture.ClientCert, "/signalr/mixedauthhub");
            await _connection.StartAsync(ct);

            // Client 2: connects with Bearer token
            _connection2 = CreateTokenConnection("test-mixed-token");
            await _connection2.StartAsync(ct);

            // Both call the same method
            var certResult = await _connection.InvokeCoreAsync<string>(
                new Cocoar.SignalARRR.Common.ClientRequestMessage("MixedAuthMethods.GetAuthMethod"), ct);

            var tokenResult = await _connection2.InvokeCoreAsync<string>(
                new Cocoar.SignalARRR.Common.ClientRequestMessage("MixedAuthMethods.GetAuthMethod"), ct);

            Assert.Equal("certificate", certResult);
            Assert.Equal("token", tokenResult);
        }

        // ──────────────────────────────────────────────
        // Test 8: Mixed mode — cache expiry behavior differs
        // ──────────────────────────────────────────────

        [Fact]
        public async Task MixedMode_CertClient_CacheExpiry_RevalidatesOnMixedHub() {
            var ct = TestContext.Current.CancellationToken;

            // Cert client on MixedAuthTestHub (uses default scheme, no explicit AuthenticationSchemes)
            _connection = CreateCertConnection(_fixture.ClientCert, "/signalr/mixedauthhub");
            await _connection.StartAsync(ct);

            using var http = CreateTestHttpClient();

            // Debug: check client state before anything
            var debug1 = await http.GetStringAsync(
                $"{_fixture.ServerUrl}/__test/client-debug?connectionId={_connection.ConnectionId}", ct);

            // First call succeeds
            var certResult1 = await _connection.InvokeCoreAsync<string>(
                new Cocoar.SignalARRR.Common.ClientRequestMessage("MixedAuthMethods.GetAuthMethod"), ct);
            Assert.Equal("certificate", certResult1);

            // Expire cache
            await http.PostAsync(
                $"{_fixture.ServerUrl}/__test/expire-cert-auth-cache?connectionId={_connection.ConnectionId}",
                null, ct);

            // Debug: check client state after expiry
            var debug2 = await http.GetStringAsync(
                $"{_fixture.ServerUrl}/__test/client-debug?connectionId={_connection.ConnectionId}", ct);

            // Second call after cache expiry — should re-validate cert server-side
            try {
                var certResult2 = await _connection.InvokeCoreAsync<string>(
                    new Cocoar.SignalARRR.Common.ClientRequestMessage("MixedAuthMethods.GetAuthMethod"), ct);
                Assert.Equal("certificate", certResult2);
            } catch (Exception ex) {
                throw new Exception($"Call failed after cache expiry.\nBefore: {debug1}\nAfter expiry: {debug2}", ex);
            }
        }
    }
}
