using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Cocoar.SignalARRR.Common.Attributes;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    #region Auth Test Infrastructure

    public class AuthTestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions> {
        public AuthTestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            var token = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(token)) {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> {
                new("name", "TestUser"),
                new(ClaimTypes.Role, "testrole"),
                new("access_token", token)
            };
            var identity = new ClaimsIdentity(claims, "TestScheme", "name", ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "TestScheme");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    [Authorize]
    public class AuthTestHub : HARRR {
        public AuthTestHub(IServiceProvider serviceProvider) : base(serviceProvider) { }

        public string GetName() => "AuthName";
        public Task<string> GetNameAsync() => Task.FromResult("AuthNameAsync");
    }

    [MessageName("AuthMethods")]
    public class AuthTestServerMethods : ServerMethods<AuthTestHub>, ITestServerMethods {
        public string GetName() => "AuthMethodName";
        public Task<string> GetNameAsync() => Task.FromResult("AuthMethodNameAsync");
        public Guid GetGuid() => Guid.NewGuid();
        public Task<Guid> GetGuidAsync() => Task.FromResult(Guid.NewGuid());
        public void Nothing() { }
        public Task NothingAsync() => Task.CompletedTask;
    }

    // Second ServerMethods class on the same hub — tests multi-class organization
    [MessageName("AuthExtraMethods")]
    public class AuthExtraServerMethods : ServerMethods<AuthTestHub> {

        [AllowAnonymous]
        public string PublicInfo() => "PublicValue";

        public string SecretInfo() => "SecretValue";
    }

    public class AuthTestServerFixture : IDisposable {
        private readonly IHost _host;
        public string ServerUrl { get; }

        public AuthTestServerFixture() {
            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webBuilder => {
                    webBuilder
                        .UseKestrel()
                        .UseUrls("http://127.0.0.1:0")
                        .ConfigureServices(services => {
                            services.AddRouting();
                            services.AddSignalR().AddJsonProtocol(options => {
                                options.PayloadSerializerOptions.PropertyNamingPolicy = null;
                                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                                options.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                            });

                            services.AddAuthentication("TestScheme")
                                .AddScheme<AuthenticationSchemeOptions, AuthTestAuthenticationHandler>("TestScheme", null);

                            services.AddAuthorization(options => {
                                options.AddPolicy("TestPolicy1", policy => policy.RequireRole("testrole"));
                            });

                            services.AddSignalARRR(builder => builder
                                .AddServerMethodsFrom(typeof(AuthTestHub).Assembly));
                        })
                        .Configure(app => {
                            app.UseRouting();
                            app.UseAuthentication();
                            app.UseAuthorization();
                            app.UseEndpoints(endpoints => {
                                endpoints.MapSignalARRRHub<AuthTestHub>("/signalr/authtesthub");

                                // Test trigger: expire a client's auth cache to force challenge on next call
                                endpoints.MapPost("/__test/expire-auth-cache", async context => {
                                    var connectionId = context.Request.Query["connectionId"].ToString();
                                    if (string.IsNullOrWhiteSpace(connectionId)) {
                                        context.Response.StatusCode = 400;
                                        await context.Response.WriteAsync("Missing connectionId");
                                        return;
                                    }
                                    var clientManager = context.RequestServices.GetRequiredService<ClientManager>();
                                    // Same 503-until-registered guard the transport-auth endpoints use:
                                    // OnConnectedAsync may still be in flight when a test calls this.
                                    var client = await TransportAuthTestServerFixture.RequireRegistered(
                                        context, clientManager.GetClientById(connectionId));
                                    if (client == null) {
                                        return;
                                    }
                                    client.UserValidUntil = DateTime.MinValue;
                                    await context.Response.WriteAsync("Expired");
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
        }
    }

    [CollectionDefinition("Auth")]
    public class AuthSignalARRCollection : ICollectionFixture<AuthTestServerFixture> { }

    #endregion

    [Collection("Auth")]
    public class AuthorizationTests : IAsyncLifetime {
        private readonly AuthTestServerFixture _fixture;
        private HARRRConnection? _connection;

        public AuthorizationTests(AuthTestServerFixture fixture) {
            _fixture = fixture;
        }

        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        public async ValueTask DisposeAsync() {
            if (_connection != null) {
                await _connection.StopAsync();
                await _connection.DisposeAsync();
            }
        }

        private HARRRConnection CreateConnection(string? accessToken = null) {
            return HARRRConnection.Create(builder => {
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/authtesthub", options => {
                    if (accessToken != null) {
                        options.Headers["Authorization"] = accessToken;
                    }
                });
            });
        }

        /// <summary>
        /// Configures the same credential twice on purpose: SignalR's provider authenticates the
        /// connection, SignalARRR's authenticates each message. They are separate settings — the
        /// second used to be taken from the first by reflecting into SignalR's private fields — and
        /// handing one credential to both is the common case.
        /// </summary>
        private HARRRConnection CreateConnectionWithTokenProvider(Func<Task<string?>> tokenProvider) {
            return HARRRConnection.Create(
                builder => {
                    builder.WithUrl($"{_fixture.ServerUrl}/signalr/authtesthub", options => {
                        options.AccessTokenProvider = tokenProvider;
                    });
                },
                options => options.WithAuthorization(async () => await tokenProvider() ?? string.Empty));
        }

        /// <summary>A connection that authenticates only its transport, with no per-message credential.</summary>
        private HARRRConnection CreateConnectionWithTransportTokenOnly(Func<Task<string?>> tokenProvider) {
            return HARRRConnection.Create(builder => {
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/authtesthub", options => {
                    options.AccessTokenProvider = tokenProvider;
                });
            });
        }

        [Fact]
        public async Task AuthenticatedCall_WithValidToken_Succeeds() {
            _connection = CreateConnection("Bearer test-token-123");
            await _connection.StartAsync(TestContext.Current.CancellationToken);

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = await typedClient.GetNameAsync();

            Assert.Equal("AuthMethodNameAsync", result);
        }

        [Fact]
        public async Task AuthenticatedCall_Sync_WithValidToken_Succeeds() {
            _connection = CreateConnection("Bearer test-token-123");
            await _connection.StartAsync(TestContext.Current.CancellationToken);

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = typedClient.GetName();

            Assert.Equal("AuthMethodName", result);
        }

        [Fact]
        public async Task UnauthenticatedConnection_ToAuthorizedHub_FailsToConnect() {
            _connection = CreateConnection();

            // Hub-level [Authorize] prevents unauthenticated connections at the SignalR negotiate step
            await Assert.ThrowsAnyAsync<Exception>(async () => {
                await _connection.StartAsync(TestContext.Current.CancellationToken);
            });
        }

        [Fact]
        public async Task TokenChallenge_ExpiredCache_RefreshesToken() {
            var ct = TestContext.Current.CancellationToken;
            var callCount = 0;

            // Token provider returns a fresh token on each call
            _connection = CreateConnectionWithTokenProvider(() => {
                callCount++;
                return Task.FromResult<string?>($"token-{callCount}");
            });
            await _connection.StartAsync(ct);

            // First call — uses initial token
            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result1 = await typedClient.GetNameAsync();
            Assert.Equal("AuthMethodNameAsync", result1);

            // Expire the auth cache on the server
            using var http = new HttpClient();
            var expireUrl = $"{_fixture.ServerUrl}/__test/expire-auth-cache?connectionId={_connection.ConnectionId}";
            await http.PostAsync(expireUrl, null, ct);

            // Second call — server detects expired cache, challenges client, gets fresh token
            var result2 = await typedClient.GetNameAsync();
            Assert.Equal("AuthMethodNameAsync", result2);

            // Token provider was called at least twice (initial + challenge refresh)
            Assert.True(callCount >= 2, $"Expected the authorization provider to be called at least 2 times, was called {callCount} times");
        }

        [Fact]
        public async Task WithoutAMessageCredential_TheConnectionKeepsItsNegotiatedPrincipal() {
            // A client that authenticates only its connection — no WithAuthorization — behaves the
            // way it would under plain SignalR: the principal established at negotiate carries it.
            // This used to be a flat denial once the cache expired, which caught nothing: the
            // connection is authenticated, the principal is right there, and denying hit valid
            // sessions exactly as hard as expired ones. The expiry a principal states is still
            // honoured, which is more than SignalR does — see the unit tests for that half.
            var ct = TestContext.Current.CancellationToken;
            _connection = CreateConnectionWithTransportTokenOnly(() => Task.FromResult<string?>("transport-only"));
            await _connection.StartAsync(ct);

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            Assert.Equal("AuthMethodNameAsync", await typedClient.GetNameAsync());

            using var http = new HttpClient();
            await http.PostAsync(
                $"{_fixture.ServerUrl}/__test/expire-auth-cache?connectionId={_connection.ConnectionId}", null, ct);

            Assert.Equal("AuthMethodNameAsync", await typedClient.GetNameAsync());
        }

        [Fact]
        public async Task AllowAnonymous_OnMethod_BypassesHubAuth() {
            // Connect with a token (hub requires auth) but call an [AllowAnonymous] method
            _connection = CreateConnectionWithTokenProvider(() => Task.FromResult<string?>("test-token"));
            await _connection.StartAsync(TestContext.Current.CancellationToken);

            // Call [AllowAnonymous] method on the second ServerMethods class
            var result = await _connection.InvokeCoreAsync<string>(
                new Cocoar.SignalARRR.Common.ClientRequestMessage("AuthExtraMethods.PublicInfo"),
                TestContext.Current.CancellationToken);

            Assert.Equal("PublicValue", result);
        }

        [Fact]
        public async Task SecondServerMethodsClass_OnSameHub_RequiresAuth() {
            _connection = CreateConnectionWithTokenProvider(() => Task.FromResult<string?>("test-token-secret"));
            await _connection.StartAsync(TestContext.Current.CancellationToken);

            // Call method on the second ServerMethods class (authenticated, inherits [Authorize] from hub)
            var result = await _connection.InvokeCoreAsync<string>(
                new Cocoar.SignalARRR.Common.ClientRequestMessage("AuthExtraMethods.SecretInfo"),
                TestContext.Current.CancellationToken);

            Assert.Equal("SecretValue", result);
        }
    }
}
