using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
                                endpoints.MapHARRRController<AuthTestHub>("/signalr/authtesthub");
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
    }
}
